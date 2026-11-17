using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using HotelManagement.Application.DTOs;
using HotelManagement.Domain.Enums;
using Xunit;

namespace HotelManagement.IntegrationTests;

/// <summary>
/// Fixture compartida que autentica una vez los 6 roles canónicos y resuelve IDs reales de la Sede 1
/// para ejecutar la matriz completa RBAC (6 roles + Anónimo × todos los endpoints protegidos).
/// </summary>
public sealed class RbacMatrixFixture : IAsyncLifetime
{
    public CustomWebApplicationFactory Factory { get; } = new();
    public HttpClient Client { get; private set; } = null!;
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);

    public int SampleReservationId { get; private set; } = 1;
    public int SampleFolioId { get; private set; } = 1;
    public int SampleGuestId { get; private set; } = 1;
    public int SampleRoomId { get; private set; } = 1;
    public int SampleRoomTypeId { get; private set; } = 1;
    public int SampleServiceId { get; private set; } = 1;
    public string SampleReservationCode { get; private set; } = string.Empty;

    private static readonly (string Role, string Email)[] CanonicalAccounts =
    {
        (UserRole.AdminGlobal, "admin@hotelchain.com"),
        (UserRole.Socio, "socio@hotelchain.com"),
        (UserRole.Auditor, "auditor@hotelchain.com"),
        (UserRole.AdminSede, "admin.bogota@hotelchain.com"),
        (UserRole.Recepcionista, "recepcion.bogota@hotelchain.com"),
        (UserRole.Housekeeping, "housekeeping.bogota@hotelchain.com")
    };

    public async Task InitializeAsync()
    {
        Client = Factory.CreateClient();

        foreach (var (role, email) in CanonicalAccounts)
        {
            var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Email = email,
                Password = "Admin123*!",
                PreferredHotelId = 1
            });
            resp.EnsureSuccessStatusCode();
            var login = await resp.Content.ReadFromJsonAsync<LoginResponseDto>();
            Tokens[role] = login!.AccessToken;
        }

        // Resolver IDs existentes reales de Sede 1
        var adminSedeToken = Tokens[UserRole.AdminSede];
        SampleReservationId = await FetchFirstReservationWithFolioAsync(adminSedeToken);
        SampleGuestId = await FetchFirstIdAsync(adminSedeToken, "/api/guests", "guestId");
        SampleRoomId = await FetchFirstIdAsync(adminSedeToken, "/api/rooms", "roomId");
        SampleRoomTypeId = await FetchFirstIdAsync(adminSedeToken, "/api/roomtypes", "roomTypeId");
        SampleServiceId = await FetchFirstIdAsync(adminSedeToken, "/api/services", "serviceId");
    }

    private async Task<int> FetchFirstReservationWithFolioAsync(string token)
    {
        using var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/reservations");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        listReq.Headers.Add("X-Hotel-Id", "1");
        var listResp = await Client.SendAsync(listReq);
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<ReservationDto>>();
        var first = list!.First();
        SampleReservationCode = first.Code;

        using var detailReq = new HttpRequestMessage(HttpMethod.Get, $"/api/reservations/{first.ReservationId}");
        detailReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        detailReq.Headers.Add("X-Hotel-Id", "1");
        var detailResp = await Client.SendAsync(detailReq);
        detailResp.EnsureSuccessStatusCode();
        var detail = await detailResp.Content.ReadFromJsonAsync<ReservationDetailDto>();
        SampleFolioId = detail!.Folio?.FolioId ?? 1;
        return first.ReservationId;
    }

    private async Task<int> FetchFirstIdAsync(string token, string path, string idProperty)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Hotel-Id", "1");
        var resp = await Client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement[0].GetProperty(idProperty).GetInt32();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Suite automatizada de matriz RBAC por endpoint y rol (6 roles + Anónimo) sobre los 16 controladores.
/// </summary>
public class RbacMatrixComprehensiveTests : IClassFixture<RbacMatrixFixture>
{
    private readonly RbacMatrixFixture _fx;
    private static readonly string[] AllRoles =
    {
        UserRole.AdminGlobal,
        UserRole.Socio,
        UserRole.Auditor,
        UserRole.AdminSede,
        UserRole.Recepcionista,
        UserRole.Housekeeping
    };

    public RbacMatrixComprehensiveTests(RbacMatrixFixture fixture)
    {
        _fx = fixture;
    }

    public static IEnumerable<object[]> GetEndpointMatrixCases()
    {
        var specs = new (string PathTemplate, string[] AllowedRoles)[]
        {
            ("/api/auth/me", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Housekeeping }),
            ("/api/hotels", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/hotels/1", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/roomtypes", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/roomtypes/{roomTypeId}", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/rooms", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/rooms/{roomId}", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/rates/seasons", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/rates/promotions", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/rates/taxes", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/rates/cancellation-policies", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/services", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/services/{serviceId}", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede, UserRole.Recepcionista }),
            ("/api/availability/calendar", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/reservations", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/reservations/{reservationId}", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/reservations/{reservationId}/voucher", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/folios/{folioId}", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/cashshift/current", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/cashshift/history", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/housekeeping/rooms", new[] { UserRole.AdminSede, UserRole.Recepcionista, UserRole.Housekeeping }),
            ("/api/guests", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/guests/{guestId}", new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista, UserRole.Auditor }),
            ("/api/dashboard/metrics", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede }),
            ("/api/dashboard/export-revenue-trend", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede }),
            ("/api/reports/transactions/excel", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede }),
            ("/api/reports/guests/excel", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede }),
            ("/api/reports/invoice/{reservationId}", new[] { UserRole.AdminGlobal, UserRole.Socio, UserRole.Auditor, UserRole.AdminSede }),
            ("/api/audit", new[] { UserRole.AdminGlobal, UserRole.Auditor, UserRole.AdminSede }),
            ("/api/users", new[] { UserRole.AdminGlobal })
        };

        foreach (var (pathTemplate, allowed) in specs)
        {
            yield return new object[] { "Anonymous", pathTemplate, false };
            foreach (var role in AllRoles)
            {
                yield return new object[] { role, pathTemplate, allowed.Contains(role) };
            }
        }
    }

    public static IEnumerable<object[]> MutatingEndpointMatrixCases()
    {
        // Usamos cuerpo JSON malformado ("{") en endpoints [FromBody] y ID inexistente (999999) en endpoints por ID:
        // [Authorize] siempre se evalúa antes de enlazar el body o consultar BD, garantizando cero contaminación de datos reales.
        var specs = new (string Method, string Path, bool HasBody, string[] AllowedRoles)[]
        {
            ("POST", "/api/auth/switch-hotel", true, AllRoles),
            ("POST", "/api/availability/search", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/availability/cross-hotel", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/cashshift/open", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/cashshift/close", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/folios/999999/charges", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/folios/charges/999999/annul", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/folios/999999/payments", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("PUT", "/api/guests/999999", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/guests/999999/anonymize", false, new[] { UserRole.AdminGlobal }),
            ("POST", "/api/hotels", true, new[] { UserRole.AdminGlobal }),
            ("PUT", "/api/hotels/999999", true, new[] { UserRole.AdminGlobal }),
            ("PUT", "/api/housekeeping/rooms/999999/status", true, new[] { UserRole.AdminSede, UserRole.Recepcionista, UserRole.Housekeeping }),
            ("POST", "/api/rates/seasons", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("POST", "/api/rates/promotions", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("POST", "/api/services", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("PUT", "/api/services/999999", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("PATCH", "/api/services/999999/toggle-status", false, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("DELETE", "/api/users/999999", false, new[] { UserRole.AdminGlobal }),
            ("POST", "/api/users", true, new[] { UserRole.AdminGlobal }),
            ("POST", "/api/reservations", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/reservations/999999/check-in", false, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/reservations/999999/check-out", false, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/reservations/999999/state", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/reservations/999999/cancel", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("POST", "/api/roomtypes", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("PUT", "/api/roomtypes/999999", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("PATCH", "/api/roomtypes/999999/toggle-status", false, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("POST", "/api/rooms", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("PUT", "/api/rooms/999999", true, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("PATCH", "/api/rooms/999999/status", true, new[] { UserRole.AdminSede, UserRole.Recepcionista }),
            ("PATCH", "/api/rooms/999999/activate", false, new[] { UserRole.AdminGlobal, UserRole.AdminSede }),
            ("DELETE", "/api/rooms/999999", false, new[] { UserRole.AdminGlobal, UserRole.AdminSede })
        };

        foreach (var (method, path, hasBody, allowed) in specs)
        {
            yield return new object[] { "Anonymous", method, path, hasBody, false };
            foreach (var role in AllRoles)
            {
                yield return new object[] { role, method, path, hasBody, allowed.Contains(role) };
            }
        }
    }

    private string ResolvePath(string template) => template
        .Replace("{reservationId}", _fx.SampleReservationId.ToString())
        .Replace("{folioId}", _fx.SampleFolioId.ToString())
        .Replace("{guestId}", _fx.SampleGuestId.ToString())
        .Replace("{roomId}", _fx.SampleRoomId.ToString())
        .Replace("{roomTypeId}", _fx.SampleRoomTypeId.ToString())
        .Replace("{serviceId}", _fx.SampleServiceId.ToString());

    [Theory]
    [MemberData(nameof(GetEndpointMatrixCases))]
    public async Task GetEndpoint_RoleMatrix_ShouldEnforceExactAuthorization(string role, string pathTemplate, bool expectedAllowed)
    {
        var path = ResolvePath(pathTemplate);
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (role != "Anonymous")
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.Tokens[role]);
            req.Headers.Add("X-Hotel-Id", "1");
        }

        var response = await _fx.Client.SendAsync(req);

        if (role == "Anonymous")
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"GET {path} sin token debe devolver 401");
        }
        else if (!expectedAllowed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Rol {role} no autorizado en GET {path} debe devolver 403");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"Rol {role} autorizado en GET {path} debe devolver 200 OK");
        }
    }

    [Theory]
    [MemberData(nameof(MutatingEndpointMatrixCases))]
    public async Task MutatingEndpoint_RoleMatrix_ShouldEnforceExactAuthorization(
        string role,
        string method,
        string path,
        bool hasBody,
        bool expectedAllowed)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (hasBody)
        {
            // JSON truncado pasa [Authorize] para roles válidos -> 400 BadRequest sin alterar datos reales
            req.Content = new StringContent("{", Encoding.UTF8, "application/json");
        }

        if (role != "Anonymous")
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.Tokens[role]);
            req.Headers.Add("X-Hotel-Id", "1");
        }

        var response = await _fx.Client.SendAsync(req);

        if (role == "Anonymous")
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path} sin token debe devolver 401");
        }
        else if (!expectedAllowed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Rol {role} no autorizado en {method} {path} debe devolver 403");
        }
        else
        {
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, $"Rol {role} autorizado pasó autenticación");
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"Rol {role} autorizado no debe ser bloqueado por RBAC en {method} {path}");
        }
    }
}

