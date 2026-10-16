using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HotelManagement.Application.DTOs;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GuestsController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public GuestsController(
        IHotelDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GuestDto>>> GetGuests([FromQuery] string? search)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var query = _dbContext.Guests
            .Include(g => g.DataConsents)
            .Where(g => g.HotelId == targetHotelId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(g => g.FirstName.ToLower().Contains(term) ||
                                     g.LastName.ToLower().Contains(term) ||
                                     g.DocumentNumber.Contains(term) ||
                                     g.Email.ToLower().Contains(term));
        }

        var guests = await query
            .OrderByDescending(g => g.CreatedAt)
            .Take(50)
            .Select(g => new GuestDto
            {
                GuestId = g.GuestId,
                DocumentType = g.DocumentType,
                DocumentNumber = g.DocumentNumber,
                FirstName = g.FirstName,
                LastName = g.LastName,
                FullName = g.FullName,
                Email = g.Email,
                Phone = g.Phone,
                Nationality = g.Nationality,
                IsAnonymized = g.IsAnonymized,
                HasConsent = g.DataConsents.Any(dc => dc.Accepted)
            })
            .ToListAsync();

        return Ok(guests);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GuestDto>> GetGuest(int id)
    {
        var g = await _dbContext.Guests
            .Include(g => g.DataConsents)
            .FirstOrDefaultAsync(g => g.GuestId == id);

        if (g == null) return NotFound(new { message = "Huésped no encontrado." });

        return Ok(new GuestDto
        {
            GuestId = g.GuestId,
            DocumentType = g.DocumentType,
            DocumentNumber = g.DocumentNumber,
            FirstName = g.FirstName,
            LastName = g.LastName,
            FullName = g.FullName,
            Email = g.Email,
            Phone = g.Phone,
            Nationality = g.Nationality,
            IsAnonymized = g.IsAnonymized,
            HasConsent = g.DataConsents.Any(dc => dc.Accepted)
        });
    }

    // RF-47 / HU-23: Derecho al olvido y anonimización irreversible (AdminGlobal)
    [Authorize(Roles = UserRole.AdminGlobal)]
    [HttpPost("{id}/anonymize")]
    public async Task<IActionResult> AnonymizeGuest(int id)
    {
        var guest = await _dbContext.Guests
            .Include(g => g.DataConsents)
            .FirstOrDefaultAsync(g => g.GuestId == id);

        if (guest == null) return NotFound(new { message = "Huésped no encontrado." });

        if (guest.IsAnonymized)
        {
            return BadRequest(new { message = "Este huésped ya fue anonimizado previamente." });
        }

        // Ejecutar anonimización irreversible en la entidad de dominio
        guest.Anonymize();

        // Registrar auditoría de negocio obligatoria e irreversible
        _dbContext.AuditLogs.Add(new AuditLog
        {
            HotelId = guest.HotelId,
            UserId = _currentUserService.UserId,
            Level = AuditLevel.Negocio,
            Action = "Anonimización de datos personales (Habeas Data / Derecho al Olvido)",
            EntityName = nameof(Guest),
            EntityId = guest.GuestId.ToString(),
            OldValues = "Datos personales identificables reemplazados",
            NewValues = $"GuestId {guest.GuestId} anonimizado como '{guest.DocumentNumber}'. Folios y contabilidad preservados.",
            Timestamp = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Los datos personales del huésped han sido anonimizados de forma irreversible (RF-47 / HU-23). Los registros de folios y comprobantes contables se conservan intactos.",
            guestId = guest.GuestId,
            anonymousIdentifier = guest.DocumentNumber
        });
    }
}
