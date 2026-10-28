using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HotelManagement.Application.DTOs;
using HotelManagement.Application.Interfaces;
using HotelManagement.Application.Services;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoliosController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public FoliosController(
        IHotelDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Auditor}")]
    [HttpGet("{id}")]
    public async Task<ActionResult<FolioDto>> GetFolio(int id)
    {
        var f = await _dbContext.Folios
            .Include(f => f.Reservation)
                .ThenInclude(r => r.Guest)
            .Include(f => f.Reservation)
                .ThenInclude(r => r.Room)
            .Include(f => f.Charges)
                .ThenInclude(c => c.CreatedByUser)
            .Include(f => f.Charges)
                .ThenInclude(c => c.Service)
            .Include(f => f.Payments)
                .ThenInclude(p => p.CreatedByUser)
            .FirstOrDefaultAsync(f => f.FolioId == id);

        if (f == null) return NotFound(new { message = "Folio no encontrado." });

        f.RecalculateTotals();

        return Ok(new FolioDto
        {
            FolioId = f.FolioId,
            ReservationId = f.ReservationId,
            ReservationCode = f.Reservation.Code,
            GuestName = f.Reservation.Guest.FullName,
            RoomNumber = f.Reservation.Room.RoomNumber,
            TotalCharges = f.TotalCharges,
            TotalTaxes = f.TotalTaxes,
            TotalPayments = f.TotalPayments,
            Balance = f.Balance,
            Status = f.Status,
            ClosedAt = f.ClosedAt,
            Charges = f.Charges.OrderBy(c => c.CreatedAt).Select(c => new FolioChargeDto
            {
                ChargeId = c.ChargeId,
                ServiceId = c.ServiceId,
                ServiceName = c.Service?.Name,
                Description = c.Description,
                Quantity = c.Quantity,
                UnitPrice = c.UnitPrice,
                Amount = c.Amount,
                TaxAmount = c.TaxAmount,
                IsAnnulled = c.IsAnnulled,
                AnnulledByChargeId = c.AnnulledByChargeId,
                AnnulmentReason = c.AnnulmentReason,
                CreatedByUserName = c.CreatedByUser?.FullName ?? "Sistema",
                CreatedAt = c.CreatedAt
            }).ToList(),
            Payments = f.Payments.OrderBy(p => p.CreatedAt).Select(p => new FolioPaymentDto
            {
                PaymentId = p.PaymentId,
                CashShiftId = p.CashShiftId,
                Amount = p.Amount,
                PaymentMethod = p.PaymentMethod,
                Reference = p.Reference,
                ReceiptNumber = p.ReceiptNumber,
                CreatedByUserName = p.CreatedByUser?.FullName ?? "Sistema",
                CreatedAt = p.CreatedAt
            }).ToList()
        });
    }

    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("{id}/charges")]
    public async Task<ActionResult<FolioChargeDto>> AddCharge(int id, [FromBody] AddChargeDto dto)
    {
        var folio = await _dbContext.Folios
            .Include(f => f.Charges)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.FolioId == id);

        if (folio == null) return NotFound(new { message = "Folio no encontrado." });

        if (folio.Status == FolioStatus.Cerrado)
        {
            return BadRequest(new { message = "El folio se encuentra cerrado. No se pueden registrar más consumos." });
        }

        AdditionalService? service = null;
        if (dto.ServiceId.HasValue)
        {
            service = await _dbContext.AdditionalServices.FindAsync(dto.ServiceId.Value);

            // RF-17: Los servicios inactivos no deben estar disponibles para registrar consumos
            if (service == null || !service.IsActive)
            {
                return BadRequest(new { message = "Error RF-17: El servicio seleccionado está inactivo o no existe en esta sede." });
            }
        }

        decimal unitPrice = dto.UnitPrice > 0 ? dto.UnitPrice : (service?.Price ?? 0);
        decimal amount = Math.Round(unitPrice * dto.Quantity, 2);
        decimal taxPercent = dto.TaxPercentage ?? (service?.Category == "Desayuno" || service?.Category == "Restaurante" ? 8.0m : 19.0m);
        decimal taxAmount = Math.Round(amount * (taxPercent / 100m), 2);

        var charge = new FolioCharge
        {
            FolioId = folio.FolioId,
            ServiceId = dto.ServiceId,
            Description = !string.IsNullOrWhiteSpace(dto.Description) ? dto.Description.Trim() : (service?.Name ?? "Consumo adicional"),
            Quantity = dto.Quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            TaxAmount = taxAmount,
            IsAnnulled = false,
            CreatedByUserId = _currentUserService.UserId ?? 1,
            CreatedAt = DateTime.UtcNow
        };

        folio.Charges.Add(charge);
        folio.RecalculateTotals();

        await _dbContext.SaveChangesAsync();

        return Ok(new FolioChargeDto
        {
            ChargeId = charge.ChargeId,
            ServiceId = charge.ServiceId,
            ServiceName = service?.Name,
            Description = charge.Description,
            Quantity = charge.Quantity,
            UnitPrice = charge.UnitPrice,
            Amount = charge.Amount,
            TaxAmount = charge.TaxAmount,
            IsAnnulled = false,
            CreatedByUserName = _currentUserService.FullName ?? "Recepción",
            CreatedAt = charge.CreatedAt
        });
    }

    // RF-36 / HU-17: Los cargos no se editan directamente: corrección mediante anulación con motivo y contra-asiento auditado
    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("charges/{chargeId}/annul")]
    public async Task<IActionResult> AnnulCharge(int chargeId, [FromBody] AnnulChargeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "Es obligatorio ingresar un motivo para anular el cargo." });
        }

        var charge = await _dbContext.FolioCharges
            .Include(c => c.Folio)
                .ThenInclude(f => f.Charges)
            .Include(c => c.Folio)
                .ThenInclude(f => f.Payments)
            .FirstOrDefaultAsync(c => c.ChargeId == chargeId);

        if (charge == null) return NotFound(new { message = "Cargo no encontrado." });

        if (charge.IsAnnulled)
        {
            return BadRequest(new { message = "El cargo ya se encuentra anulado." });
        }

        int userId = _currentUserService.UserId ?? 1;

        // Generar el contra-asiento contable
        var counterEntry = FolioEngine.CreateCounterEntry(charge, userId, dto.Reason.Trim());
        _dbContext.FolioCharges.Add(counterEntry);

        charge.Folio.Charges.Add(counterEntry);
        charge.Folio.RecalculateTotals();

        // Registrar auditoría de negocio para anulación
        _dbContext.AuditLogs.Add(new AuditLog
        {
            HotelId = _tenantContext.CurrentHotelId,
            UserId = userId,
            Level = AuditLevel.Negocio,
            Action = $"Anulación de Cargo #{charge.ChargeId}",
            EntityName = nameof(FolioCharge),
            EntityId = charge.ChargeId.ToString(),
            OldValues = JsonSerializer.Serialize(new { charge.Description, charge.Amount, charge.TaxAmount }),
            NewValues = JsonSerializer.Serialize(new { counterEntry.Description, counterEntry.Amount, Motivo = dto.Reason }),
            Timestamp = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Cargo anulado exitosamente mediante contra-asiento contable auditado (RF-36).",
            counterEntryId = counterEntry.ChargeId,
            newFolioBalance = charge.Folio.Balance
        });
    }

    // RF-38, RF-39 / HU-18: Registro de pagos y abonos con idempotencia y emisión de recibo
    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("{id}/payments")]
    public async Task<ActionResult<FolioPaymentDto>> AddPayment(int id, [FromBody] AddPaymentDto dto)
    {
        // Soporte de idempotencia
        string? idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cached = await _dbContext.IdempotencyKeys.FirstOrDefaultAsync(k => k.Key == idempotencyKey);
            if (cached != null)
            {
                return StatusCode(cached.ResponseCode, JsonSerializer.Deserialize<FolioPaymentDto>(cached.ResponseBody));
            }
        }

        var folio = await _dbContext.Folios
            .Include(f => f.Charges)
            .Include(f => f.Payments)
            .Include(f => f.Reservation)
            .FirstOrDefaultAsync(f => f.FolioId == id);

        if (folio == null) return NotFound(new { message = "Folio no encontrado." });

        if (dto.Amount <= 0)
        {
            return BadRequest(new { message = "El importe del pago debe ser mayor a cero." });
        }

        string receiptNumber = $"REC-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
        int userId = _currentUserService.UserId ?? 1;

        // Actualizar totales en turno de caja si está abierto (RF-42)
        CashShift? shift = null;
        if (dto.CashShiftId.HasValue)
        {
            shift = await _dbContext.CashShifts.FindAsync(dto.CashShiftId.Value);
        }
        else
        {
            int targetHotelId = folio.Reservation?.HotelId ?? _tenantContext.CurrentHotelId ?? 1;
            shift = await _dbContext.CashShifts
                .FirstOrDefaultAsync(s => s.HotelId == targetHotelId && s.UserId == userId && s.Status == ShiftStatus.Abierto)
                ?? await _dbContext.CashShifts
                .FirstOrDefaultAsync(s => s.HotelId == targetHotelId && s.Status == ShiftStatus.Abierto);
        }

        var payment = new FolioPayment
        {
            FolioId = folio.FolioId,
            CashShiftId = shift != null && shift.Status == ShiftStatus.Abierto ? shift.CashShiftId : dto.CashShiftId,
            Amount = dto.Amount,
            PaymentMethod = dto.PaymentMethod,
            Reference = string.IsNullOrWhiteSpace(dto.Reference) ? "PAGO-MOSTRADOR" : dto.Reference.Trim(),
            ReceiptNumber = receiptNumber,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        folio.Payments.Add(payment);
        folio.RecalculateTotals();

        if (shift != null && shift.Status == ShiftStatus.Abierto)
        {
            if (dto.PaymentMethod == PaymentMethod.Efectivo) shift.TotalCash += dto.Amount;
            else if (dto.PaymentMethod == PaymentMethod.TarjetaDebito || dto.PaymentMethod == PaymentMethod.TarjetaCredito) shift.TotalCard += dto.Amount;
            else if (dto.PaymentMethod == PaymentMethod.Transferencia) shift.TotalTransfer += dto.Amount;
        }

        await _dbContext.SaveChangesAsync();

        var result = new FolioPaymentDto
        {
            PaymentId = payment.PaymentId,
            CashShiftId = payment.CashShiftId,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            Reference = payment.Reference,
            ReceiptNumber = payment.ReceiptNumber,
            CreatedByUserName = _currentUserService.FullName ?? "Recepción",
            CreatedAt = payment.CreatedAt
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _dbContext.IdempotencyKeys.Add(new IdempotencyKey
            {
                Key = idempotencyKey,
                Endpoint = $"/api/folios/{id}/payments",
                ResponseCode = 200,
                ResponseBody = JsonSerializer.Serialize(result),
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }

        return Ok(result);
    }
}
