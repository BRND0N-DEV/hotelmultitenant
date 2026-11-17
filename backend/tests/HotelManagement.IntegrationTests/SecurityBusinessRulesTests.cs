using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using HotelManagement.Application.DTOs;
using HotelManagement.Domain.Enums;
using Xunit;

namespace HotelManagement.IntegrationTests;

/// <summary>
/// Pruebas automatizadas de escenarios críticos de seguridad y reglas de negocio:
/// - 3.2: Restricción "Con límite" de Recepción en cancelaciones y prevención de bypass de máquina de estados.
/// - 3.3: Auditor estricto solo lectura (GET autorizados 200 vs escrituras rechazadas 403).
/// - 3.4: Camarería mínimo privilegio y transiciones válidas de limpieza.
/// - 3.5: Diferencias funcionales y SoD entre Socio y Administrador Global.
/// - Revocación inmediata de usuarios desactivados.
/// </summary>
public class SecurityBusinessRulesTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public SecurityBusinessRulesTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<LoginResponseDto> AuthenticateFullAsync(string email, string password, int? hotelId = null)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password,
            PreferredHotelId = hotelId
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    }

    private async Task<string> AuthenticateAsync(string email, int hotelId = 1) =>
        (await AuthenticateFullAsync(email, "Admin123*!", hotelId)).AccessToken;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, int? hotelId = 1, object? body = null, string? idempotencyKey = null)
    {
        using var req = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (hotelId.HasValue)
        {
            req.Headers.Add("X-Hotel-Id", hotelId.Value.ToString());
        }
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        return await _client.SendAsync(req);
    }

    private async Task<(int RoomId, DateTime CheckIn, DateTime CheckOut)> FindAvailableRoomAsync(string token, int startOffsetDays, int nights)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var checkIn = DateTime.UtcNow.Date.AddDays(startOffsetDays + attempt * (nights + 1));
            var checkOut = checkIn.AddDays(nights);

            var resp = await SendAsync(HttpMethod.Post, "/api/availability/search", token, 1, new AvailabilitySearchRequestDto
            {
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                Adults = 1
            });

            if (resp.IsSuccessStatusCode)
            {
                var rooms = await resp.Content.ReadFromJsonAsync<List<AvailableRoomResponseDto>>();
                if (rooms != null && rooms.Count > 0)
                {
                    return (rooms[0].RoomId, checkIn, checkOut);
                }
            }
        }

        throw new InvalidOperationException("No se encontró habitación disponible en Sede 1 para la prueba.");
    }

    private async Task<int> GetPolicyIdByNameContainsAsync(string token, string partialName)
    {
        var resp = await SendAsync(HttpMethod.Get, "/api/rates/cancellation-policies", token, 1);
        resp.EnsureSuccessStatusCode();
        var policies = await resp.Content.ReadFromJsonAsync<List<CancellationPolicyDto>>();
        return policies!.First(p => p.Name.Contains(partialName, StringComparison.OrdinalIgnoreCase)).PolicyId;
    }

    private async Task<ReservationDto> CreateTempReservationAsync(string receptionToken, int roomId, DateTime checkIn, DateTime checkOut, int policyId)
    {
        var resp = await SendAsync(HttpMethod.Post, "/api/reservations", receptionToken, 1, new CreateReservationDto
        {
            RoomId = roomId,
            CheckInDate = checkIn,
            CheckOutDate = checkOut,
            Adults = 1,
            CancellationPolicyId = policyId,
            PrimaryGuest = new CreateGuestDto
            {
                DocumentType = "CC",
                DocumentNumber = "QA-" + Guid.NewGuid().ToString("N")[..8],
                FirstName = "Test",
                LastName = "Seguridad",
                Email = "qa.security@hotelchain.com",
                Phone = "+57 300 555 0101",
                AcceptDataConsent = true
            }
        }, idempotencyKey: Guid.NewGuid().ToString());

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<ReservationDto>())!;
    }

    // =========================================================================
    // 3.2 RESTRICCIONES DE NEGOCIO - RECEPCIÓN ("Con límite") & ESTADOS
    // =========================================================================

    [Fact]
    public async Task Reception_CancelWithinFreeWindow_ShouldSucceedWithZeroPenalty()
    {
        var receptionToken = await AuthenticateAsync("recepcion.bogota@hotelchain.com", 1);
        int flexiblePolicyId = await GetPolicyIdByNameContainsAsync(receptionToken, "Flexible");
        var (roomId, checkIn, checkOut) = await FindAvailableRoomAsync(receptionToken, startOffsetDays: 30, nights: 2);
        var created = await CreateTempReservationAsync(receptionToken, roomId, checkIn, checkOut, flexiblePolicyId);

        try
        {
            var cancelResp = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/cancel", receptionToken, 1,
                new CancelReservationDto { Reason = "Cancelación gratuita dentro de 48h de anticipación" });

            cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await cancelResp.Content.ReadAsStringAsync();
            body.Should().Contain("\"penaltyAmount\":0");

            var detailResp = await SendAsync(HttpMethod.Get, $"/api/reservations/{created.ReservationId}", receptionToken, 1);
            detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = await detailResp.Content.ReadFromJsonAsync<ReservationDetailDto>();
            detail!.Status.Should().Be(ReservationStatus.Anulada);
            detail.PenaltyAmount.Should().Be(0m);
        }
        finally
        {
            var adminSedeToken = await AuthenticateAsync("admin.bogota@hotelchain.com", 1);
            await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Cleanup" });
        }
    }

    [Fact]
    public async Task Reception_CancelOutsideFreeWindowOrNonRefundable_ShouldReturn403_WhileAdminSedeSucceedsWithPenalty()
    {
        var receptionToken = await AuthenticateAsync("recepcion.bogota@hotelchain.com", 1);
        var adminSedeToken = await AuthenticateAsync("admin.bogota@hotelchain.com", 1);

        int moderatePolicyId = await GetPolicyIdByNameContainsAsync(receptionToken, "Moderada");
        int nonRefundablePolicyId = await GetPolicyIdByNameContainsAsync(receptionToken, "No Reembolsable");

        // Caso A: Política Moderada (72h) con check-in a <24h -> fuera de ventana gratuita
        var nearIn = DateTime.UtcNow.Date.AddDays(1);
        var nearOut = nearIn.AddDays(1);
        var availNearResp = await SendAsync(HttpMethod.Post, "/api/availability/search", receptionToken, 1, new AvailabilitySearchRequestDto
        {
            CheckInDate = nearIn,
            CheckOutDate = nearOut,
            Adults = 1
        });
        availNearResp.EnsureSuccessStatusCode();
        var nearRooms = await availNearResp.Content.ReadFromJsonAsync<List<AvailableRoomResponseDto>>();
        nearRooms.Should().NotBeNullOrEmpty("debe haber al menos una habitación disponible mañana en Sede 1");

        var nearReservation = await CreateTempReservationAsync(receptionToken, nearRooms![0].RoomId, nearIn, nearOut, moderatePolicyId);

        // Caso B: Política No Reembolsable (0h, 100%) incluso con 45 días de anticipación
        var (farRoomId, farIn, farOut) = await FindAvailableRoomAsync(receptionToken, startOffsetDays: 45, nights: 2);
        var nonRefReservation = await CreateTempReservationAsync(receptionToken, farRoomId, farIn, farOut, nonRefundablePolicyId);

        try
        {
            // Recepción intenta cancelar Caso A -> 403 Forbidden
            var recCancelMod = await SendAsync(HttpMethod.Post, $"/api/reservations/{nearReservation.ReservationId}/cancel", receptionToken, 1,
                new CancelReservationDto { Reason = "Cliente solicita cancelar última hora" });
            recCancelMod.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Recepción intenta cancelar Caso B (No Reembolsable) -> 403 Forbidden
            var recCancelNonRef = await SendAsync(HttpMethod.Post, $"/api/reservations/{nonRefReservation.ReservationId}/cancel", receptionToken, 1,
                new CancelReservationDto { Reason = "Intento de anular tarifa no reembolsable" });
            recCancelNonRef.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Verificar que ninguna reserva fue alterada
            var checkMod = await (await SendAsync(HttpMethod.Get, $"/api/reservations/{nearReservation.ReservationId}", receptionToken, 1))
                .Content.ReadFromJsonAsync<ReservationDetailDto>();
            checkMod!.Status.Should().Be(ReservationStatus.Pendiente);

            // AdminSede sí puede anular ambas con liquidación de penalidad > 0
            var adminCancelMod = await SendAsync(HttpMethod.Post, $"/api/reservations/{nearReservation.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Autorizado por AdminSede con penalidad 50%" });
            adminCancelMod.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterMod = await (await SendAsync(HttpMethod.Get, $"/api/reservations/{nearReservation.ReservationId}", adminSedeToken, 1))
                .Content.ReadFromJsonAsync<ReservationDetailDto>();
            afterMod!.Status.Should().Be(ReservationStatus.Anulada);
            afterMod.PenaltyAmount.Should().BeGreaterThan(0m);

            var adminCancelNonRef = await SendAsync(HttpMethod.Post, $"/api/reservations/{nonRefReservation.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Autorizado por AdminSede con penalidad 100%" });
            adminCancelNonRef.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterNonRef = await (await SendAsync(HttpMethod.Get, $"/api/reservations/{nonRefReservation.ReservationId}", adminSedeToken, 1))
                .Content.ReadFromJsonAsync<ReservationDetailDto>();
            afterNonRef!.Status.Should().Be(ReservationStatus.Anulada);
            afterNonRef.PenaltyAmount.Should().Be(afterNonRef.TotalAmount);
        }
        finally
        {
            await SendAsync(HttpMethod.Post, $"/api/reservations/{nearReservation.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Cleanup" });
            await SendAsync(HttpMethod.Post, $"/api/reservations/{nonRefReservation.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Cleanup" });
        }
    }

    [Fact]
    public async Task ReservationStateBypassAndValidation_ShouldRejectUnauthorizedShortcutsWith422And400()
    {
        var receptionToken = await AuthenticateAsync("recepcion.bogota@hotelchain.com", 1);
        var adminSedeToken = await AuthenticateAsync("admin.bogota@hotelchain.com", 1);
        int flexiblePolicyId = await GetPolicyIdByNameContainsAsync(receptionToken, "Flexible");
        var (roomId, checkIn, checkOut) = await FindAvailableRoomAsync(receptionToken, startOffsetDays: 75, nights: 2);
        var created = await CreateTempReservationAsync(receptionToken, roomId, checkIn, checkOut, flexiblePolicyId);

        try
        {
            // 1. Intento de cancelar sin motivo -> 400 BadRequest
            var emptyReasonResp = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/cancel", receptionToken, 1,
                new CancelReservationDto { Reason = "   " });
            emptyReasonResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 2. Bypass de reglas de penalidad intentando pasar a Anulada via /state -> 422 UnprocessableEntity
            var bypassCancelResp = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/state", receptionToken, 1,
                new ChangeReservationStateDto { NewStatus = ReservationStatus.Anulada, Reason = "Bypass" });
            bypassCancelResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // 3. Bypass de Check-In (limpieza/overbooking) o Check-Out (folio=0) via /state -> 422 UnprocessableEntity
            var bypassCheckIn = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/state", receptionToken, 1,
                new ChangeReservationStateDto { NewStatus = ReservationStatus.CheckIn });
            bypassCheckIn.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            var bypassLiquidada = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/state", adminSedeToken, 1,
                new ChangeReservationStateDto { NewStatus = ReservationStatus.Liquidada });
            bypassLiquidada.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // 4. Transición válida Pendiente -> Confirmada -> NoShow -> intentar cancelar NoShow debe dar 422
            var confirmResp = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/state", receptionToken, 1,
                new ChangeReservationStateDto { NewStatus = ReservationStatus.Confirmada, Reason = "Confirmada QA" });
            confirmResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Check-out desde Confirmada con saldo pendiente debe ser bloqueado (400 BadRequest / 422 UnprocessableEntity)
            var invalidCheckout = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/check-out", receptionToken, 1);
            invalidCheckout.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Cancelar reserva válida y verificar que re-cancelar Anulada da 422
            var cancelOk = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/cancel", receptionToken, 1,
                new CancelReservationDto { Reason = "Liberación QA" });
            cancelOk.StatusCode.Should().Be(HttpStatusCode.OK);

            var recancelResp = await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Doble cancelación" });
            recancelResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await SendAsync(HttpMethod.Post, $"/api/reservations/{created.ReservationId}/cancel", adminSedeToken, 1,
                new CancelReservationDto { Reason = "Cleanup" });
        }
    }

    // =========================================================================
    // 3.3 AUDITOR - SOLO LECTURA ESTRICTO (GET OK vs POST/PUT/PATCH/DELETE 403)
    // =========================================================================

    [Fact]
    public async Task Auditor_AuthorizedReadsReturn200_AndAllWriteEndpointsRejectWith403()
    {
        var auditorToken = await AuthenticateAsync("auditor@hotelchain.com", 1);

        var allowedGets = new[]
        {
            "/api/dashboard/metrics",
            "/api/dashboard/export-revenue-trend",
            "/api/reports/transactions/excel",
            "/api/reports/guests/excel",
            "/api/audit",
            "/api/hotels",
            "/api/roomtypes",
            "/api/rooms",
            "/api/rates/seasons",
            "/api/rates/promotions",
            "/api/rates/taxes",
            "/api/rates/cancellation-policies",
            "/api/services",
            "/api/guests",
            "/api/reservations",
            "/api/availability/calendar",
            "/api/cashshift/current",
            "/api/cashshift/history"
        };

        foreach (var path in allowedGets)
        {
            var resp = await SendAsync(HttpMethod.Get, path, auditorToken, 1);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"Auditor debe poder consultar {path}");
        }

        // Auditor intentando consultar módulo administrativo de usuarios y camarería -> 403
        (await SendAsync(HttpMethod.Get, "/api/users", auditorToken, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/housekeeping/rooms", auditorToken, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Auditor intentando mutaciones reales con payloads válidos -> 403 Forbidden
        var forbiddenWrites = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, "/api/hotels", new CreateHotelDto { Code = "AUD-1", Name = "X", City = "Bogotá", Address = "X" }),
            (HttpMethod.Put, "/api/hotels/1", new UpdateHotelDto { Name = "Hijack", City = "Bogotá", Address = "X" }),
            (HttpMethod.Post, "/api/users", new CreateUserDto { Email = "aud@test.com", Password = "Admin123*!", FullName = "Aud", Role = UserRole.Auditor }),
            (HttpMethod.Delete, "/api/users/1", null),
            (HttpMethod.Post, "/api/roomtypes", new CreateRoomTypeDto { Name = "RT", Code = "AUD", BasePrice = 100000 }),
            (HttpMethod.Post, "/api/rooms", new CreateRoomDto { RoomTypeId = 1, RoomNumber = "998", Floor = 9 }),
            (HttpMethod.Patch, "/api/rooms/1/status", new UpdateRoomStatusDto { CleaningStatus = RoomCleaningStatus.Sucia }),
            (HttpMethod.Delete, "/api/rooms/1", null),
            (HttpMethod.Post, "/api/rates/seasons", new CreateSeasonDto { Name = "S", StartDate = DateTime.UtcNow.AddDays(300), EndDate = DateTime.UtcNow.AddDays(310), PriceMultiplier = 1.2m }),
            (HttpMethod.Post, "/api/rates/promotions", new CreatePromotionDto { Code = "AUD10", Name = "P", DiscountPercent = 10, StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(10) }),
            (HttpMethod.Post, "/api/services", new CreateAdditionalServiceDto { Name = "S", Category = "Spa", Price = 10000 }),
            (HttpMethod.Put, "/api/guests/1", new UpdateGuestDto { FirstName = "A", LastName = "B", Email = "a@b.com", Phone = "1" }),
            (HttpMethod.Post, "/api/guests/1/anonymize", null),
            (HttpMethod.Post, "/api/reservations", new CreateReservationDto { RoomId = 1, CheckInDate = DateTime.UtcNow.AddDays(90), CheckOutDate = DateTime.UtcNow.AddDays(92), Adults = 1 }),
            (HttpMethod.Post, "/api/reservations/1/check-out", null),
            (HttpMethod.Post, "/api/reservations/1/cancel", new CancelReservationDto { Reason = "Auditor" }),
            (HttpMethod.Post, "/api/folios/1/charges", new AddChargeDto { Description = "X", Quantity = 1, UnitPrice = 1000 }),
            (HttpMethod.Post, "/api/folios/1/payments", new AddPaymentDto { Amount = 1000, PaymentMethod = PaymentMethod.Efectivo }),
            (HttpMethod.Post, "/api/cashshift/open", new OpenShiftDto { InitialAmount = 100000 }),
            (HttpMethod.Post, "/api/cashshift/close", new CloseShiftDto { Notes = "Auditor close" }),
            (HttpMethod.Put, "/api/housekeeping/rooms/1/status", new UpdateRoomStatusDto { CleaningStatus = RoomCleaningStatus.Sucia })
        };

        foreach (var (method, path, body) in forbiddenWrites)
        {
            var resp = await SendAsync(method, path, auditorToken, 1, body);
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Auditor no debe mutar {method} {path}");
        }
    }

    // =========================================================================
    // 3.4 CAMARERÍA (HOUSEKEEPING) - MÍNIMO PRIVILEGIO Y TRANSICIONES
    // =========================================================================

    [Fact]
    public async Task Housekeeping_ShouldUpdateCleaningStatusOnly_AndRejectOperationalOrAdminActions()
    {
        var hkToken = await AuthenticateAsync("housekeeping.bogota@hotelchain.com", 1);

        // 1. Consulta permitida de habitaciones asignadas
        var listResp = await SendAsync(HttpMethod.Get, "/api/housekeeping/rooms", hkToken, 1);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rooms = await listResp.Content.ReadFromJsonAsync<List<RoomDto>>();
        rooms.Should().NotBeNullOrEmpty();

        var targetRoom = rooms!.First(r => r.OperationalStatus == RoomOperationalStatus.Disponible);
        var originalCleaning = targetRoom.CleaningStatus;

        try
        {
            // 2. Transición válida de limpieza
            var seq = new[] { RoomCleaningStatus.Sucia, RoomCleaningStatus.EnLimpieza, RoomCleaningStatus.EnRevision, RoomCleaningStatus.Limpia };
            foreach (var nextCleaning in seq)
            {
                var putResp = await SendAsync(HttpMethod.Put, $"/api/housekeeping/rooms/{targetRoom.RoomId}/status", hkToken, 1,
                    new UpdateRoomStatusDto { CleaningStatus = nextCleaning, Notes = $"QA {nextCleaning}" });
                putResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 3. Housekeeping intentando alterar OperationalStatus (Mantenimiento/FueraDeServicio/Ocupada) -> 403 Forbidden
            var escalateMaintResp = await SendAsync(HttpMethod.Put, $"/api/housekeeping/rooms/{targetRoom.RoomId}/status", hkToken, 1,
                new UpdateRoomStatusDto { OperationalStatus = RoomOperationalStatus.Mantenimiento });
            escalateMaintResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var patchRoomsResp = await SendAsync(HttpMethod.Patch, $"/api/rooms/{targetRoom.RoomId}/status", hkToken, 1,
                new UpdateRoomStatusDto { CleaningStatus = RoomCleaningStatus.Sucia });
            patchRoomsResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 4. Recepción/AdminSede no puede poner manualmente OperationalStatus = Ocupada -> 422 UnprocessableEntity
            var adminSedeToken = await AuthenticateAsync("admin.bogota@hotelchain.com", 1);
            var manualOcupadaResp = await SendAsync(HttpMethod.Patch, $"/api/rooms/{targetRoom.RoomId}/status", adminSedeToken, 1,
                new UpdateRoomStatusDto { OperationalStatus = RoomOperationalStatus.Ocupada });
            manualOcupadaResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await SendAsync(HttpMethod.Put, $"/api/housekeeping/rooms/{targetRoom.RoomId}/status", hkToken, 1,
                new UpdateRoomStatusDto { CleaningStatus = originalCleaning, Notes = "Restauración QA" });
        }
    }

    // =========================================================================
    // 3.5 SOCIO VS ADMIN GLOBAL & REVOCACIÓN DE USUARIO INACTIVO
    // =========================================================================

    [Fact]
    public async Task Socio_Vs_AdminGlobal_ShouldRespectDistinctGlobalAndSoDPermissions()
    {
        var socioToken = await AuthenticateAsync("socio@hotelchain.com", 1);
        var globalToken = await AuthenticateAsync("admin@hotelchain.com", 1);

        // Socio: acceso consolidado estratégico + reportes globales + catálogos
        var socioMetricsResp = await SendAsync(HttpMethod.Get, "/api/dashboard/metrics?hotelId=0", socioToken, 0);
        socioMetricsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var socioMetrics = await socioMetricsResp.Content.ReadFromJsonAsync<DashboardMetricsDto>();
        socioMetrics!.HotelComparisons.Count.Should().BeGreaterThanOrEqualTo(4);

        (await SendAsync(HttpMethod.Get, "/api/reports/transactions/excel?hotelId=0", socioToken, 0)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/reports/guests/excel?hotelId=0", socioToken, 0)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/hotels", socioToken, 1)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Socio: rechazado en Auditoría, Usuarios, Huéspedes, Reservas, Folios, Caja y toda escritura
        (await SendAsync(HttpMethod.Get, "/api/audit", socioToken, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/users", socioToken, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/guests", socioToken, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/reservations", socioToken, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/hotels", socioToken, 1, new CreateHotelDto { Code = "SOC", Name = "X", City = "C", Address = "A" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // AdminGlobal: acceso global + auditoría + usuarios + conmutación de sede + segregación operativa (SoD)
        var globalMetricsResp = await SendAsync(HttpMethod.Get, "/api/dashboard/metrics?hotelId=0", globalToken, 0);
        globalMetricsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/audit", globalToken, 1)).StatusCode.Should().Be(HttpStatusCode.OK);

        var switchResp = await SendAsync(HttpMethod.Post, "/api/auth/switch-hotel", globalToken, null, new { hotelId = 2 });
        switchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // SoD: AdminGlobal rechazado al intentar operar caja, reservas o limpieza
        (await SendAsync(HttpMethod.Post, "/api/reservations", globalToken, 1, new CreateReservationDto { RoomId = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/folios/1/charges", globalToken, 1, new AddChargeDto { Description = "X", Quantity = 1, UnitPrice = 100 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/cashshift/open", globalToken, 1, new OpenShiftDto { InitialAmount = 100 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/housekeeping/rooms", globalToken, 1))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeactivatedUser_ShouldBeImmediatelyRevokedAcrossJwtAndRefreshAndLogin()
    {
        var globalToken = await AuthenticateAsync("admin@hotelchain.com", 1);
        string uniqueEmail = $"qa.temp.{Guid.NewGuid():N}@hotelchain.com";

        var createUserResp = await SendAsync(HttpMethod.Post, "/api/users", globalToken, 1, new CreateUserDto
        {
            Email = uniqueEmail,
            Password = "TempUser123*!",
            FullName = "QA Temp User",
            Role = UserRole.Auditor,
            Department = "Auditoría",
            Shift = "Mañana",
            AssignedHotelIds = new List<int> { 1 }
        });
        createUserResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdUser = await createUserResp.Content.ReadFromJsonAsync<UserInfoDto>();
        createdUser.Should().NotBeNull();

        // Iniciar sesión antes de desactivar para obtener JWT y RefreshToken válidos
        var userLogin = await AuthenticateFullAsync(uniqueEmail, "TempUser123*!", 1);
        (await SendAsync(HttpMethod.Get, "/api/auth/me", userLogin.AccessToken, 1)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Desactivar usuario mediante DELETE /api/users/{id}
        var deactivateResp = await SendAsync(HttpMethod.Delete, $"/api/users/{createdUser!.UserId}", globalToken, 1);
        deactivateResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // El JWT ya emitido queda revocado inmediatamente en TenantResolutionMiddleware -> 401
        var afterTokenUseResp = await SendAsync(HttpMethod.Get, "/api/audit", userLogin.AccessToken, 1);
        afterTokenUseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var meAfterDeactivate = await SendAsync(HttpMethod.Get, "/api/auth/me", userLogin.AccessToken, 1);
        meAfterDeactivate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // RefreshToken y nuevo Login también rechazados -> 401
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequestDto
        {
            RefreshToken = userLogin.RefreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reloginResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = uniqueEmail,
            Password = "TempUser123*!"
        });
        reloginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
