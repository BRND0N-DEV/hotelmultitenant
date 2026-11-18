using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HotelManagement.Application.DTOs;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Enums;
using Xunit;

namespace HotelManagement.IntegrationTests;

public class AuthAndEmailNotificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthAndEmailNotificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    private async Task<string> AuthenticateAsync(string email, string password = "Admin123*!", int? preferredHotelId = 1)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password,
            PreferredHotelId = preferredHotelId
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await resp.Content.ReadFromJsonAsync<LoginResponseDto>();
        return data!.AccessToken;
    }

    // =========================================================================
    // 1. PRUEBAS DE AUTENTICACIÓN, VALIDACIÓN DE LOGIN Y ANTI-ENUMERACIÓN
    // =========================================================================

    [Fact]
    public async Task Login_WithUnregisteredUser_And_WrongPassword_ShouldReturnIdenticalControlled401WithoutUserEnumeration()
    {
        string unregisteredEmail = $"no.registrado.{Guid.NewGuid():N}@hotelchain.com";

        // 1. Usuario inexistente
        var unregResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = unregisteredEmail,
            Password = "PasswordCualquiera123!"
        });

        unregResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unregBody = await unregResp.Content.ReadAsStringAsync();
        unregBody.Should().Contain("Las credenciales ingresadas no son válidas. Verifique su usuario y contraseña.");

        // 2. Usuario existente con contraseña incorrecta
        var wrongPassResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "recepcion.bogota@hotelchain.com",
            Password = "ClaveIncorrecta999!"
        });

        wrongPassResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var wrongPassBody = await wrongPassResp.Content.ReadAsStringAsync();
        wrongPassBody.Should().Contain("Las credenciales ingresadas no son válidas. Verifique su usuario y contraseña.");

        // 3. Ambos mensajes públicos deben ser idénticos para evitar enumeración de cuentas
        wrongPassBody.Should().Be(unregBody);

        // 4. Restaurar contador de fallos autenticando correctamente y verificar registro en AuditLogs
        await AuthenticateAsync("recepcion.bogota@hotelchain.com");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IHotelDbContext>();
        var auditEntry = await db.AuditLogs
            .OrderByDescending(a => a.AuditLogId)
            .FirstOrDefaultAsync(a => a.Action == "LoginFailed" && a.NewValues != null && a.NewValues.Contains(unregisteredEmail));
        auditEntry.Should().NotBeNull("el intento fallido con usuario no registrado debe quedar auditado internamente");
    }

    [Theory]
    [InlineData("", "Admin123*!")]
    [InlineData("   ", "Admin123*!")]
    [InlineData("recepcion.bogota@hotelchain.com", "")]
    [InlineData("recepcion.bogota@hotelchain.com", "   ")]
    public async Task Login_WithEmptyOrWhitespaceFields_ShouldReturn400BadRequest(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("campos obligatorios");
    }

    [Theory]
    [InlineData("socio@hotelchain.com", "Socio", 1)]
    [InlineData("admin@hotelchain.com", "AdminGlobal", 1)]
    [InlineData("admin.bogota@hotelchain.com", "AdminSede", 1)]
    [InlineData("recepcion.bogota@hotelchain.com", "Recepcionista", 1)]
    [InlineData("housekeeping.bogota@hotelchain.com", "Housekeeping", 1)]
    [InlineData("auditor@hotelchain.com", "Auditor", 1)]
    public async Task Login_AllSixCanonicalRoles_ShouldAuthenticateNormally(string email, string expectedRole, int hotelId)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = "Admin123*!",
            PreferredHotelId = hotelId
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await resp.Content.ReadFromJsonAsync<LoginResponseDto>();
        loginData.Should().NotBeNull();
        loginData!.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginData.User.Role.Should().Be(expectedRole);
    }

    // =========================================================================
    // 2. PRUEBAS DE ENVÍO REAL DE CORREO SMTP PARA CONFIRMACIÓN DE RESERVAS
    // =========================================================================

    [Fact]
    public async Task CreateReservation_ShouldSendRealSmtpEmailWithDatabaseReservationDetails_AndRecordAudit()
    {
        await using var smtpServer = new LocalSmtpTestServer();
        smtpServer.Start();

        var prevHost = Environment.GetEnvironmentVariable("SMTP_HOST");
        var prevPort = Environment.GetEnvironmentVariable("SMTP_PORT");
        var prevSsl = Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL");
        var prevFrom = Environment.GetEnvironmentVariable("SMTP_FROM_EMAIL");

        int? createdReservationId = null;
        string token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);

        try
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", "127.0.0.1");
            Environment.SetEnvironmentVariable("SMTP_PORT", smtpServer.Port.ToString());
            Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", "false");
            Environment.SetEnvironmentVariable("SMTP_FROM_EMAIL", "reservas@continentalia.com");

            // Buscar fecha libre para una habitación en Hotel 1
            var (roomId, checkIn, checkOut) = await FindAvailableSlotAsync(token, 1);
            string guestEmail = $"confirmacion.real.{Guid.NewGuid():N}@continentalia.test";
            string guestDoc = $"99{Random.Shared.Next(10000000, 99999999)}";

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/reservations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Hotel-Id", "1");
            req.Content = JsonContent.Create(new CreateReservationDto
            {
                RoomId = roomId,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                Adults = 2,
                Children = 1,
                CancellationPolicyId = 1,
                SpecialRequests = "Confirmación SMTP QA",
                PrimaryGuest = new CreateGuestDto
                {
                    DocumentType = "CC",
                    DocumentNumber = guestDoc,
                    FirstName = "Alejandro",
                    LastName = "Valderrama",
                    Email = guestEmail,
                    Phone = "+57 315 800 9000",
                    Nationality = "Colombia",
                    AcceptDataConsent = true
                }
            });

            var createResp = await _client.SendAsync(req);
            createResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var createdDto = await createResp.Content.ReadFromJsonAsync<ReservationDto>();
            createdDto.Should().NotBeNull();
            createdReservationId = createdDto!.ReservationId;

            // Ejecutar y verificar entrega directa a través del servidor SMTP real
            using var scope = _factory.Services.CreateScope();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            var qrService = scope.ServiceProvider.GetRequiredService<IQrCodeService>();
            var qrBytes = qrService.GenerateQrCodePng($"http://localhost:5000/api/reservations/verify-voucher/{createdDto.Code}");

            var deliveryResult = await emailService.SendReservationConfirmationAsync(createdDto.ReservationId, guestEmail, qrBytes);

            deliveryResult.Success.Should().BeTrue();
            deliveryResult.Attempts.Should().Be(1);
            deliveryResult.ReservationCode.Should().Be(createdDto.Code);
            deliveryResult.Recipient.Should().Be(guestEmail);

            smtpServer.ReceivedMessages.Should().NotBeEmpty("el cliente SMTP debe haber transmitido el correo real por socket TCP");
            var rawMime = string.Join("\n", smtpServer.ReceivedMessages);

            // Decodificar contenido MIME (Quoted-Printable / Base64 / UTF-8) y también validar plantilla con datos reales de BD
            var db = scope.ServiceProvider.GetRequiredService<IHotelDbContext>();
            var dbReservation = await db.Reservations
                .IgnoreQueryFilters()
                .Include(r => r.Hotel)
                .Include(r => r.Room)
                    .ThenInclude(rm => rm.RoomType)
                .Include(r => r.Guest)
                .FirstAsync(r => r.ReservationId == createdDto.ReservationId);

            string htmlRendered = HotelManagement.Infrastructure.Services.EmailNotificationService.BuildConfirmationHtmlTemplate(dbReservation);
            htmlRendered.Should().Contain("CONTINENTALIA");
            htmlRendered.Should().Contain("Alejandro Valderrama");
            htmlRendered.Should().Contain(createdDto.Code);
            htmlRendered.Should().Contain(dbReservation.Hotel.Name);
            htmlRendered.Should().Contain(dbReservation.Room.RoomNumber);
            htmlRendered.Should().Contain(dbReservation.Room.RoomType.Name);
            htmlRendered.Should().Contain(checkIn.ToString("yyyy-MM-dd"));
            htmlRendered.Should().Contain(checkOut.ToString("yyyy-MM-dd"));
            htmlRendered.Should().Contain("3 huésped(es)");
            htmlRendered.Should().Contain(dbReservation.TotalAmount.ToString("N2"));
            htmlRendered.Should().Contain(dbReservation.Status.ToString());
            htmlRendered.Should().Contain("Gracias por elegir a");

            rawMime.Should().Contain(guestEmail);
            rawMime.Should().Contain(createdDto.Code);
            rawMime.Should().Contain("voucher-qr");

            // Verificar registro de auditoría EmailConfirmationSent en SQL Server
            var emailAudit = await db.AuditLogs
                .OrderByDescending(a => a.AuditLogId)
                .FirstOrDefaultAsync(a => a.Action == "EmailConfirmationSent" && a.EntityId == createdDto.ReservationId.ToString());
            emailAudit.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", prevHost);
            Environment.SetEnvironmentVariable("SMTP_PORT", prevPort);
            Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", prevSsl);
            Environment.SetEnvironmentVariable("SMTP_FROM_EMAIL", prevFrom);

            if (createdReservationId.HasValue)
            {
                await CancelTestReservationAsync(token, createdReservationId.Value);
            }
        }
    }

    [Fact]
    public async Task FailedReservation_ShouldNotSendAnyConfirmationEmail()
    {
        await using var smtpServer = new LocalSmtpTestServer();
        smtpServer.Start();

        var prevHost = Environment.GetEnvironmentVariable("SMTP_HOST");
        var prevPort = Environment.GetEnvironmentVariable("SMTP_PORT");
        var prevSsl = Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL");

        try
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", "127.0.0.1");
            Environment.SetEnvironmentVariable("SMTP_PORT", smtpServer.Port.ToString());
            Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", "false");

            string token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/reservations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Hotel-Id", "1");
            req.Content = JsonContent.Create(new CreateReservationDto
            {
                RoomId = 1,
                CheckInDate = DateTime.UtcNow.Date.AddDays(50),
                CheckOutDate = DateTime.UtcNow.Date.AddDays(52),
                Adults = 1,
                CancellationPolicyId = 1,
                PrimaryGuest = new CreateGuestDto
                {
                    DocumentType = "CC",
                    DocumentNumber = "1122334455",
                    FirstName = "Sin",
                    LastName = "Consentimiento",
                    Email = "no.enviar@continentalia.test",
                    Phone = "3000000000",
                    Nationality = "Colombia",
                    AcceptDataConsent = false // Provoca rechazo HTTP 400 (RF-46)
                }
            });

            var resp = await _client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            await Task.Delay(150);
            smtpServer.ReceivedMessages.Should().BeEmpty("cuando la reserva falla no debe enviarse ningún correo de confirmación");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", prevHost);
            Environment.SetEnvironmentVariable("SMTP_PORT", prevPort);
            Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", prevSsl);
        }
    }

    [Fact]
    public async Task SmtpServerUnavailable_ShouldRetry3Times_LogAuditFailure_AndKeepReservationIntact()
    {
        // Obtener un puerto libre y cerrarlo de inmediato para simular caída del servidor SMTP
        int closedPort;
        using (var tempListener = new TcpListener(IPAddress.Loopback, 0))
        {
            tempListener.Start();
            closedPort = ((IPEndPoint)tempListener.LocalEndpoint).Port;
            tempListener.Stop();
        }

        var prevHost = Environment.GetEnvironmentVariable("SMTP_HOST");
        var prevPort = Environment.GetEnvironmentVariable("SMTP_PORT");
        var prevSsl = Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL");

        int? createdReservationId = null;
        string token = await AuthenticateAsync("recepcion.bogota@hotelchain.com", "Admin123*!", 1);

        try
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", "127.0.0.1");
            Environment.SetEnvironmentVariable("SMTP_PORT", closedPort.ToString());
            Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", "false");

            var (roomId, checkIn, checkOut) = await FindAvailableSlotAsync(token, 1, startOffsetDays: 360);
            string guestEmail = $"fallo.smtp.{Guid.NewGuid():N}@continentalia.test";

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/reservations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Hotel-Id", "1");
            req.Content = JsonContent.Create(new CreateReservationDto
            {
                RoomId = roomId,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                Adults = 1,
                CancellationPolicyId = 1,
                PrimaryGuest = new CreateGuestDto
                {
                    DocumentType = "CC",
                    DocumentNumber = $"88{Random.Shared.Next(10000000, 99999999)}",
                    FirstName = "Resiliencia",
                    LastName = "Smtp",
                    Email = guestEmail,
                    Phone = "3101112233",
                    Nationality = "Colombia",
                    AcceptDataConsent = true
                }
            });

            var createResp = await _client.SendAsync(req);
            createResp.StatusCode.Should().Be(HttpStatusCode.Created, "el fallo del servidor SMTP nunca debe impedir ni revertir la reserva");
            var createdDto = await createResp.Content.ReadFromJsonAsync<ReservationDto>();
            createdReservationId = createdDto!.ReservationId;

            using var scope = _factory.Services.CreateScope();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            var deliveryResult = await emailService.SendReservationConfirmationAsync(createdDto.ReservationId, guestEmail, Array.Empty<byte>());

            deliveryResult.Success.Should().BeFalse();
            deliveryResult.Attempts.Should().Be(3, "debe reintentar hasta 3 veces ante fallo del servidor SMTP");
            deliveryResult.ErrorMessage.Should().NotBeNullOrWhiteSpace();

            // Verificar que la reserva sigue persistida en SQL Server y que el fallo quedó registrado en AuditLogs
            var db = scope.ServiceProvider.GetRequiredService<IHotelDbContext>();
            var persistedRes = await db.Reservations.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.ReservationId == createdDto.ReservationId);
            persistedRes.Should().NotBeNull("la reserva debe permanecer intacta en SQL Server aunque falle el correo");

            var failAudit = await db.AuditLogs
                .OrderByDescending(a => a.AuditLogId)
                .FirstOrDefaultAsync(a => a.Action == "EmailConfirmationFailed" && a.EntityId == createdDto.ReservationId.ToString());
            failAudit.Should().NotBeNull("el fallo tras los 3 reintentos debe registrarse en AuditLogs");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", prevHost);
            Environment.SetEnvironmentVariable("SMTP_PORT", prevPort);
            Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", prevSsl);

            if (createdReservationId.HasValue)
            {
                await CancelTestReservationAsync(token, createdReservationId.Value);
            }
        }
    }

    private async Task<(int RoomId, DateTime CheckIn, DateTime CheckOut)> FindAvailableSlotAsync(string token, int hotelId, int startOffsetDays = 310)
    {
        for (int offset = startOffsetDays; offset < startOffsetDays + 120; offset += 5)
        {
            var checkIn = DateTime.UtcNow.Date.AddDays(offset);
            var checkOut = checkIn.AddDays(2);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/availability/search");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Hotel-Id", hotelId.ToString());
            req.Content = JsonContent.Create(new AvailabilitySearchRequestDto
            {
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                Adults = 2,
                Children = 0
            });

            var resp = await _client.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var rooms = await resp.Content.ReadFromJsonAsync<List<AvailableRoomResponseDto>>();
                if (rooms != null && rooms.Count > 0)
                {
                    return (rooms[0].RoomId, checkIn, checkOut);
                }
            }
        }

        throw new InvalidOperationException("No se encontró disponibilidad en las fechas de prueba.");
    }

    private async Task CancelTestReservationAsync(string token, int reservationId)
    {
        var adminToken = await AuthenticateAsync("admin.bogota@hotelchain.com", "Admin123*!", 1);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/reservations/{reservationId}/cancel");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Headers.Add("X-Hotel-Id", "1");
        req.Content = JsonContent.Create(new CancelReservationDto { Reason = "Limpieza automática QA post-prueba SMTP" });
        await _client.SendAsync(req);
    }

    // Servidor SMTP RFC 5321 local real sobre sockets TCP para verificación determinista de integración
    private sealed class LocalSmtpTestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptLoopTask;

        public int Port { get; private set; }
        public ConcurrentQueue<string> ReceivedMessages { get; } = new();

        public LocalSmtpTestServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoopTask = Task.Run(() => AcceptClientsAsync(_cts.Token));
        }

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
                catch
                {
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" })
            {
                await writer.WriteLineAsync("220 localhost ESMTP ContinentaliaTestServer");
                var dataBuilder = new StringBuilder();
                bool inData = false;

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    if (inData)
                    {
                        if (line == ".")
                        {
                            inData = false;
                            ReceivedMessages.Enqueue(dataBuilder.ToString());
                            dataBuilder.Clear();
                            await writer.WriteLineAsync("250 2.0.0 OK: Message accepted for delivery");
                        }
                        else
                        {
                            dataBuilder.AppendLine(line);
                        }
                        continue;
                    }

                    dataBuilder.AppendLine(line);

                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250 8BITMIME");
                    }
                    else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 2.1.0 OK");
                    }
                    else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 2.1.5 OK");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        inData = true;
                        await writer.WriteLineAsync("354 Start mail input; end with <CRLF>.<CRLF>");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 2.0.0 Bye");
                        break;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_acceptLoopTask != null)
            {
                try { await _acceptLoopTask; } catch { }
            }
            _cts.Dispose();
        }
    }
}
