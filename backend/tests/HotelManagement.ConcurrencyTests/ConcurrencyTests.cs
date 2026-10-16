using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using HotelManagement.Application.DTOs;

namespace HotelManagement.ConcurrencyTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
}

public class ConcurrencyAndOverbookingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConcurrencyAndOverbookingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "recepcion.bogota@hotelchain.com",
            Password = "Admin123*!",
            PreferredHotelId = 1
        });

        response.EnsureSuccessStatusCode();
        var loginResult = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return loginResult!.AccessToken;
    }

    // RF-24, RNF-02, RNF-11: Prevención estricta de sobreventa bajo concurrencia real
    [Fact]
    public async Task TwoSimultaneousBookings_ForSameRoomAndDates_OnlyOneMustSucceed_OtherMustBe409Conflict()
    {
        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        var token1 = await AuthenticateAsync(client1);
        var token2 = await AuthenticateAsync(client2);

        // Ambas peticiones intentan reservar la Habitación 104 (RoomId 4) para las mismas fechas
        int randomOffset = new Random().Next(60, 500);
        var startDate = DateTime.UtcNow.Date.AddDays(randomOffset);
        var endDate = startDate.AddDays(3);

        var booking1 = new CreateReservationDto
        {
            RoomId = 4,
            CheckInDate = startDate,
            CheckOutDate = endDate,
            Adults = 1,
            CancellationPolicyId = 1,
            PrimaryGuest = new CreateGuestDto
            {
                DocumentType = "CC",
                DocumentNumber = "CONCURRENCY_GUEST_A",
                FirstName = "Usuario",
                LastName = "Concurrente A",
                Email = "concurrente.a@test.com",
                Phone = "+57 311 000 1111",
                AcceptDataConsent = true
            }
        };

        var booking2 = new CreateReservationDto
        {
            RoomId = 4,
            CheckInDate = startDate,
            CheckOutDate = endDate,
            Adults = 1,
            CancellationPolicyId = 1,
            PrimaryGuest = new CreateGuestDto
            {
                DocumentType = "CC",
                DocumentNumber = "CONCURRENCY_GUEST_B",
                FirstName = "Usuario",
                LastName = "Concurrente B",
                Email = "concurrente.b@test.com",
                Phone = "+57 311 000 2222",
                AcceptDataConsent = true
            }
        };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/reservations") { Content = JsonContent.Create(booking1) };
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        req1.Headers.Add("X-Hotel-Id", "1");

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/reservations") { Content = JsonContent.Create(booking2) };
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        req2.Headers.Add("X-Hotel-Id", "1");

        // Act: Disparar ambas peticiones exactamente al mismo tiempo
        var task1 = client1.SendAsync(req1);
        var task2 = client2.SendAsync(req2);

        await Task.WhenAll(task1, task2);

        var res1 = await task1;
        var res2 = await task2;

        string c1 = await res1.Content.ReadAsStringAsync();
        string c2 = await res2.Content.ReadAsStringAsync();

        if (res1.StatusCode != HttpStatusCode.Created && res1.StatusCode != HttpStatusCode.Conflict)
        {
            throw new Exception($"Client1 returned {res1.StatusCode}: {c1}");
        }
        if (res2.StatusCode != HttpStatusCode.Created && res2.StatusCode != HttpStatusCode.Conflict)
        {
            throw new Exception($"Client2 returned {res2.StatusCode}: {c2}");
        }

        var statuses = new List<HttpStatusCode> { res1.StatusCode, res2.StatusCode };
        statuses.Should().Contain(HttpStatusCode.Created);
        statuses.Should().Contain(HttpStatusCode.Conflict);
    }
}
