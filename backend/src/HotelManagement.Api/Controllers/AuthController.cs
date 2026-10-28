using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HotelManagement.Application.DTOs;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(
        IHotelDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto dto)
    {
        var user = await _dbContext.Users
            .Include(u => u.HotelUsers)
                .ThenInclude(hu => hu.Hotel)
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLowerInvariant());

        if (user == null)
        {
            return Unauthorized(new { message = "Credenciales incorrectas." });
        }

        if (user.IsLockedOut())
        {
            return StatusCode(423, new { message = $"Cuenta bloqueada por 5 intentos fallidos consecutivos hasta {user.LockoutEnd:HH:mm} UTC." });
        }

        if (!_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            user.RecordFailedLogin();
            await _dbContext.SaveChangesAsync();

            if (user.IsLockedOut())
            {
                return StatusCode(423, new { message = "Cuenta bloqueada temporalmente por 15 minutos tras 5 intentos fallidos consecutivos." });
            }

            int remaining = 5 - user.FailedLoginAttempts;
            return Unauthorized(new { message = $"Contraseña incorrecta. Intentos restantes antes de bloqueo: {remaining}" });
        }

        if (user.FailedLoginAttempts > 0 || user.LockoutEnd.HasValue)
        {
            user.ResetLoginLockout();
        }

        // Determinar sede activa
        int? activeHotelId = dto.PreferredHotelId;
        if (!activeHotelId.HasValue)
        {
            var defaultAssignment = user.HotelUsers.FirstOrDefault(hu => hu.IsDefault) ?? user.HotelUsers.FirstOrDefault();
            activeHotelId = defaultAssignment?.HotelId;
        }

        // Generar tokens
        var accessToken = _tokenService.GenerateAccessToken(user, activeHotelId);
        var refreshToken = _tokenService.GenerateRefreshToken(user.UserId);

        _dbContext.RefreshTokens.Add(refreshToken);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (_dbContext is DbContext db)
            {
                await db.Entry(user).ReloadAsync();
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                throw;
            }
        }

        var activeHotel = await _dbContext.Hotels.FirstOrDefaultAsync(h => h.HotelId == activeHotelId);
        var assignedHotels = await GetUserAssignedHotelsAsync(user, activeHotelId);

        var response = new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            User = new UserInfoDto
            {
                UserId = user.UserId,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                Department = user.Department,
                Shift = user.Shift,
                ActiveHotelId = activeHotelId,
                ActiveHotelName = activeHotel?.Name,
                AssignedHotels = assignedHotels
            }
        };

        return Ok(response);
    }


    [HttpPost("refresh-token")]
    public async Task<ActionResult<LoginResponseDto>> RefreshToken([FromBody] RefreshTokenRequestDto dto)
    {
        var existingToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.HotelUsers)
                    .ThenInclude(hu => hu.Hotel)
            .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

        if (existingToken == null)
        {
            return Unauthorized(new { message = "Token de renovación inválido." });
        }

        // Detección de reutilización de refresh tokens (RF-02 / RNF-12)
        if (existingToken.IsRevoked)
        {
            // Revocación de toda la familia de tokens por sospecha de compromiso
            var userTokens = await _dbContext.RefreshTokens
                .Where(rt => rt.UserId == existingToken.UserId && rt.RevokedAt == null)
                .ToListAsync();

            foreach (var t in userTokens)
            {
                t.RevokedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
            return Unauthorized(new { message = "Se detectó la reutilización de un token revocado. Todas las sesiones activas han sido cerradas por seguridad." });
        }

        if (existingToken.IsExpired)
        {
            return Unauthorized(new { message = "El token de renovación ha expirado. Por favor inicie sesión nuevamente." });
        }

        // Rotación de token
        var newRefreshToken = _tokenService.GenerateRefreshToken(existingToken.UserId);
        existingToken.RevokedAt = DateTime.UtcNow;
        existingToken.ReplacedByToken = newRefreshToken.Token;

        _dbContext.RefreshTokens.Add(newRefreshToken);

        int? activeHotelId = dto.ActiveHotelId;
        if (!activeHotelId.HasValue)
        {
            var defaultAssignment = existingToken.User.HotelUsers.FirstOrDefault(hu => hu.IsDefault) ?? existingToken.User.HotelUsers.FirstOrDefault();
            activeHotelId = defaultAssignment?.HotelId;
        }

        var newAccessToken = _tokenService.GenerateAccessToken(existingToken.User, activeHotelId);
        await _dbContext.SaveChangesAsync();

        var activeHotel = existingToken.User.HotelUsers.FirstOrDefault(hu => hu.HotelId == activeHotelId)?.Hotel;

        return Ok(new LoginResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            User = new UserInfoDto
            {
                UserId = existingToken.User.UserId,
                Email = existingToken.User.Email,
                FullName = existingToken.User.FullName,
                Role = existingToken.User.Role,
                Department = existingToken.User.Department,
                Shift = existingToken.User.Shift,
                ActiveHotelId = activeHotelId,
                ActiveHotelName = activeHotel?.Name,
                AssignedHotels = existingToken.User.HotelUsers.Select(hu => new HotelAssignmentDto
                {
                    HotelId = hu.HotelId,
                    Name = hu.Hotel.Name,
                    City = hu.Hotel.City,
                    Code = hu.Hotel.Code,
                    IsDefault = hu.IsDefault
                }).ToList()
            }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoDto>> GetCurrentUser()
    {
        if (!_currentUserService.UserId.HasValue) return Unauthorized();

        var user = await _dbContext.Users
            .Include(u => u.HotelUsers)
                .ThenInclude(hu => hu.Hotel)
            .FirstOrDefaultAsync(u => u.UserId == _currentUserService.UserId.Value);

        if (user == null) return NotFound();

        int? activeHotelId = _tenantContext.CurrentHotelId;
        var activeHotel = activeHotelId.HasValue 
            ? await _dbContext.Hotels.FirstOrDefaultAsync(h => h.HotelId == activeHotelId.Value)
            : null;

        var assignedHotels = await GetUserAssignedHotelsAsync(user, activeHotelId);

        return Ok(new UserInfoDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            Department = user.Department,
            Shift = user.Shift,
            ActiveHotelId = activeHotelId,
            ActiveHotelName = activeHotel?.Name,
            AssignedHotels = assignedHotels
        });
    }

    [Authorize]
    [HttpPost("switch-hotel")]
    public async Task<ActionResult<LoginResponseDto>> SwitchHotelContext([FromBody] JsonElement body)
    {
        if (!_currentUserService.UserId.HasValue) return Unauthorized();

        int hotelId = 0;
        if (body.ValueKind == JsonValueKind.Number && body.TryGetInt32(out var numId))
        {
            hotelId = numId;
        }
        else if (body.ValueKind == JsonValueKind.Object)
        {
            if (body.TryGetProperty("hotelId", out var prop) && prop.TryGetInt32(out var parsedId))
            {
                hotelId = parsedId;
            }
            else if (body.TryGetProperty("HotelId", out var propUpper) && propUpper.TryGetInt32(out var parsedUpper))
            {
                hotelId = parsedUpper;
            }
        }
        else if (body.ValueKind == JsonValueKind.String && int.TryParse(body.GetString(), out var strId))
        {
            hotelId = strId;
        }

        if (hotelId <= 0)
        {
            return BadRequest(new { message = "Identificador de sede inválido." });
        }

        var user = await _dbContext.Users
            .Include(u => u.HotelUsers)
                .ThenInclude(hu => hu.Hotel)
            .FirstOrDefaultAsync(u => u.UserId == _currentUserService.UserId.Value);

        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        var hotel = await _dbContext.Hotels.FirstOrDefaultAsync(h => h.HotelId == hotelId);
        if (hotel == null)
        {
            return NotFound(new { message = $"La sede con identificador {hotelId} no existe en el sistema." });
        }

        if (!hotel.IsActive)
        {
            return BadRequest(new { message = $"La sede '{hotel.Name}' se encuentra inactiva y no admite conmutación operativa." });
        }

        bool isGlobal = user.Role == UserRole.AdminGlobal ||
                        user.Role == UserRole.Socio ||
                        user.Role == UserRole.Auditor;

        bool isAssigned = user.HotelUsers.Any(hu => hu.HotelId == hotelId);

        if (!isGlobal && !isAssigned)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tiene autorización para operar en la sede seleccionada. Acceso restringido al personal asignado." });
        }

        var newAccessToken = _tokenService.GenerateAccessToken(user, hotelId);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.UserId);
        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync();

        var assignedHotels = await GetUserAssignedHotelsAsync(user, hotelId);

        return Ok(new LoginResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            User = new UserInfoDto
            {
                UserId = user.UserId,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                Department = user.Department,
                Shift = user.Shift,
                ActiveHotelId = hotelId,
                ActiveHotelName = hotel.Name,
                AssignedHotels = assignedHotels
            }
        });
    }

    private async Task<List<HotelAssignmentDto>> GetUserAssignedHotelsAsync(User user, int? activeHotelId)
    {
        bool isGlobal = user.Role == UserRole.AdminGlobal || user.Role == UserRole.Socio || user.Role == UserRole.Auditor;
        if (isGlobal)
        {
            return await _dbContext.Hotels
                .Where(h => h.IsActive)
                .OrderBy(h => h.HotelId)
                .Select(h => new HotelAssignmentDto
                {
                    HotelId = h.HotelId,
                    Name = h.Name,
                    City = h.City,
                    Code = h.Code,
                    IsDefault = h.HotelId == activeHotelId
                })
                .ToListAsync();
        }

        return user.HotelUsers
            .Where(hu => hu.Hotel.IsActive)
            .OrderBy(hu => hu.HotelId)
            .Select(hu => new HotelAssignmentDto
            {
                HotelId = hu.HotelId,
                Name = hu.Hotel.Name,
                City = hu.Hotel.City,
                Code = hu.Hotel.Code,
                IsDefault = hu.IsDefault || hu.HotelId == activeHotelId
            })
            .ToList();
    }
}

