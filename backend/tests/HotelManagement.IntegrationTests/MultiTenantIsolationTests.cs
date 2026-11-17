using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using HotelManagement.Application.DTOs;
using HotelManagement.Domain.Enums;
using Xunit;

namespace HotelManagement.IntegrationTests;

/// <summary>
/// Fixture multitenant que autentica cuentas de Sede 1 (Bogotá) y Sede 2 (Cartagena)
/// y resuelve identificadores reales de recursos pertenecientes exclusivamente a Sede 2.
/// </summary>
public sealed class MultiTenantFixture : IAsyncLifetime
{
    public CustomWebApplicationFactory Factory { get; } = new();
    public HttpClient Client { get; private set; } = null!;

    public string BogotaAdminToken { get; private set; } = string.Empty;
    public string BogotaReceptionToken { get; private set; } = string.Empty;
    public string CartagenaAdminToken { get; private set; } = string.Empty;
    public string GlobalAdminToken { get; private set; } = string.Empty;

    public int BogotaFolioId { get; private set; } = 1;
    public int CartagenaRoomTypeId { get; private set; }
    public int CartagenaRoomId { get; private set; }
    public int CartagenaServiceId { get; private set; }
    public int CartagenaReservationId { get; private set; }
    public int CartagenaFolioId { get; private set; }
    public int CartagenaGuestId { get; private set; }
    public int? CartagenaChargeId { get; private set; }

    public async Task InitializeAsync()
    {
        Client = Factory.CreateClient();

        BogotaAdminToken = await LoginAsync("admin.bogota@hotelchain.com", 1);
        BogotaReceptionToken = await LoginAsync("recepcion.bogota@hotelchain.com", 1);
        CartagenaAdminToken = await LoginAsync("admin.cartagena@hotelchain.com", 2);
        GlobalAdminToken = await LoginAsync("admin@hotelchain.com", 1);

        // Folio real de Sede 1
        var resBog = await SendGetAsync<List<ReservationDto>>(BogotaAdminToken, "/api/reservations", 1);
        var detailBog = await SendGetAsync<ReservationDetailDto>(BogotaAdminToken, $"/api/reservations/{resBog!.First().ReservationId}", 1);
        BogotaFolioId = detailBog!.Folio!.FolioId;

        // Recursos reales de Sede 2 (Cartagena) obtenidos legítimamente por su AdminSede
        var rtCtg = await SendGetAsync<List<RoomTypeDto>>(CartagenaAdminToken, "/api/roomtypes", 2);
        CartagenaRoomTypeId = rtCtg!.First().RoomTypeId;

        var rmCtg = await SendGetAsync<List<RoomDto>>(CartagenaAdminToken, "/api/rooms", 2);
        CartagenaRoomId = rmCtg!.First().RoomId;

        var svcCtg = await SendGetAsync<List<AdditionalServiceDto>>(CartagenaAdminToken, "/api/services", 2);
        CartagenaServiceId = svcCtg!.First().ServiceId;

        var resCtg = await SendGetAsync<List<ReservationDto>>(CartagenaAdminToken, "/api/reservations", 2);
        CartagenaReservationId = resCtg!.First().ReservationId;

        var detailCtg = await SendGetAsync<ReservationDetailDto>(CartagenaAdminToken, $"/api/reservations/{CartagenaReservationId}", 2);
        CartagenaGuestId = detailCtg!.GuestId;
        CartagenaFolioId = detailCtg.Folio!.FolioId;
        CartagenaChargeId = detailCtg.Folio.Charges.FirstOrDefault()?.ChargeId;
    }

    private async Task<string> LoginAsync(string email, int preferredHotelId)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = "Admin123*!",
            PreferredHotelId = preferredHotelId
        });
        resp.EnsureSuccessStatusCode();
        var data = await resp.Content.ReadFromJsonAsync<LoginResponseDto>();
        return data!.AccessToken;
    }

    private async Task<T?> SendGetAsync<T>(string token, string path, int hotelId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Hotel-Id", hotelId.ToString());
        var resp = await Client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Pruebas de aislamiento multitenant y prevención de IDOR (OWASP API1 BOLA) para AdminSede y roles operativos.
/// </summary>
public class MultiTenantIsolationTests : IClassFixture<MultiTenantFixture>
{
    private readonly MultiTenantFixture _fx;

    public MultiTenantIsolationTests(MultiTenantFixture fixture)
    {
        _fx = fixture;
    }

    public static IEnumerable<object[]> TamperedHeaderEndpoints() => new[]
    {
        new object[] { "/api/hotels" },
        new object[] { "/api/hotels/2" },
        new object[] { "/api/roomtypes" },
        new object[] { "/api/rooms" },
        new object[] { "/api/rates/seasons" },
        new object[] { "/api/rates/promotions" },
        new object[] { "/api/rates/taxes" },
        new object[] { "/api/rates/cancellation-policies" },
        new object[] { "/api/services" },
        new object[] { "/api/guests" },
        new object[] { "/api/reservations" },
        new object[] { "/api/availability/calendar" },
        new object[] { "/api/cashshift/current" },
        new object[] { "/api/cashshift/history" },
        new object[] { "/api/dashboard/metrics" },
        new object[] { "/api/reports/transactions/excel" },
        new object[] { "/api/reports/guests/excel" },
        new object[] { "/api/audit" }
    };

    [Theory]
    [MemberData(nameof(TamperedHeaderEndpoints))]
    public async Task AdminSede_Bogota_ForcingCartagenaHeader_ShouldReturn404NotFound(string endpoint)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        req.Headers.Add("X-Hotel-Id", "2");

        var resp = await _fx.Client.SendAsync(req);

        resp.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            $"AdminSede de Bogotá manipulando X-Hotel-Id:2 en {endpoint} no debe revelar la existencia de recursos ajenos");
    }

    [Fact]
    public async Task NonGlobalUser_SendingMalformedHotelHeader_ShouldReturn400BadRequest()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/rooms");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        req.Headers.Add("X-Hotel-Id", "invalid-tenant-id");

        var resp = await _fx.Client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("GET", "/api/rooms?hotelId=2")]
    [InlineData("GET", "/api/roomtypes?hotelId=2")]
    [InlineData("GET", "/api/rates/seasons?hotelId=2")]
    [InlineData("GET", "/api/rates/promotions?hotelId=2")]
    [InlineData("GET", "/api/rates/taxes?hotelId=2")]
    [InlineData("GET", "/api/rates/cancellation-policies?hotelId=2")]
    [InlineData("GET", "/api/services?hotelId=2")]
    [InlineData("GET", "/api/dashboard/metrics?hotelId=2")]
    [InlineData("GET", "/api/dashboard/metrics?hotelId=0")]
    [InlineData("GET", "/api/dashboard/export-revenue-trend?hotelId=2")]
    [InlineData("GET", "/api/reports/transactions/excel?hotelId=2")]
    [InlineData("GET", "/api/reports/guests/excel?hotelId=2")]
    [InlineData("GET", "/api/audit?hotelId=2")]
    [InlineData("POST", "/api/rooms?hotelId=2")]
    [InlineData("POST", "/api/roomtypes?hotelId=2")]
    [InlineData("POST", "/api/rates/seasons?hotelId=2")]
    [InlineData("POST", "/api/rates/promotions?hotelId=2")]
    [InlineData("POST", "/api/services?hotelId=2")]
    public async Task AdminSede_Bogota_TamperingQueryHotelId_ShouldReturn403Forbidden(string method, string pathWithQuery)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), pathWithQuery);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        req.Headers.Add("X-Hotel-Id", "1");
        if (method == "POST")
        {
            req.Content = JsonContent.Create(new { name = "TamperTest" });
        }

        var resp = await _fx.Client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"manipular ?hotelId= en {method} {pathWithQuery} debe rechazarse con 403");
    }

    [Fact]
    public async Task AdminSede_Bogota_DirectIdorOnCartagenaResources_ShouldAlwaysBeRejected()
    {
        var probes = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/hotels/2", null),
            (HttpMethod.Get, $"/api/roomtypes/{_fx.CartagenaRoomTypeId}", null),
            (HttpMethod.Put, $"/api/roomtypes/{_fx.CartagenaRoomTypeId}", new UpdateRoomTypeDto { Name = "Hijack", Code = "HJK", BaseCapacity = 1, MaxCapacity = 2, BasePrice = 100000 }),
            (HttpMethod.Patch, $"/api/roomtypes/{_fx.CartagenaRoomTypeId}/toggle-status", null),
            (HttpMethod.Get, $"/api/rooms/{_fx.CartagenaRoomId}", null),
            (HttpMethod.Put, $"/api/rooms/{_fx.CartagenaRoomId}", new UpdateRoomDto { RoomTypeId = _fx.CartagenaRoomTypeId, RoomNumber = "999", Floor = 9 }),
            (HttpMethod.Patch, $"/api/rooms/{_fx.CartagenaRoomId}/status", new UpdateRoomStatusDto { CleaningStatus = RoomCleaningStatus.Sucia }),
            (HttpMethod.Patch, $"/api/rooms/{_fx.CartagenaRoomId}/activate", null),
            (HttpMethod.Delete, $"/api/rooms/{_fx.CartagenaRoomId}", null),
            (HttpMethod.Get, $"/api/services/{_fx.CartagenaServiceId}", null),
            (HttpMethod.Put, $"/api/services/{_fx.CartagenaServiceId}", new UpdateAdditionalServiceDto { Name = "Hijack", Category = "Spa", Price = 10000 }),
            (HttpMethod.Patch, $"/api/services/{_fx.CartagenaServiceId}/toggle-status", null),
            (HttpMethod.Get, $"/api/reservations/{_fx.CartagenaReservationId}", null),
            (HttpMethod.Get, $"/api/reservations/{_fx.CartagenaReservationId}/voucher", null),
            (HttpMethod.Post, $"/api/reservations/{_fx.CartagenaReservationId}/check-in", null),
            (HttpMethod.Post, $"/api/reservations/{_fx.CartagenaReservationId}/cancel", new CancelReservationDto { Reason = "IDOR test" }),
            (HttpMethod.Get, $"/api/reports/invoice/{_fx.CartagenaReservationId}", null),
            (HttpMethod.Get, $"/api/folios/{_fx.CartagenaFolioId}", null),
            (HttpMethod.Post, $"/api/folios/{_fx.CartagenaFolioId}/charges", new AddChargeDto { Description = "IDOR", Quantity = 1, UnitPrice = 10000 }),
            (HttpMethod.Post, $"/api/folios/{_fx.CartagenaFolioId}/payments", new AddPaymentDto { Amount = 10000, PaymentMethod = PaymentMethod.Efectivo }),
            (HttpMethod.Get, $"/api/guests/{_fx.CartagenaGuestId}", null),
            (HttpMethod.Put, $"/api/guests/{_fx.CartagenaGuestId}", new UpdateGuestDto { FirstName = "Hijack", LastName = "Test", Email = "x@y.com", Phone = "123" }),
            (HttpMethod.Put, $"/api/housekeeping/rooms/{_fx.CartagenaRoomId}/status", new UpdateRoomStatusDto { CleaningStatus = RoomCleaningStatus.Sucia })
        };

        foreach (var (method, path, body) in probes)
        {
            using var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
            req.Headers.Add("X-Hotel-Id", "1");
            if (body != null) req.Content = JsonContent.Create(body);

            var resp = await _fx.Client.SendAsync(req);
            resp.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                $"IDOR {method} {path} sobre recurso de Sede 2 desde AdminSede de Sede 1 debe devolver 404 NotFound");
        }

        if (_fx.CartagenaChargeId.HasValue)
        {
            using var annulReq = new HttpRequestMessage(HttpMethod.Post, $"/api/folios/charges/{_fx.CartagenaChargeId.Value}/annul")
            {
                Content = JsonContent.Create(new AnnulChargeDto { Reason = "IDOR charge test" })
            };
            annulReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
            annulReq.Headers.Add("X-Hotel-Id", "1");
            var annulResp = await _fx.Client.SendAsync(annulReq);
            annulResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task CrossTenantForeignKeyInjection_ShouldBeBlocked()
    {
        // 1. AdminSede Bogotá intenta crear habitación física referenciando RoomTypeId de Cartagena
        using var roomReq = new HttpRequestMessage(HttpMethod.Post, "/api/rooms")
        {
            Content = JsonContent.Create(new CreateRoomDto
            {
                RoomTypeId = _fx.CartagenaRoomTypeId,
                RoomNumber = "X999",
                Floor = 9
            })
        };
        roomReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        roomReq.Headers.Add("X-Hotel-Id", "1");
        var roomResp = await _fx.Client.SendAsync(roomReq);
        roomResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Recepcionista Bogotá intenta crear reserva sobre RoomId de Cartagena
        using var resReq = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new CreateReservationDto
            {
                RoomId = _fx.CartagenaRoomId,
                CheckInDate = DateTime.UtcNow.Date.AddDays(50),
                CheckOutDate = DateTime.UtcNow.Date.AddDays(52),
                Adults = 1,
                CancellationPolicyId = 1,
                PrimaryGuest = new CreateGuestDto
                {
                    DocumentType = "CC",
                    DocumentNumber = "90000111",
                    FirstName = "Cross",
                    LastName = "Tenant",
                    Email = "cross@test.com",
                    Phone = "3000000000",
                    AcceptDataConsent = true
                }
            })
        };
        resReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaReceptionToken);
        resReq.Headers.Add("X-Hotel-Id", "1");
        var resResp = await _fx.Client.SendAsync(resReq);
        resResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 3. Recepcionista Bogotá intenta cargar un ServiceId de Cartagena a un folio de Bogotá
        using var chargeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/folios/{_fx.BogotaFolioId}/charges")
        {
            Content = JsonContent.Create(new AddChargeDto
            {
                ServiceId = _fx.CartagenaServiceId,
                Description = "Servicio cross-tenant",
                Quantity = 1,
                UnitPrice = 0
            })
        };
        chargeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaReceptionToken);
        chargeReq.Headers.Add("X-Hotel-Id", "1");
        var chargeResp = await _fx.Client.SendAsync(chargeReq);
        chargeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminSede_QueriesAndDashboard_ShouldOnlyReturnOwnHotelDataWithoutLeakage()
    {
        // 1. /api/hotels solo retorna Sede 1
        using var hotelsReq = new HttpRequestMessage(HttpMethod.Get, "/api/hotels");
        hotelsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        hotelsReq.Headers.Add("X-Hotel-Id", "1");
        var hotelsResp = await _fx.Client.SendAsync(hotelsReq);
        hotelsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hotels = await hotelsResp.Content.ReadFromJsonAsync<List<HotelDto>>();
        hotels.Should().NotBeNull();
        hotels!.Should().ContainSingle(h => h.HotelId == 1);

        // 2. /api/dashboard/metrics no debe filtrar comparativas financieras de otras sedes
        using var dashReq = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/metrics");
        dashReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        dashReq.Headers.Add("X-Hotel-Id", "1");
        var dashResp = await _fx.Client.SendAsync(dashReq);
        dashResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = await dashResp.Content.ReadFromJsonAsync<DashboardMetricsDto>();
        metrics.Should().NotBeNull();
        metrics!.HotelComparisons.Should().OnlyContain(c => c.HotelId == 1);
        metrics.HotelComparisons.Count.Should().BeLessThanOrEqualTo(1);

        // 3. /api/audit solo retorna registros de Sede 1 (excluye logs globales o de otras sedes)
        using var auditReq = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.BogotaAdminToken);
        auditReq.Headers.Add("X-Hotel-Id", "1");
        var auditResp = await _fx.Client.SendAsync(auditReq);
        auditResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await auditResp.Content.ReadAsStringAsync());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("hotelId", out var hotelProp) && hotelProp.ValueKind != JsonValueKind.Null)
            {
                hotelProp.GetInt32().Should().Be(1);
            }
            if (item.TryGetProperty("hotelName", out var hotelNameProp) && hotelNameProp.ValueKind == JsonValueKind.String)
            {
                hotelNameProp.GetString().Should().NotContain("Cartagena").And.NotContain("Medellín").And.NotContain("Cali");
            }
        }
    }

    [Fact]
    public async Task AuthLoginAndRefresh_WithUnassignedHotelId_ShouldReturn403Forbidden()
    {
        var loginResp = await _fx.Client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "admin.bogota@hotelchain.com",
            Password = "Admin123*!",
            PreferredHotelId = 2
        });
        loginResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var validLoginResp = await _fx.Client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "admin.bogota@hotelchain.com",
            Password = "Admin123*!",
            PreferredHotelId = 1
        });
        validLoginResp.EnsureSuccessStatusCode();
        var validLogin = await validLoginResp.Content.ReadFromJsonAsync<LoginResponseDto>();

        var refreshResp = await _fx.Client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequestDto
        {
            RefreshToken = validLogin!.RefreshToken,
            ActiveHotelId = 2
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminSede_Cartagena_AuthorizedAccessToOwnHotel_ShouldSucceedWith200()
    {
        using var hotelReq = new HttpRequestMessage(HttpMethod.Get, "/api/hotels/2");
        hotelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.CartagenaAdminToken);
        hotelReq.Headers.Add("X-Hotel-Id", "2");
        var hotelResp = await _fx.Client.SendAsync(hotelReq);
        hotelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var roomsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/rooms/{_fx.CartagenaRoomId}");
        roomsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.CartagenaAdminToken);
        roomsReq.Headers.Add("X-Hotel-Id", "2");
        var roomsResp = await _fx.Client.SendAsync(roomsReq);
        roomsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var dashReq = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/metrics?hotelId=2");
        dashReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.CartagenaAdminToken);
        dashReq.Headers.Add("X-Hotel-Id", "2");
        var dashResp = await _fx.Client.SendAsync(dashReq);
        dashResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

