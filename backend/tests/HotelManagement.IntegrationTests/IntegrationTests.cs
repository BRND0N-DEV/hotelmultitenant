using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using HotelManagement.Application.DTOs;

namespace HotelManagement.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Utiliza la base de datos SQL Server HotelManagementDb ya migrada y sembrada
}

public class HotelApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public HotelApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> AuthenticateAsync(string email, string password, int hotelId)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
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
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnJwtAndAssignedHotels()
    {
        // Act: Login con credenciales de recepción Bogotá
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "recepcion.bogota@hotelchain.com",
            Password = "Admin123*!",
            PreferredHotelId = 1
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.User.Role.Should().Be("Recepcionista");
        result.User.ActiveHotelId.Should().Be(1);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShouldFailWithRemainingAttempts()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "recepcion.bogota@hotelchain.com",
            Password = "WrongPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Contraseña incorrecta");
    }

    // RF-07: Aislamiento multitenant - Usuario de Bogotá intentando acceder con X-Hotel-Id de otra sede no autorizada recibe 404
    [Fact]
    public async Task Multitenancy_AccessOtherHotel_ShouldReturnNotFound404()
    {
        // Arrange: Recepcionista de Bogotá intenta consultar habitaciones forzando X-Hotel-Id de Cartagena (2)
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Hotel-Id", "2"); // Hotel Caribe Real Cartagena

        // Act
        var response = await _client.SendAsync(request);

        // Assert: RF-07 exige retornar 404 para no divulgar recursos de otra sede
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // RF-20, RF-21: Consulta inter-sedes protegida
    [Fact]
    public async Task CrossHotelAvailability_ShouldReturnSummariesWithoutSensitiveData()
    {
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);
        var searchDto = new AvailabilitySearchRequestDto
        {
            CheckInDate = DateTime.UtcNow.Date.AddDays(10),
            CheckOutDate = DateTime.UtcNow.Date.AddDays(12),
            Adults = 2
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/availability/cross-hotel")
        {
            Content = JsonContent.Create(searchDto)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await response.Content.ReadFromJsonAsync<List<CrossHotelAvailabilityResponseDto>>();
        summaries.Should().NotBeNull();
        summaries!.Count.Should().BeGreaterThanOrEqualTo(4);

        // Verificar que solo contiene sede, tipo de habitación, cantidad disponible y tarifa referencial
        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().NotContain("Guest");
        jsonString.Should().NotContain("DocumentNumber");
        jsonString.Should().NotContain("Folio");
    }

    // RF-23: Idempotencia en creación de reservas
    [Fact]
    public async Task CreateReservation_WithSameIdempotencyKey_ShouldReturnCachedResponse()
    {
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);
        string idempotencyKey = Guid.NewGuid().ToString();

        int offset = new Random().Next(100, 500);
        var reservationDto = new CreateReservationDto
        {
            RoomId = 3, // Habitación 103 (Doble Superior)
            CheckInDate = DateTime.UtcNow.Date.AddDays(offset),
            CheckOutDate = DateTime.UtcNow.Date.AddDays(offset + 3),
            Adults = 2,
            CancellationPolicyId = 1,
            PrimaryGuest = new CreateGuestDto
            {
                DocumentType = "CC",
                DocumentNumber = "77889900",
                FirstName = "Carlos",
                LastName = "Prueba",
                Email = "carlos.prueba@test.com",
                Phone = "+57 300 111 2233",
                AcceptDataConsent = true // RF-45
            }
        };

        // Primera petición
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(reservationDto)
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request1.Headers.Add("Idempotency-Key", idempotencyKey);
        request1.Headers.Add("X-Hotel-Id", "1");

        var response1 = await _client.SendAsync(request1);
        if (response1.StatusCode != HttpStatusCode.Created)
        {
            var err = await response1.Content.ReadAsStringAsync();
            throw new Exception($"API Error: {err}");
        }
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        var res1 = await response1.Content.ReadFromJsonAsync<ReservationDto>();

        // Segunda petición idéntica con la misma clave de idempotencia
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(reservationDto)
        };
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request2.Headers.Add("Idempotency-Key", idempotencyKey);
        request2.Headers.Add("X-Hotel-Id", "1");

        var response2 = await _client.SendAsync(request2);

        // Assert: Misma respuesta y mismo código de reserva sin crear duplicado
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
        var res2 = await response2.Content.ReadFromJsonAsync<ReservationDto>();
        res2!.Code.Should().Be(res1!.Code);
        res2.ReservationId.Should().Be(res1.ReservationId);
    }

    [Fact]
    public async Task VerifyVoucher_PublicEndpoint_ShouldReturnHtmlWithoutAuthentication()
    {
        // 1. Obtener una reserva existente del hotel
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);
        var resReq = new HttpRequestMessage(HttpMethod.Get, "/api/reservations");
        resReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        resReq.Headers.Add("X-Hotel-Id", "1");
        var resResp = await _client.SendAsync(resReq);
        resResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resResp.Content.ReadFromJsonAsync<List<ReservationDto>>();
        list.Should().NotBeNull();
        list!.Should().NotBeEmpty();

        var reservation = list!.First();

        // 2. Probar el endpoint público de verificación en formato JSON
        var unauthenticatedClient = _factory.CreateClient();
        var jsonResp = await unauthenticatedClient.GetAsync($"/api/reservations/verify-voucher/{reservation.Code}");
        jsonResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonContent = await jsonResp.Content.ReadAsStringAsync();
        jsonContent.Should().Contain(reservation.Code);
        jsonContent.Should().Contain(reservation.HotelName);
        jsonContent.Should().NotContain("PasswordHash");
        jsonContent.Should().NotContain("RefreshToken");

        // 3. Probar el endpoint público de verificación solicitando HTML (como navegador móvil al escanear el QR)
        var htmlReq = new HttpRequestMessage(HttpMethod.Get, $"/api/reservations/verify-voucher/{reservation.Code}");
        htmlReq.Headers.Add("Accept", "text/html");
        var htmlResp = await unauthenticatedClient.SendAsync(htmlReq);
        htmlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var htmlContent = await htmlResp.Content.ReadAsStringAsync();
        htmlContent.Should().Contain("Comprobante Oficial de Reserva");
        htmlContent.Should().Contain(reservation.Code);
        htmlContent.Should().Contain(reservation.HotelName);
        htmlContent.Should().NotContain("PasswordHash");
        htmlContent.Should().NotContain("RefreshToken");
    }

    [Fact]
    public async Task SwitchHotel_AdminGlobal_ShouldSwitchActiveHotel()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-hotel")
        {
            Content = JsonContent.Create(2)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: $"Response was: {content}");
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        result.Should().NotBeNull();
        result!.User.ActiveHotelId.Should().Be(2);
    }

    [Fact]
    public async Task SwitchHotel_WithStringOrObjectPayload_TestingWhatHappens()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-hotel")
        {
            Content = new StringContent("\"2\"", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: $"Response for string \"2\" was: {response.StatusCode} - {content}");
    }
}



