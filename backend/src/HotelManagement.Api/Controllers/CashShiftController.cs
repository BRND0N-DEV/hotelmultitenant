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
public class CashShiftController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public CashShiftController(
        IHotelDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    [HttpGet("current")]
    public async Task<ActionResult<CashShiftDto>> GetCurrentShift()
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        int userId = _currentUserService.UserId ?? 1;

        var shift = await _dbContext.CashShifts
            .Include(cs => cs.User)
            .Include(cs => cs.Payments)
            .Where(cs => cs.HotelId == targetHotelId && cs.Status == ShiftStatus.Abierto)
            .OrderByDescending(cs => cs.OpenedAt)
            .FirstOrDefaultAsync();

        if (shift == null) return NotFound(new { message = "No hay ningún turno de caja abierto actualmente en esta sede." });

        return Ok(new CashShiftDto
        {
            CashShiftId = shift.CashShiftId,
            HotelId = shift.HotelId,
            UserId = shift.UserId,
            UserName = shift.User.FullName,
            OpenedAt = shift.OpenedAt,
            ClosedAt = shift.ClosedAt,
            InitialAmount = shift.InitialAmount,
            TotalCash = shift.TotalCash,
            TotalCard = shift.TotalCard,
            TotalTransfer = shift.TotalTransfer,
            Status = shift.Status,
            Notes = shift.Notes
        });
    }

    [HttpPost("open")]
    public async Task<ActionResult<CashShiftDto>> OpenShift([FromBody] OpenShiftDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        int userId = _currentUserService.UserId ?? 1;

        // Validar si ya hay un turno abierto
        bool alreadyOpen = await _dbContext.CashShifts
            .AnyAsync(cs => cs.HotelId == targetHotelId && cs.Status == ShiftStatus.Abierto);

        if (alreadyOpen)
        {
            return BadRequest(new { message = "Ya existe un turno de caja abierto en esta sede. Debe cerrarlo antes de abrir uno nuevo." });
        }

        var shift = new CashShift
        {
            HotelId = targetHotelId,
            UserId = userId,
            OpenedAt = DateTime.UtcNow,
            InitialAmount = dto.InitialAmount,
            TotalCash = 0,
            TotalCard = 0,
            TotalTransfer = 0,
            Status = ShiftStatus.Abierto,
            Notes = dto.Notes
        };

        _dbContext.CashShifts.Add(shift);
        await _dbContext.SaveChangesAsync();

        return Ok(new CashShiftDto
        {
            CashShiftId = shift.CashShiftId,
            HotelId = shift.HotelId,
            UserId = shift.UserId,
            UserName = _currentUserService.FullName ?? "Usuario",
            OpenedAt = shift.OpenedAt,
            InitialAmount = shift.InitialAmount,
            TotalCash = 0,
            TotalCard = 0,
            TotalTransfer = 0,
            Status = shift.Status,
            Notes = shift.Notes
        });
    }

    // RF-42 / HU-20: Cierre de caja por turno con congelamiento de totales
    [HttpPost("close")]
    public async Task<IActionResult> CloseShift([FromBody] CloseShiftDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var shift = await _dbContext.CashShifts
            .Include(cs => cs.Payments)
            .FirstOrDefaultAsync(cs => cs.HotelId == targetHotelId && cs.Status == ShiftStatus.Abierto);

        if (shift == null)
        {
            return BadRequest(new { message = "No hay ningún turno de caja abierto para cerrar." });
        }

        // Congelar los totales a partir de los pagos registrados vinculados a este turno
        decimal cashTotal = shift.Payments.Where(p => p.PaymentMethod == PaymentMethod.Efectivo).Sum(p => p.Amount);
        decimal cardTotal = shift.Payments.Where(p => p.PaymentMethod == PaymentMethod.TarjetaDebito || p.PaymentMethod == PaymentMethod.TarjetaCredito).Sum(p => p.Amount);
        decimal transferTotal = shift.Payments.Where(p => p.PaymentMethod == PaymentMethod.Transferencia).Sum(p => p.Amount);

        shift.TotalCash = cashTotal;
        shift.TotalCard = cardTotal;
        shift.TotalTransfer = transferTotal;
        shift.Status = ShiftStatus.Cerrado; // Congelado (RF-42)
        shift.ClosedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            shift.Notes = (shift.Notes + " | Cierre: " + dto.Notes).Trim();
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Turno de caja cerrado exitosamente. Los totales por método de pago han quedado congelados (RF-42).",
            closedAt = shift.ClosedAt,
            totalCash = shift.TotalCash,
            totalCard = shift.TotalCard,
            totalTransfer = shift.TotalTransfer,
            totalCollected = shift.TotalCash + shift.TotalCard + shift.TotalTransfer,
            finalBalance = shift.InitialAmount + shift.TotalCash
        });
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<CashShiftDto>>> GetShiftHistory([FromQuery] int limit = 10)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var shifts = await _dbContext.CashShifts
            .Include(cs => cs.User)
            .Where(cs => cs.HotelId == targetHotelId)
            .OrderByDescending(cs => cs.OpenedAt)
            .Take(limit)
            .Select(cs => new CashShiftDto
            {
                CashShiftId = cs.CashShiftId,
                HotelId = cs.HotelId,
                UserId = cs.UserId,
                UserName = cs.User.FullName,
                OpenedAt = cs.OpenedAt,
                ClosedAt = cs.ClosedAt,
                InitialAmount = cs.InitialAmount,
                TotalCash = cs.TotalCash,
                TotalCard = cs.TotalCard,
                TotalTransfer = cs.TotalTransfer,
                Status = cs.Status,
                Notes = cs.Notes
            })
            .ToListAsync();

        return Ok(shifts);
    }
}
