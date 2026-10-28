using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using HotelManagement.Application.DTOs;
using Xunit;

namespace HotelManagement.IntegrationTests;

public class NewFunctionalRequirementsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public NewFunctionalRequirementsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> AuthenticateAsync(string email, string password, int? preferredHotelId = null)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password,
            PreferredHotelId = preferredHotelId
        });

        response.EnsureSuccessStatusCode();
        var loginResult = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return loginResult!.AccessToken;
    }

    // =========================================================================
    // 1. CAMBIO DE SEDE DEL ADMINISTRADOR GLOBAL (AUTH / CONMUTACIÓN)
    // =========================================================================

    [Fact]
    public async Task SwitchHotel_AdminGlobal_ShouldSwitchHotelAndReturnUpdatedContext()
    {
        // 1. Admin Global inicia sesión en Hotel 1 (Bogotá)
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-hotel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { hotelId = 2 }); // Conmutar a Cartagena (2)

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.User.ActiveHotelId.Should().Be(2);
        result.User.Role.Should().Be("AdminGlobal");
        result.User.AssignedHotels.Should().NotBeEmpty();
        result.User.AssignedHotels.Should().Contain(h => h.HotelId == 1);
        result.User.AssignedHotels.Should().Contain(h => h.HotelId == 2);
    }

    [Fact]
    public async Task SwitchHotel_AdminGlobal_BackToHotel1_ShouldSucceed()
    {
        // Conmutar a Hotel 2 y luego volver a Hotel 1
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 2);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-hotel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { hotelId = 1 });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        result!.User.ActiveHotelId.Should().Be(1);
    }

    [Fact]
    public async Task SwitchHotel_HotelAdmin_ToOtherHotel_ShouldReturnForbidden403()
    {
        // Admin Sede Bogotá (Hotel 1) intenta conmutar a Cartagena (Hotel 2)
        var token = await AuthenticateAsync("admin.bogota@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-hotel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { hotelId = 2 });

        var response = await _client.SendAsync(request);

        // Debe ser 403 Forbidden, protegiendo el aislamiento multitenant
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("No tiene autorización");
    }

    [Fact]
    public async Task SwitchHotel_NonExistentHotel_ShouldReturnNotFound404()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-hotel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { hotelId = 99999 });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // 2. EXPORTACIÓN DASHBOARD GLOBAL & ADMIN SEDE (CSV / BOM)
    // =========================================================================

    [Fact]
    public async Task ExportRevenueTrend_AdminGlobal_Consolidated_ShouldReturnCsvWithBom()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/export-revenue-trend?hotelId=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // Verificar presencia de BOM UTF-8 (EF, BB, BF) para compatibilidad con Excel
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var content = System.Text.Encoding.UTF8.GetString(bytes);
        content.Should().Contain("CONTINENTALIA SAS - INFORME GERENCIAL Y TENDENCIA DE INGRESOS (7 DÍAS)");
        content.Should().Contain("TENDENCIA DE INGRESOS & MOVIMIENTOS (7 DÍAS)");
        content.Should().Contain("Fecha,Sede / Alcance,Check-Ins,Check-Outs,Ingresos Recaudados COP");
    }

    [Fact]
    public async Task ExportRevenueTrend_HotelAdmin_OwnHotel_ShouldSucceed()
    {
        var token = await AuthenticateAsync("admin.bogota@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/export-revenue-trend?hotelId=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        content.Should().Contain("Grand Oasis Bogotá");
    }

    [Fact]
    public async Task ExportRevenueTrend_HotelAdmin_TamperingWithAnotherHotel_ShouldReturnForbidden403()
    {
        // Admin Sede Bogotá (Hotel 1) manipula parámetros HTTP intentando exportar Cartagena (Hotel 2)
        var token = await AuthenticateAsync("admin.bogota@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/export-revenue-trend?hotelId=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        // El backend debe rechazar el intento con 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportRevenueTrend_HotelAdmin_TamperingWithConsolidated_ShouldReturnForbidden403()
    {
        // Admin Sede Bogotá intenta exportar la consolidada general (hotelId=0)
        var token = await AuthenticateAsync("admin.bogota@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/export-revenue-trend?hotelId=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportRevenueTrend_Receptionist_ShouldReturnForbidden403()
    {
        // Rol Recepcionista no tiene acceso a reportes de dashboard gerencial
        var token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/export-revenue-trend?hotelId=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================
    // 3. PARAMETRIZACIÓN: TIPOLOGÍAS DE HABITACIÓN & TARIFAS
    // =========================================================================

    [Fact]
    public async Task RoomType_CreateUpdateAndToggle_ShouldPersistCorrectly()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        // 1. Crear nueva tipología
        var uniqueCode = "VIP-" + Guid.NewGuid().ToString("N")[..4].ToUpper();
        var createDto = new CreateRoomTypeDto
        {
            Code = uniqueCode,
            Name = "Suite Presidencial Test",
            BaseCapacity = 2,
            MaxCapacity = 4,
            BasePrice = 350000,
            Amenities = "Wi-Fi, Jacuzzi, Balcón, Smart TV",
            Description = "Cama King Size, 45 m², vista panorámica"
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/roomtypes");
        postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        postRequest.Content = JsonContent.Create(createDto);

        var postResponse = await _client.SendAsync(postRequest);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<RoomTypeDto>();
        created.Should().NotBeNull();
        created!.Code.Should().Be(uniqueCode);
        created.BasePrice.Should().Be(350000);
        created.IsActive.Should().BeTrue();

        // 2. Modificar atributos físicos y comerciales
        var updateDto = new UpdateRoomTypeDto
        {
            Code = uniqueCode,
            Name = "Suite Presidencial Test Modificada",
            BaseCapacity = 3,
            MaxCapacity = 5,
            BasePrice = 420000,
            Amenities = "Wi-Fi, Jacuzzi, Balcón, Smart TV, Minibar",
            Description = "Actualizada para QA",
            IsActive = true
        };

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/roomtypes/{created.RoomTypeId}");
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        putRequest.Content = JsonContent.Create(updateDto);

        var putResponse = await _client.SendAsync(putRequest);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResponse.Content.ReadFromJsonAsync<RoomTypeDto>();
        updated!.Name.Should().Be("Suite Presidencial Test Modificada");
        updated.BasePrice.Should().Be(420000);
        updated.MaxCapacity.Should().Be(5);

        // 3. Conmutar estado (desactivación lógica)
        var toggleRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/roomtypes/{created.RoomTypeId}/toggle-status");
        toggleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var toggleResponse = await _client.SendAsync(toggleRequest);
        toggleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verificar que permanece en base de datos con IsActive = false
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/roomtypes/{created.RoomTypeId}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<RoomTypeDto>();
        fetched!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RoomType_CreateWithInvalidPrice_ShouldReturnBadRequest()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        var createDto = new CreateRoomTypeDto
        {
            Code = "INV-01",
            Name = "Habitación Precio Negativo",
            BaseCapacity = 2,
            MaxCapacity = 2,
            BasePrice = -50000 // Invalido
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/roomtypes");
        postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        postRequest.Content = JsonContent.Create(createDto);

        var response = await _client.SendAsync(postRequest);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // 4. INVENTARIO FÍSICO DE HABITACIONES & DESACTIVACIÓN PROTEGIDA
    // =========================================================================

    [Fact]
    public async Task Room_Update_ShouldPersistAttributes()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        // Consultar habitaciones de Bogotá
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/rooms?includeInactive=true");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResp = await _client.SendAsync(getReq);
        var rooms = await getResp.Content.ReadFromJsonAsync<List<RoomDto>>();
        rooms.Should().NotBeNullOrEmpty();
        var targetRoom = rooms![0];

        // Modificar notas y piso
        var updateDto = new UpdateRoomDto
        {
            RoomNumber = targetRoom.RoomNumber,
            Floor = targetRoom.Floor,
            RoomTypeId = targetRoom.RoomTypeId,
            Notes = "Revisión QA automatizada " + DateTime.UtcNow.Ticks,
            OperationalStatus = targetRoom.OperationalStatus,
            CleaningStatus = targetRoom.CleaningStatus,
            IsActive = targetRoom.IsActive
        };

        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/rooms/{targetRoom.RoomId}");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        putReq.Content = JsonContent.Create(updateDto);

        var putResp = await _client.SendAsync(putReq);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<RoomDto>();
        updated!.Notes.Should().Contain("Revisión QA automatizada");
    }

    [Fact]
    public async Task Room_DeactivateAndReactivate_ShouldSucceedForRoomWithoutActiveReservations()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        // Crear una habitación temporal sin reservas
        var uniqueRoomNumber = "T" + (new Random().Next(500, 999));
        var createRoomReq = new HttpRequestMessage(HttpMethod.Post, "/api/rooms");
        createRoomReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRoomReq.Content = JsonContent.Create(new CreateRoomDto
        {
            RoomNumber = uniqueRoomNumber,
            Floor = 3,
            RoomTypeId = 1,
            Notes = "Habitación para prueba de desactivación"
        });

        var createResp = await _client.SendAsync(createRoomReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<RoomDto>();

        // Desactivar habitación creada (sin reservas activas)
        var deactReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/rooms/{created!.RoomId}");
        deactReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var deactResp = await _client.SendAsync(deactReq);
        deactResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Reactivar habitación
        var actReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/rooms/{created.RoomId}/activate");
        actReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var actResp = await _client.SendAsync(actReq);
        actResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =========================================================================
    // 5. CATÁLOGO DE SERVICIOS ADICIONALES (CREAR, EDITAR, TOGGLE)
    // =========================================================================

    [Fact]
    public async Task Service_CreateUpdateAndToggle_ShouldPersistWithoutDeletingHistory()
    {
        var token = await AuthenticateAsync("admin@hotelchain.com", "Admin123*!", 1);

        // 1. Crear nuevo servicio en el catálogo
        var newServiceName = "Servicio QA " + Guid.NewGuid().ToString("N")[..6];
        var createDto = new CreateAdditionalServiceDto
        {
            Name = newServiceName,
            Category = "Spa",
            Price = 65000
        };

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/services");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        postReq.Content = JsonContent.Create(createDto);

        var postResp = await _client.SendAsync(postReq);
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResp.Content.ReadFromJsonAsync<AdditionalServiceDto>();
        created!.Name.Should().Be(newServiceName);
        created.Price.Should().Be(65000);
        created.IsActive.Should().BeTrue();

        // 2. Modificar precio y categoría
        var updateDto = new UpdateAdditionalServiceDto
        {
            Name = newServiceName + " Modificado",
            Category = "Spa",
            Price = 75000,
            IsActive = true
        };

        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/services/{created.ServiceId}");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        putReq.Content = JsonContent.Create(updateDto);

        var putResp = await _client.SendAsync(putReq);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<AdditionalServiceDto>();
        updated!.Price.Should().Be(75000);

        // 3. Desactivación lógica (proteger históricos)
        var toggleReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/services/{created.ServiceId}/toggle-status");
        toggleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var toggleResp = await _client.SendAsync(toggleReq);
        toggleResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Comprobar que permanece en DB como inactivo
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/services/{created.ServiceId}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var getResp = await _client.SendAsync(getReq);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<AdditionalServiceDto>();
        fetched!.IsActive.Should().BeFalse();
    }
}
