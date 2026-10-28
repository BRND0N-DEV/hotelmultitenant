using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using HotelManagement.Application.DTOs;

namespace HotelManagement.IntegrationTests;

public class RbacAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RbacAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> AuthenticateAsync(string email, string password, int hotelId)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password,
            PreferredHotelId = hotelId
        });

        response.EnsureSuccessStatusCode();
        var loginResult = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return loginResult!.AccessToken;
    }

    [Fact]
    public async Task AnonymousUser_AccessingProtectedEndpoint_ShouldReturnUnauthorized401()
    {
        // Act: Acceso sin token Bearer
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/metrics");
        var response = await _client.SendAsync(request);

        // Assert: HTTP 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Housekeeping_AttemptingToCreateReservation_ShouldReturnForbidden403()
    {
        // Arrange
        var token = await AuthenticateAsync("housekeeping.bogota@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new CreateReservationDto
            {
                RoomId = 1,
                CheckInDate = DateTime.UtcNow.AddDays(5),
                CheckOutDate = DateTime.UtcNow.AddDays(7),
                Adults = 1
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Housekeeping_UpdatingCleaningStatus_ShouldBeAllowed()
    {
        // Arrange: Camarera actualizando estado de limpieza de una habitación
        var token = await AuthenticateAsync("housekeeping.bogota@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/housekeeping/rooms/1/status")
        {
            Content = JsonContent.Create(new UpdateRoomStatusDto
            {
                CleaningStatus = Domain.Enums.RoomCleaningStatus.Limpia,
                Notes = "Limpieza de rutina completada"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auditor_AttemptingToAddFolioCharge_ShouldReturnForbidden403()
    {
        // Arrange: Rol Auditor es de solo lectura y no puede mutar folios contables
        var token = await AuthenticateAsync("auditor@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/folios/1/charges")
        {
            Content = JsonContent.Create(new AddChargeDto
            {
                Description = "Intento de cargo fraudulento",
                Quantity = 1,
                UnitPrice = 50000
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recepcionista_AttemptingToAnonymizeGuest_ShouldReturnForbidden403()
    {
        // Arrange: Recepcionista intenta ejecutar RF-47 (Derecho al Olvido)
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/guests/1/anonymize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Solo AdminGlobal puede anonimizar -> HTTP 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recepcionista_AttemptingToViewAuditLogs_ShouldReturnForbidden403()
    {
        // Arrange: Recepcionista intenta consultar libro de auditoría
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auditor_ViewingAuditLogs_ShouldBeAllowed()
    {
        // Arrange: Auditor consultando bitácora de auditoría
        var token = await AuthenticateAsync("auditor@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminSede_QueryingChainConsolidatedDashboard_ShouldReturnForbidden403()
    {
        // Arrange: AdminSede intentando consultar dashboard global de toda la cadena (hotelId = 0)
        var token = await AuthenticateAsync("admin.bogota@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/metrics?hotelId=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Socio_QueryingChainConsolidatedDashboard_ShouldBeAllowed()
    {
        // Arrange: Socio consultando dashboard consolidado de la cadena
        var token = await AuthenticateAsync("socio@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminGlobal_AttemptingToCreateReservation_ShouldReturnForbidden403()
    {
        // Arrange: Según la matriz de permisos propuesta a la Junta, AdminGlobal no tiene acceso operativo a reservas
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new CreateReservationDto
            {
                RoomId = 1,
                CheckInDate = DateTime.UtcNow.AddDays(10),
                CheckOutDate = DateTime.UtcNow.AddDays(12),
                Adults = 2
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "1");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: HTTP 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
