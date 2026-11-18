using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using QRCoder;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Infrastructure.Services;

// 1. CONTEXTO DE TENANT (SEDE ACTIVA)
public class TenantContext : ITenantContext
{
    public int? CurrentHotelId { get; private set; }
    public bool IsGlobalBypass { get; set; } = false;

    public void SetHotelContext(int hotelId)
    {
        CurrentHotelId = hotelId;
    }

    public void ClearHotelContext()
    {
        CurrentHotelId = null;
    }
}

// 2. HASHER DE CONTRASEÑAS CON BCRYPT
public class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, 11);
    }

    public bool Verify(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }
}

// 3. GENERADOR DE JWT Y REFRESH TOKENS (RNF-12)
public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateAccessToken(User user, int? hotelId)
    {
        var secret = _configuration["Jwt:Secret"] ?? "SuperSecretHotelManagementChainKey2026!MustBeVeryLong";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role)
        };

        if (hotelId.HasValue)
        {
            claims.Add(new Claim("HotelId", hotelId.Value.ToString()));
        }

        // Token de corta duración: 20 minutos (requisito RNF-12: 15-30 minutos)
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "HotelManagementApi",
            audience: _configuration["Jwt:Audience"] ?? "HotelManagementWeb",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshToken GenerateRefreshToken(int userId)
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        return new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(randomBytes),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
    }
}

// 4. GENERADOR DE CÓDIGO QR PARA VOUCHERS
public class QrCodeService : IQrCodeService
{
    public byte[] GenerateQrCodePng(string payload)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }
}

// 5. SERVICIO CENTRALIZADO Y REAL DE NOTIFICACIONES POR CORREO SMTP (RF-43, RF-44)
public class EmailNotificationService : IEmailNotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<EmailNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendReservationVoucherAsync(Reservation reservation, string toEmail, byte[] qrCode)
    {
        if (reservation == null || reservation.ReservationId <= 0)
        {
            _logger.LogWarning("Se omitió el envío de correo porque la reserva no posee un identificador persistido válido.");
            return Task.CompletedTask;
        }

        int reservationId = reservation.ReservationId;
        _ = Task.Run(async () =>
        {
            await SendReservationConfirmationAsync(reservationId, toEmail, qrCode);
        });

        return Task.CompletedTask;
    }

    public async Task<EmailDeliveryResult> SendReservationConfirmationAsync(
        int reservationId,
        string toEmail,
        byte[] qrCode,
        CancellationToken cancellationToken = default)
    {
        var result = new EmailDeliveryResult
        {
            Recipient = toEmail ?? string.Empty
        };

        if (reservationId <= 0)
        {
            result.ErrorMessage = "Identificador de reserva inválido; la reserva no ha sido guardada.";
            return result;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IHotelDbContext>();

            // Obtener la reserva directamente desde SQL Server para garantizar datos reales persistidos
            var dbReservation = await dbContext.Reservations
                .IgnoreQueryFilters()
                .Include(r => r.Hotel)
                .Include(r => r.Room)
                    .ThenInclude(rm => rm.RoomType)
                .Include(r => r.Guest)
                .Include(r => r.CancellationPolicy)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId, cancellationToken);

            if (dbReservation == null)
            {
                result.ErrorMessage = $"La reserva con ID {reservationId} no existe en SQL Server. No se envió correo.";
                _logger.LogWarning(result.ErrorMessage);
                return result;
            }

            result.ReservationCode = dbReservation.Code;
            string recipientEmail = !string.IsNullOrWhiteSpace(toEmail)
                ? toEmail.Trim()
                : (dbReservation.Guest?.Email?.Trim() ?? string.Empty);
            result.Recipient = recipientEmail;

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                result.ErrorMessage = $"La reserva {dbReservation.Code} no tiene una dirección de correo electrónico válida.";
                await RecordEmailAuditAsync(dbContext, dbReservation, false, 0, recipientEmail, result.ErrorMessage, cancellationToken);
                return result;
            }

            // Lectura segura de configuración desde variables de entorno o IConfiguration
            string smtpHost = GetSetting("SMTP_HOST", "EmailSettings:SmtpHost", string.Empty);
            int smtpPort = int.TryParse(GetSetting("SMTP_PORT", "EmailSettings:SmtpPort", "587"), out var p) ? p : 587;
            string smtpUser = GetSetting("SMTP_USERNAME", "EmailSettings:SmtpUsername", string.Empty);
            string smtpPass = GetSetting("SMTP_PASSWORD", "EmailSettings:SmtpPassword", string.Empty);
            string fromEmail = GetSetting("SMTP_FROM_EMAIL", "EmailSettings:FromEmail",
                !string.IsNullOrWhiteSpace(smtpUser) ? smtpUser : "reservas@continentalia.com");
            string fromName = GetSetting("SMTP_FROM_NAME", "EmailSettings:FromName", "CONTINENTALIA SAS - Reservas");
            bool enableSsl = !bool.TryParse(GetSetting("SMTP_ENABLE_SSL", "EmailSettings:EnableSsl", "true"), out var ssl) || ssl;
            string pickupDir = GetSetting("SMTP_PICKUP_DIRECTORY", "EmailSettings:PickupDirectory", string.Empty);
            int maxRetries = int.TryParse(_configuration["EmailSettings:MaxRetries"], out var mr) && mr > 0 ? mr : 3;
            int retryDelayMs = int.TryParse(_configuration["EmailSettings:RetryDelayMs"], out var rd) && rd >= 0 ? rd : 800;
            int timeoutMs = int.TryParse(_configuration["EmailSettings:TimeoutMs"], out var tm) && tm > 0 ? tm : 10000;

            if (string.IsNullOrWhiteSpace(smtpHost) && string.IsNullOrWhiteSpace(pickupDir))
            {
                result.Attempts = 1;
                result.ErrorMessage = "Servidor SMTP no configurado (variable de entorno SMTP_HOST o EmailSettings:SmtpHost ausente).";
                _logger.LogWarning("No fue posible enviar el correo de la reserva {Code} a {Email}: {Reason}",
                    dbReservation.Code, recipientEmail, result.ErrorMessage);
                await RecordEmailAuditAsync(dbContext, dbReservation, false, result.Attempts, recipientEmail, result.ErrorMessage, cancellationToken);
                return result;
            }

            string subject = "Confirmación de reserva – CONTINENTALIA SAS";
            string htmlBody = BuildConfirmationHtmlTemplate(dbReservation);
            string plainTextBody = BuildPlainTextFallback(dbReservation);

            string? lastError = null;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                result.Attempts = attempt;
                try
                {
                    using var message = new MailMessage
                    {
                        From = new MailAddress(fromEmail, fromName, Encoding.UTF8),
                        Subject = subject,
                        SubjectEncoding = Encoding.UTF8,
                        BodyEncoding = Encoding.UTF8,
                        IsBodyHtml = true
                    };
                    message.To.Add(new MailAddress(recipientEmail, dbReservation.Guest?.FullName ?? recipientEmail, Encoding.UTF8));

                    var plainView = AlternateView.CreateAlternateViewFromString(plainTextBody, Encoding.UTF8, MediaTypeNames.Text.Plain);
                    var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);

                    if (qrCode != null && qrCode.Length > 0)
                    {
                        var linkedQr = new LinkedResource(new MemoryStream(qrCode), "image/png")
                        {
                            ContentId = "voucher-qr",
                            TransferEncoding = TransferEncoding.Base64
                        };
                        htmlView.LinkedResources.Add(linkedQr);

                        var qrAttachment = new Attachment(new MemoryStream(qrCode), $"Voucher_{dbReservation.Code}.png", "image/png");
                        message.Attachments.Add(qrAttachment);
                    }

                    message.AlternateViews.Add(plainView);
                    message.AlternateViews.Add(htmlView);

                    using var client = new SmtpClient();
                    client.Timeout = timeoutMs;

                    if (!string.IsNullOrWhiteSpace(pickupDir) && string.IsNullOrWhiteSpace(smtpHost))
                    {
                        Directory.CreateDirectory(pickupDir);
                        client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                        client.PickupDirectoryLocation = pickupDir;
                    }
                    else
                    {
                        client.Host = smtpHost;
                        client.Port = smtpPort;
                        client.EnableSsl = enableSsl;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.UseDefaultCredentials = false;

                        if (!string.IsNullOrWhiteSpace(smtpUser) && !string.IsNullOrWhiteSpace(smtpPass))
                        {
                            client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                        }
                    }

                    await client.SendMailAsync(message, cancellationToken);

                    result.Success = true;
                    result.ErrorMessage = null;
                    _logger.LogInformation(
                        "Correo real de confirmación enviado exitosamente para la reserva {Code} a {Email} en el intento {Attempt}.",
                        dbReservation.Code, recipientEmail, attempt);

                    await RecordEmailAuditAsync(dbContext, dbReservation, true, attempt, recipientEmail, null, cancellationToken);
                    return result;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogWarning(ex,
                        "Fallo en el intento {Attempt}/{MaxRetries} al enviar correo de confirmación de la reserva {Code} a {Email}: {Error}",
                        attempt, maxRetries, dbReservation.Code, recipientEmail, ex.Message);

                    if (attempt < maxRetries && retryDelayMs > 0)
                    {
                        await Task.Delay(retryDelayMs * attempt, cancellationToken);
                    }
                }
            }

            result.Success = false;
            result.ErrorMessage = $"Agotados los {maxRetries} intentos de envío SMTP. Último error: {lastError}";
            await RecordEmailAuditAsync(dbContext, dbReservation, false, result.Attempts, recipientEmail, result.ErrorMessage, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            // RF-44: Ningún fallo del servicio de correo debe propagarse ni revertir una reserva confirmada
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error controlado en EmailNotificationService para la reserva ID {ReservationId}.", reservationId);
            return result;
        }
    }

    private string GetSetting(string envKey, string configKey, string defaultValue)
    {
        var envVal = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal.Trim();
        }

        var cfgVal = _configuration[configKey];
        if (!string.IsNullOrWhiteSpace(cfgVal))
        {
            return cfgVal.Trim();
        }

        return defaultValue;
    }

    private static async Task RecordEmailAuditAsync(
        IHotelDbContext dbContext,
        Reservation reservation,
        bool success,
        int attempts,
        string recipientEmail,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var audit = new AuditLog
            {
                HotelId = reservation.HotelId,
                UserId = null,
                Level = AuditLevel.Negocio,
                Action = success ? "EmailConfirmationSent" : "EmailConfirmationFailed",
                EntityName = "Reservation",
                EntityId = reservation.ReservationId.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    ReservationCode = reservation.Code,
                    Recipient = recipientEmail,
                    Success = success,
                    Attempts = attempts,
                    Error = errorMessage,
                    TimestampUtc = DateTime.UtcNow
                }),
                Timestamp = DateTime.UtcNow,
                IpAddress = "127.0.0.1"
            };

            dbContext.AuditLogs.Add(audit);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Evitar que un error secundario de auditoría interrumpa el flujo
        }
    }

    private static string EscapeHtml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    public static string BuildConfirmationHtmlTemplate(Reservation r)
    {
        string primaryColor = !string.IsNullOrWhiteSpace(r.Hotel?.PrimaryColor) ? r.Hotel.PrimaryColor : "#17365D";
        string hotelName = EscapeHtml(r.Hotel?.Name ?? "Sede CONTINENTALIA SAS");
        string hotelCity = EscapeHtml(r.Hotel?.City ?? "Colombia");
        string hotelAddress = EscapeHtml(r.Hotel?.Address ?? "Dirección principal de la sede");
        string hotelCode = EscapeHtml(r.Hotel?.Code ?? "HTL");
        string currency = EscapeHtml(r.Hotel?.Currency ?? "COP");
        string guestName = EscapeHtml(r.Guest?.FullName ?? "Estimado Huésped");
        string guestDoc = EscapeHtml($"{r.Guest?.DocumentType} {r.Guest?.DocumentNumber}".Trim());
        string guestEmail = EscapeHtml(r.Guest?.Email ?? "");
        string roomType = EscapeHtml(r.Room?.RoomType?.Name ?? "Habitación Estándar");
        string roomNumber = EscapeHtml(r.Room?.RoomNumber ?? "Asignada en recepción");
        int floor = r.Room?.Floor ?? 1;
        string code = EscapeHtml(r.Code);
        string status = EscapeHtml(r.Status.ToString());
        int nights = Math.Max(1, (int)(r.CheckOutDate.Date - r.CheckInDate.Date).TotalDays);
        int totalGuests = r.AdultCount + r.ChildCount;
        string guestsDetail = r.ChildCount > 0
            ? $"{totalGuests} huésped(es) ({r.AdultCount} adulto(s), {r.ChildCount} niño(s))"
            : $"{totalGuests} huésped(es) ({r.AdultCount} adulto(s))";

        return $@"<!DOCTYPE html>
<html lang=""es"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>Confirmación de reserva – CONTINENTALIA SAS</title>
</head>
<body style=""margin:0;padding:0;background-color:#F1F5F9;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#0F172A;"">
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#F1F5F9;padding:28px 12px;"">
        <tr>
            <td align=""center"">
                <table role=""presentation"" width=""620"" cellpadding=""0"" cellspacing=""0"" style=""max-width:620px;width:100%;background-color:#FFFFFF;border-radius:18px;overflow:hidden;box-shadow:0 12px 32px rgba(15,23,42,0.12);border:1px solid #E2E8F0;"">
                    <!-- ENCABEZADO CORPORATIVO Y LOGOTIPO CONTINENTALIA SAS -->
                    <tr>
                        <td style=""background:linear-gradient(135deg,{primaryColor} 0%,#0A192F 100%);background-color:{primaryColor};padding:30px 28px;text-align:center;border-bottom:4px solid #C5A059;"">
                            <div style=""width:58px;height:58px;margin:0 auto 12px;border-radius:14px;background-color:rgba(255,255,255,0.12);border:2px solid #C5A059;line-height:58px;font-size:24px;font-weight:800;color:#C5A059;text-align:center;"">
                                CS
                            </div>
                            <h1 style=""margin:0;font-size:22px;font-weight:800;letter-spacing:0.1em;color:#FFFFFF;"">
                                CONTINENTALIA <span style=""color:#C5A059;"">SAS</span>
                            </h1>
                            <p style=""margin:4px 0 0;font-size:11px;font-weight:700;letter-spacing:0.18em;color:#CBD5E1;text-transform:uppercase;"">
                                Hospitality Management System • Confirmación Oficial de Reserva
                            </p>
                        </td>
                    </tr>

                    <!-- SALUDO Y MENSAJE DE CONFIRMACIÓN -->
                    <tr>
                        <td style=""padding:28px 28px 12px;"">
                            <p style=""margin:0 0 8px;font-size:15px;color:#334155;"">
                                Estimado(a) <strong style=""color:#0F172A;"">{guestName}</strong>,
                            </p>
                            <p style=""margin:0;font-size:14px;line-height:1.6;color:#475569;"">
                                Su reserva ha sido registrada exitosamente en el sistema central de <strong>CONTINENTALIA SAS</strong> para nuestra sede <strong>{hotelName} ({hotelCity})</strong>. A continuación encontrará el resumen oficial de su estancia:
                            </p>
                        </td>
                    </tr>

                    <!-- CÓDIGO ÚNICO Y ESTADO DE LA RESERVA -->
                    <tr>
                        <td style=""padding:12px 28px;"">
                            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#F8FAFC;border:1px solid #CBD5E1;border-left:5px solid {primaryColor};border-radius:12px;padding:16px 20px;"">
                                <tr>
                                    <td>
                                        <div style=""font-size:11px;font-weight:700;text-transform:uppercase;color:#64748B;letter-spacing:0.06em;"">Código Único de Reserva</div>
                                        <div style=""font-size:20px;font-weight:800;color:{primaryColor};font-family:monospace;margin-top:4px;"">{code}</div>
                                    </td>
                                    <td align=""right"">
                                        <span style=""display:inline-block;padding:6px 14px;border-radius:999px;background-color:#DBEAFE;color:#1E40AF;font-size:12px;font-weight:700;text-transform:uppercase;"">
                                            Estado: {status}
                                        </span>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- DETALLES DE LA ESTANCIA Y HABITACIÓN -->
                    <tr>
                        <td style=""padding:12px 28px;"">
                            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;border:1px solid #E2E8F0;border-radius:12px;overflow:hidden;"">
                                <tr style=""background-color:#F8FAFC;border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;width:42%;"">Sede / Establecimiento</td>
                                    <td style=""padding:12px 16px;font-size:13.5px;font-weight:700;color:#0F172A;"">{hotelName} ({hotelCode}) — {hotelCity}</td>
                                </tr>
                                <tr style=""border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;"">Huésped Titular</td>
                                    <td style=""padding:12px 16px;font-size:13.5px;color:#0F172A;""><strong>{guestName}</strong> ({guestDoc})</td>
                                </tr>
                                <tr style=""background-color:#F8FAFC;border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;"">Tipo y Número de Habitación</td>
                                    <td style=""padding:12px 16px;font-size:13.5px;font-weight:700;color:#0F172A;"">{roomType} • Habitación {roomNumber} (Piso {floor})</td>
                                </tr>
                                <tr style=""border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;"">Fecha de Entrada (Check-In)</td>
                                    <td style=""padding:12px 16px;font-size:13.5px;font-weight:700;color:#0F172A;"">{r.CheckInDate:yyyy-MM-dd} (Desde las 15:00 hrs)</td>
                                </tr>
                                <tr style=""background-color:#F8FAFC;border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;"">Fecha de Salida (Check-Out)</td>
                                    <td style=""padding:12px 16px;font-size:13.5px;font-weight:700;color:#0F172A;"">{r.CheckOutDate:yyyy-MM-dd} (Hasta las 12:00 hrs • {nights} noche(s))</td>
                                </tr>
                                <tr style=""border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;"">Número de Huéspedes</td>
                                    <td style=""padding:12px 16px;font-size:13.5px;color:#0F172A;"">{guestsDetail}</td>
                                </tr>
                                <tr style=""background-color:#F8FAFC;border-bottom:1px solid #E2E8F0;"">
                                    <td style=""padding:12px 16px;font-size:12px;font-weight:700;color:#64748B;"">Desglose Tarifario</td>
                                    <td style=""padding:12px 16px;font-size:12.5px;color:#334155;"">
                                        Tarifa Base: ${r.BaseRate:N2} {currency} | Descuento: -${r.DiscountAmount:N2} {currency} | Impuestos: +${r.TaxAmount:N2} {currency}
                                    </td>
                                </tr>
                                <tr style=""background-color:#EFF6FF;"">
                                    <td style=""padding:14px 16px;font-size:13px;font-weight:800;color:{primaryColor};"">Valor Total de la Reserva</td>
                                    <td style=""padding:14px 16px;font-size:17px;font-weight:800;color:{primaryColor};"">${r.TotalAmount:N2} {currency}</td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- VOUCHER QR DE VERIFICACIÓN -->
                    <tr>
                        <td style=""padding:12px 28px;text-align:center;"">
                            <div style=""background-color:#F8FAFC;border:1px dashed #CBD5E1;border-radius:12px;padding:16px;"">
                                <div style=""font-size:11px;font-weight:700;text-transform:uppercase;color:#64748B;margin-bottom:8px;"">
                                    Código QR de Verificación Digital en Recepción
                                </div>
                                <img src=""cid:voucher-qr"" alt=""Voucher QR {code}"" width=""135"" height=""135"" style=""display:block;margin:0 auto;border:4px solid #FFFFFF;border-radius:8px;"" />
                                <div style=""font-size:11px;color:#64748B;margin-top:6px;"">Voucher: VCH-{code}</div>
                            </div>
                        </td>
                    </tr>

                    <!-- INFORMACIÓN DE CONTACTO DE LA SEDE Y AGRADECIMIENTO -->
                    <tr>
                        <td style=""padding:16px 28px 24px;"">
                            <div style=""background-color:#FFFBEB;border:1px solid #FDE68A;border-radius:12px;padding:14px 18px;font-size:12.5px;color:#78350F;line-height:1.5;"">
                                <strong>Información de Contacto de la Sede ({hotelName}):</strong><br/>
                                • Dirección: {hotelAddress}, {hotelCity}<br/>
                                • Correo de Recepción: recepcion.{hotelCode.ToLowerInvariant()}@hotelchain.com<br/>
                                • Línea de Atención al Huésped: +57 (601) 742-8000 • Código de Sede: {hotelCode}
                            </div>

                            <p style=""margin:18px 0 0;font-size:14px;line-height:1.6;color:#1E293B;text-align:center;font-weight:600;"">
                                ¡Gracias por elegir a <span style=""color:{primaryColor};"">CONTINENTALIA SAS</span>! Será un honor darle la bienvenida y brindarle una experiencia de hospitalidad inolvidable.
                            </p>
                        </td>
                    </tr>

                    <!-- PIE DE PÁGINA CORPORATIVO -->
                    <tr>
                        <td style=""background-color:#0F172A;padding:18px 28px;text-align:center;font-size:11px;color:#94A3B8;line-height:1.5;"">
                            © 2026 CONTINENTALIA SAS • Cadena Hotelera Asociada<br/>
                            Enviado a {guestEmail} • Fecha de emisión: {r.CreatedAt:yyyy-MM-dd HH:mm} UTC
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    private static string BuildPlainTextFallback(Reservation r)
    {
        return $@"CONFIRMACIÓN DE RESERVA – CONTINENTALIA SAS
============================================================
Estimado(a) {r.Guest?.FullName},

Gracias por elegir a CONTINENTALIA SAS. Su reserva ha sido registrada exitosamente:

- Código Único de Reserva : {r.Code}
- Estado Actual           : {r.Status}
- Sede / Establecimiento  : {r.Hotel?.Name} ({r.Hotel?.City})
- Dirección de la Sede    : {r.Hotel?.Address}, {r.Hotel?.City}
- Habitación              : {r.Room?.RoomType?.Name} - Habitación {r.Room?.RoomNumber}
- Fecha de Entrada        : {r.CheckInDate:yyyy-MM-dd}
- Fecha de Salida         : {r.CheckOutDate:yyyy-MM-dd}
- Número de Huéspedes     : {r.AdultCount + r.ChildCount} ({r.AdultCount} adulto(s), {r.ChildCount} niño(s))
- Valor Total             : ${r.TotalAmount:N2} {r.Hotel?.Currency ?? "COP"}

Contacto de la sede: {r.Hotel?.Name} - {r.Hotel?.Address} ({r.Hotel?.City})
¡Gracias por confiar en CONTINENTALIA SAS!";
    }
}
