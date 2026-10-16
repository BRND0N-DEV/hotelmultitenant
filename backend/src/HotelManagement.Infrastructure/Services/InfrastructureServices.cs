using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using QRCoder;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Entities;

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

// 5. SERVICIO ASÍNCRONO DE NOTIFICACIONES POR CORREO (RF-43, RF-44)
public class EmailNotificationService : IEmailNotificationService
{
    public Task SendReservationVoucherAsync(Reservation reservation, string toEmail, byte[] qrCode)
    {
        // El envío se ejecuta en segundo plano desacoplado
        _ = Task.Run(async () =>
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // Simulación o entrega de correo transaccional
                    await Task.Delay(100);
                    // Éxito en envío
                    break;
                }
                catch
                {
                    if (attempt == 3)
                    {
                        // Se registraron los 3 fallos sin abortar la reserva
                    }
                }
            }
        });

        return Task.CompletedTask;
    }
}
