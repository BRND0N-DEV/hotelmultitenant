using System;
using System.Collections.Generic;
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
using HotelManagement.Domain.Exceptions;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReservationsController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IQrCodeService _qrCodeService;
    private readonly IEmailNotificationService _emailService;

    public ReservationsController(
        IHotelDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IQrCodeService qrCodeService,
        IEmailNotificationService emailService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _qrCodeService = qrCodeService;
        _emailService = emailService;
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Auditor}")]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReservationDto>>> GetReservations(
        [FromQuery] ReservationStatus? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? search)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var query = _dbContext.Reservations
            .Include(r => r.Hotel)
            .Include(r => r.Room)
                .ThenInclude(rm => rm.RoomType)
            .Include(r => r.Guest)
            .Include(r => r.Folio)
            .Where(r => r.HotelId == targetHotelId)
            .AsQueryable();

        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        if (fromDate.HasValue) query = query.Where(r => r.CheckInDate >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(r => r.CheckOutDate <= toDate.Value.Date);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Code.ToLower().Contains(term) ||
                                     r.Guest.FirstName.ToLower().Contains(term) ||
                                     r.Guest.LastName.ToLower().Contains(term) ||
                                     r.Guest.DocumentNumber.Contains(term) ||
                                     r.Room.RoomNumber.Contains(term));
        }

        var list = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReservationDto
            {
                ReservationId = r.ReservationId,
                HotelId = r.HotelId,
                HotelName = r.Hotel.Name,
                Code = r.Code,
                RoomId = r.RoomId,
                RoomNumber = r.Room.RoomNumber,
                RoomTypeName = r.Room.RoomType.Name,
                GuestId = r.GuestId,
                GuestName = r.Guest.FullName,
                GuestDocument = r.Guest.DocumentNumber,
                GuestEmail = r.Guest.Email,
                CheckInDate = r.CheckInDate,
                CheckOutDate = r.CheckOutDate,
                AdultCount = r.AdultCount,
                ChildCount = r.ChildCount,
                Status = r.Status,
                TotalAmount = r.TotalAmount,
                FolioBalance = r.Folio != null ? r.Folio.Balance : 0,
                RowVersion = Convert.ToBase64String(r.RowVersion),
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Ok(list);
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Auditor}")]
    [HttpGet("{id}")]
    public async Task<ActionResult<ReservationDetailDto>> GetReservation(int id)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Hotel)
            .Include(r => r.Room)
                .ThenInclude(rm => rm.RoomType)
            .Include(r => r.Guest)
            .Include(r => r.CancellationPolicy)
            .Include(r => r.Companions)
            .Include(r => r.StateHistories)
                .ThenInclude(sh => sh.ChangedByUser)
            .Include(r => r.Folio)
                .ThenInclude(f => f!.Charges)
                    .ThenInclude(c => c.CreatedByUser)
            .Include(r => r.Folio)
                .ThenInclude(f => f!.Payments)
                    .ThenInclude(p => p.CreatedByUser)
            .FirstOrDefaultAsync(r => r.ReservationId == id);

        if (r == null) return NotFound(new { message = "Reserva no encontrada." });

        FolioDto? folioDto = null;
        if (r.Folio != null)
        {
            folioDto = new FolioDto
            {
                FolioId = r.Folio.FolioId,
                ReservationId = r.ReservationId,
                ReservationCode = r.Code,
                GuestName = r.Guest.FullName,
                RoomNumber = r.Room.RoomNumber,
                TotalCharges = r.Folio.TotalCharges,
                TotalTaxes = r.Folio.TotalTaxes,
                TotalPayments = r.Folio.TotalPayments,
                Balance = r.Folio.Balance,
                Status = r.Folio.Status,
                ClosedAt = r.Folio.ClosedAt,
                Charges = r.Folio.Charges.OrderBy(c => c.CreatedAt).Select(c => new FolioChargeDto
                {
                    ChargeId = c.ChargeId,
                    ServiceId = c.ServiceId,
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
                Payments = r.Folio.Payments.OrderBy(p => p.CreatedAt).Select(p => new FolioPaymentDto
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
            };
        }

        return Ok(new ReservationDetailDto
        {
            ReservationId = r.ReservationId,
            HotelId = r.HotelId,
            HotelName = r.Hotel.Name,
            Code = r.Code,
            RoomId = r.RoomId,
            RoomNumber = r.Room.RoomNumber,
            RoomTypeName = r.Room.RoomType.Name,
            GuestId = r.GuestId,
            GuestName = r.Guest.FullName,
            GuestDocument = r.Guest.DocumentNumber,
            GuestEmail = r.Guest.Email,
            CheckInDate = r.CheckInDate,
            CheckOutDate = r.CheckOutDate,
            AdultCount = r.AdultCount,
            ChildCount = r.ChildCount,
            Status = r.Status,
            BaseRate = r.BaseRate,
            DiscountAmount = r.DiscountAmount,
            TaxAmount = r.TaxAmount,
            TotalAmount = r.TotalAmount,
            PenaltyAmount = r.PenaltyAmount,
            SpecialRequests = r.SpecialRequests,
            CancellationPolicyName = r.CancellationPolicy.Name,
            FreeCancellationHours = r.CancellationPolicy.FreeCancellationHours,
            FolioBalance = r.Folio?.Balance ?? 0,
            RowVersion = Convert.ToBase64String(r.RowVersion),
            CreatedAt = r.CreatedAt,
            Companions = r.Companions.Select(c => new CompanionDto
            {
                DocumentType = c.DocumentType,
                DocumentNumber = c.DocumentNumber,
                FullName = c.FullName,
                Relationship = c.Relationship
            }).ToList(),
            StateHistories = r.StateHistories.OrderBy(h => h.ChangedAt).Select(h => new ReservationStateHistoryDto
            {
                PreviousStatus = h.PreviousStatus,
                NewStatus = h.NewStatus,
                ChangedByUserName = h.ChangedByUser?.FullName ?? "Sistema",
                ChangedAt = h.ChangedAt,
                Reason = h.Reason
            }).ToList(),
            Folio = folioDto
        });
    }

    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost]
    public async Task<ActionResult<ReservationDto>> CreateReservation([FromBody] CreateReservationDto dto)
    {
        // 1. Manejo de Idempotencia (RF-23 / HU-10)
        string? idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cached = await _dbContext.IdempotencyKeys.FirstOrDefaultAsync(k => k.Key == idempotencyKey);
            if (cached != null)
            {
                return StatusCode(cached.ResponseCode, JsonSerializer.Deserialize<ReservationDto>(cached.ResponseBody));
            }
        }

        // 2. Validación de Habeas Data Consent (RF-45, RF-46 / HU-22)
        if (!dto.PrimaryGuest.AcceptDataConsent)
        {
            return BadRequest(new { message = "Error RF-46: Es obligatorio el consentimiento expreso de tratamiento de datos personales (Habeas Data) para crear la reserva." });
        }

        if (dto.CheckOutDate <= dto.CheckInDate)
        {
            return BadRequest(new { message = "La fecha de salida debe ser posterior a la de entrada." });
        }

        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        // 3. Prevención de Overbooking en código previo a BD (RF-24, RNF-11)
        bool hasConflict = await _dbContext.Reservations
            .AnyAsync(r => r.RoomId == dto.RoomId &&
                           (r.Status == ReservationStatus.Pendiente ||
                            r.Status == ReservationStatus.Confirmada ||
                            r.Status == ReservationStatus.CheckIn) &&
                           r.CheckInDate < dto.CheckOutDate.Date &&
                           r.CheckOutDate > dto.CheckInDate.Date);

        if (hasConflict)
        {
            return Conflict(new { message = "Conflicto de disponibilidad: La habitación ya se encuentra reservada para el rango de fechas solicitado." });
        }

        var room = await _dbContext.Rooms
            .Include(r => r.RoomType)
            .FirstOrDefaultAsync(r => r.RoomId == dto.RoomId && r.HotelId == targetHotelId);

        if (room == null) return NotFound(new { message = "Habitación no encontrada en la sede activa." });

        var policy = await _dbContext.CancellationPolicies
            .FirstOrDefaultAsync(p => p.PolicyId == dto.CancellationPolicyId && p.HotelId == targetHotelId)
            ?? await _dbContext.CancellationPolicies.FirstOrDefaultAsync(p => p.HotelId == targetHotelId && p.IsActive);

        if (policy == null) return BadRequest(new { message = "No hay políticas de cancelación configuradas." });

        // 4. Registro o búsqueda de huésped titular
        var guest = await _dbContext.Guests
            .FirstOrDefaultAsync(g => g.HotelId == targetHotelId &&
                                      g.DocumentNumber == dto.PrimaryGuest.DocumentNumber.Trim());

        if (guest == null)
        {
            guest = new Guest
            {
                HotelId = targetHotelId,
                DocumentType = dto.PrimaryGuest.DocumentType,
                DocumentNumber = dto.PrimaryGuest.DocumentNumber.Trim(),
                FirstName = dto.PrimaryGuest.FirstName.Trim(),
                LastName = dto.PrimaryGuest.LastName.Trim(),
                Email = dto.PrimaryGuest.Email.Trim().ToLowerInvariant(),
                Phone = dto.PrimaryGuest.Phone.Trim(),
                Nationality = dto.PrimaryGuest.Nationality.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Guests.Add(guest);
            await _dbContext.SaveChangesAsync();
        }

        // Registrar consentimiento
        _dbContext.DataConsents.Add(new DataConsent
        {
            GuestId = guest.GuestId,
            Accepted = true,
            AcceptedAt = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
            PolicyVersion = "1.0"
        });

        // 5. Cálculo tarifario desglosado
        var seasons = await _dbContext.Seasons.Where(s => s.HotelId == targetHotelId && s.IsActive).ToListAsync();
        var taxes = await _dbContext.Taxes.Where(t => t.HotelId == targetHotelId && t.IsActive).ToListAsync();
        var promotion = string.IsNullOrWhiteSpace(dto.PromotionCode)
            ? null
            : await _dbContext.Promotions.FirstOrDefaultAsync(p => p.HotelId == targetHotelId && p.Code == dto.PromotionCode.Trim().ToUpperInvariant() && p.IsActive);

        var pricing = PricingEngine.CalculateStayBreakdown(
            room.RoomType,
            dto.CheckInDate,
            dto.CheckOutDate,
            seasons,
            promotion,
            taxes);

        // 6. Generación de código único de reserva
        string hotelCode = (await _dbContext.Hotels.FindAsync(targetHotelId))?.Code ?? "HTL";
        string code = $"RES-{hotelCode}-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

        var reservation = new Reservation
        {
            HotelId = targetHotelId,
            RoomId = room.RoomId,
            GuestId = guest.GuestId,
            CancellationPolicyId = policy.PolicyId,
            Code = code,
            CheckInDate = dto.CheckInDate.Date,
            CheckOutDate = dto.CheckOutDate.Date,
            AdultCount = dto.Adults,
            ChildCount = dto.Children,
            Status = ReservationStatus.Pendiente, // RF-22: Estado inicial Pendiente
            BaseRate = pricing.BaseAmount,
            DiscountAmount = pricing.DiscountAmount,
            TaxAmount = pricing.TaxAmount,
            TotalAmount = pricing.TotalAmount,
            SpecialRequests = dto.SpecialRequests,
            CreatedAt = DateTime.UtcNow
        };

        // Acompañantes
        foreach (var c in dto.Companions)
        {
            reservation.Companions.Add(new ReservationCompanion
            {
                DocumentType = c.DocumentType,
                DocumentNumber = c.DocumentNumber.Trim(),
                FullName = c.FullName.Trim(),
                Relationship = c.Relationship
            });
        }

        // Historial inicial
        reservation.StateHistories.Add(new ReservationStateHistory
        {
            PreviousStatus = "Ninguno",
            NewStatus = ReservationStatus.Pendiente.ToString(),
            ChangedByUserId = _currentUserService.UserId ?? 1,
            ChangedAt = DateTime.UtcNow,
            Reason = "Creación de la reserva en flujo integrado (RF-22)"
        });

        _dbContext.Reservations.Add(reservation);
        await _dbContext.SaveChangesAsync();

        // 7. Crear Folio automático asociado
        int nights = (int)(reservation.CheckOutDate - reservation.CheckInDate).TotalDays;
        var folio = new Folio
        {
            ReservationId = reservation.ReservationId,
            HotelId = targetHotelId,
            TotalCharges = pricing.BaseAmount - pricing.DiscountAmount,
            TotalTaxes = pricing.TaxAmount,
            TotalPayments = 0,
            Balance = pricing.TotalAmount,
            Status = FolioStatus.Abierto,
            CreatedAt = DateTime.UtcNow
        };

        folio.Charges.Add(new FolioCharge
        {
            Description = $"Alojamiento {nights} noche(s) - Habitación {room.RoomNumber} ({room.RoomType.Name})",
            Quantity = nights,
            UnitPrice = Math.Round((pricing.BaseAmount - pricing.DiscountAmount) / nights, 2),
            Amount = pricing.BaseAmount - pricing.DiscountAmount,
            TaxAmount = pricing.TaxAmount,
            IsAnnulled = false,
            CreatedByUserId = _currentUserService.UserId ?? 1,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.Folios.Add(folio);
        await _dbContext.SaveChangesAsync();

        // 8. Envío asíncrono de correo con QR (RF-43, RF-44)
        var verificationUrl = $"{Request.Scheme}://{Request.Host}/api/reservations/verify-voucher/{reservation.Code}";
        var qrCodeBytes = _qrCodeService.GenerateQrCodePng(verificationUrl);
        _ = _emailService.SendReservationVoucherAsync(reservation, guest.Email, qrCodeBytes);

        var resultDto = new ReservationDto
        {
            ReservationId = reservation.ReservationId,
            HotelId = reservation.HotelId,
            HotelName = (await _dbContext.Hotels.FindAsync(targetHotelId))?.Name ?? "",
            Code = reservation.Code,
            RoomId = reservation.RoomId,
            RoomNumber = room.RoomNumber,
            RoomTypeName = room.RoomType.Name,
            GuestId = guest.GuestId,
            GuestName = guest.FullName,
            GuestDocument = guest.DocumentNumber,
            GuestEmail = guest.Email,
            CheckInDate = reservation.CheckInDate,
            CheckOutDate = reservation.CheckOutDate,
            AdultCount = reservation.AdultCount,
            ChildCount = reservation.ChildCount,
            Status = reservation.Status,
            TotalAmount = reservation.TotalAmount,
            FolioBalance = folio.Balance,
            RowVersion = Convert.ToBase64String(reservation.RowVersion),
            CreatedAt = reservation.CreatedAt
        };

        // Guardar clave de idempotencia si se envió
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _dbContext.IdempotencyKeys.Add(new IdempotencyKey
            {
                Key = idempotencyKey,
                Endpoint = "/api/reservations",
                ResponseCode = 201,
                ResponseBody = JsonSerializer.Serialize(resultDto),
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetReservation), new { id = reservation.ReservationId }, resultDto);
    }

    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("{id}/check-in")]
    public async Task<IActionResult> CheckIn(int id)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Room)
            .Include(r => r.Guest)
                .ThenInclude(g => g.DataConsents)
            .FirstOrDefaultAsync(r => r.ReservationId == id);

        if (r == null) return NotFound(new { message = "Reserva no encontrada." });

        // RF-31: Impedir check-in si la habitación no está Limpia
        if (r.Room.CleaningStatus != RoomCleaningStatus.Limpia || r.Room.OperationalStatus != RoomOperationalStatus.Disponible)
        {
            // Buscar habitaciones alternativas del mismo tipo que sí estén limpias
            var availableAlternative = await _dbContext.Rooms
                .Where(rm => rm.RoomTypeId == r.Room.RoomTypeId &&
                             rm.RoomId != r.RoomId &&
                             rm.CleaningStatus == RoomCleaningStatus.Limpia &&
                             rm.OperationalStatus == RoomOperationalStatus.Disponible)
                .Select(rm => rm.RoomNumber)
                .FirstOrDefaultAsync();

            string altMsg = availableAlternative != null
                ? $" Habitación alternativa limpia sugerida: {availableAlternative}."
                : "";

            return BadRequest(new
            {
                message = $"Error RF-31: No se puede completar el Check-In porque la habitación '{r.Room.RoomNumber}' no está Limpia/Disponible (Estado actual: {r.Room.CleaningStatus}/{r.Room.OperationalStatus}).{altMsg}"
            });
        }

        // RF-31: Validar consentimiento Habeas Data
        bool hasConsent = r.Guest.DataConsents.Any(dc => dc.Accepted);
        if (!hasConsent)
        {
            return BadRequest(new { message = "Error RF-31 / RF-46: Falta el registro de consentimiento de tratamiento de datos personales del huésped." });
        }

        // RF-26: Validar transición de estado
        ReservationStateMachine.ValidateTransition(r.Status, ReservationStatus.CheckIn);

        var prevStatus = r.Status.ToString();
        r.Status = ReservationStatus.CheckIn; // RF-32
        r.Room.OperationalStatus = RoomOperationalStatus.Ocupada; // RF-32

        r.StateHistories.Add(new ReservationStateHistory
        {
            PreviousStatus = prevStatus,
            NewStatus = ReservationStatus.CheckIn.ToString(),
            ChangedByUserId = _currentUserService.UserId ?? 1,
            ChangedAt = DateTime.UtcNow,
            Reason = "Check-In completado en recepción con habitación limpia y consentimiento validado."
        });

        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Check-In completado exitosamente. La habitación ahora está Ocupada." });
    }

    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("{id}/check-out")]
    public async Task<IActionResult> CheckOut(int id)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Room)
            .Include(r => r.Folio)
                .ThenInclude(f => f!.Charges)
            .Include(r => r.Folio)
                .ThenInclude(f => f!.Payments)
            .FirstOrDefaultAsync(r => r.ReservationId == id);

        if (r == null) return NotFound(new { message = "Reserva no encontrada." });

        if (r.Folio != null)
        {
            r.Folio.RecalculateTotals();

            // RF-40: Impedir check-out cuando el saldo sea distinto de cero
            if (r.Folio.Balance != 0)
            {
                return BadRequest(new
                {
                    message = $"Error RF-40: No es posible realizar el Check-Out porque el folio tiene un saldo pendiente de ${r.Folio.Balance:N2} COP. El saldo debe ser exactamente $0.00.",
                    pendingBalance = r.Folio.Balance
                });
            }

            r.Folio.Status = FolioStatus.Cerrado;
            r.Folio.ClosedAt = DateTime.UtcNow;
        }

        ReservationStateMachine.ValidateTransition(r.Status, ReservationStatus.CheckOut);

        var prevStatus = r.Status.ToString();
        r.Status = ReservationStatus.Liquidada; // RF-41: Pasa a Liquidada cuando el folio está cerrado y saldo cero
        r.Room.OperationalStatus = RoomOperationalStatus.Disponible;
        r.Room.CleaningStatus = RoomCleaningStatus.Sucia; // RF-34: Habitación pasa automáticamente a Sucia al Check-Out

        r.StateHistories.Add(new ReservationStateHistory
        {
            PreviousStatus = prevStatus,
            NewStatus = ReservationStatus.Liquidada.ToString(),
            ChangedByUserId = _currentUserService.UserId ?? 1,
            ChangedAt = DateTime.UtcNow,
            Reason = "Check-Out y liquidación exitosa de estancia con saldo cero (RF-34, RF-40, RF-41)."
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Check-Out realizado correctamente. Habitación enviada a limpieza (Sucia) y reserva Liquidada.",
            invoicePdfUrl = $"/api/reports/invoice/{r.ReservationId}"
        });
    }

    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("{id}/state")]
    public async Task<IActionResult> ChangeState(int id, [FromBody] ChangeReservationStateDto dto)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Room)
            .FirstOrDefaultAsync(r => r.ReservationId == id);

        if (r == null) return NotFound(new { message = "Reserva no encontrada." });

        // RF-26: Validación formal de transiciones (HTTP 422 si no está permitida)
        ReservationStateMachine.ValidateTransition(r.Status, dto.NewStatus);

        var prevStatus = r.Status.ToString();
        r.Status = dto.NewStatus;

        // Liberar habitación si se anula o marca No-Show (RF-28)
        if (dto.NewStatus == ReservationStatus.Anulada || dto.NewStatus == ReservationStatus.NoShow)
        {
            if (r.Room.OperationalStatus == RoomOperationalStatus.Ocupada)
            {
                r.Room.OperationalStatus = RoomOperationalStatus.Disponible;
            }
        }

        r.StateHistories.Add(new ReservationStateHistory
        {
            PreviousStatus = prevStatus,
            NewStatus = dto.NewStatus.ToString(),
            ChangedByUserId = _currentUserService.UserId ?? 1,
            ChangedAt = DateTime.UtcNow,
            Reason = dto.Reason ?? $"Cambio manual de estado a {dto.NewStatus}"
        });

        await _dbContext.SaveChangesAsync();
        return Ok(new { message = $"Estado de la reserva actualizado a '{dto.NewStatus}'.", status = r.Status });
    }

    [Authorize(Roles = $"{UserRole.AdminSede},{UserRole.Recepcionista}")]
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelReservation(int id, [FromBody] CancelReservationDto dto)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Room)
            .Include(r => r.CancellationPolicy)
            .Include(r => r.Folio)
            .FirstOrDefaultAsync(r => r.ReservationId == id);

        if (r == null) return NotFound(new { message = "Reserva no encontrada." });

        if (r.Status == ReservationStatus.CheckIn || r.Status == ReservationStatus.CheckOut || r.Status == ReservationStatus.Liquidada)
        {
            return BadRequest(new { message = "No se puede cancelar una estancia que ya ha iniciado o sido completada." });
        }

        // RF-28 / HU-13: Cálculo de penalidad usando la política vigente al momento de reservar
        var hoursUntilCheckIn = (r.CheckInDate - DateTime.UtcNow.Date).TotalHours;
        decimal penalty = 0;

        if (hoursUntilCheckIn < r.CancellationPolicy.FreeCancellationHours)
        {
            penalty = Math.Round(r.TotalAmount * (r.CancellationPolicy.PenaltyPercent / 100m), 2);
        }

        r.PenaltyAmount = penalty;
        var prevStatus = r.Status.ToString();
        r.Status = ReservationStatus.Anulada;

        // Liberar disponibilidad de la habitación
        if (r.Room.OperationalStatus == RoomOperationalStatus.Ocupada)
        {
            r.Room.OperationalStatus = RoomOperationalStatus.Disponible;
        }

        r.StateHistories.Add(new ReservationStateHistory
        {
            PreviousStatus = prevStatus,
            NewStatus = ReservationStatus.Anulada.ToString(),
            ChangedByUserId = _currentUserService.UserId ?? 1,
            ChangedAt = DateTime.UtcNow,
            Reason = $"Cancelación: {dto.Reason}. Penalidad aplicada: ${penalty:N2} COP (Política: {r.CancellationPolicy.Name})"
        });

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Reserva cancelada exitosamente y habitación liberada.",
            penaltyAmount = penalty,
            policyUsed = r.CancellationPolicy.Name
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Auditor}")]
    [HttpGet("{id}/voucher")]
    public async Task<IActionResult> GetVoucher(int id)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Hotel)
            .Include(r => r.Room)
                .ThenInclude(rm => rm.RoomType)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == id);

        if (r == null) return NotFound(new { message = "Reserva no encontrada." });

        var verificationUrl = $"{Request.Scheme}://{Request.Host}/api/reservations/verify-voucher/{r.Code}";
        var qrBytes = _qrCodeService.GenerateQrCodePng(verificationUrl);

        return Ok(new
        {
            code = r.Code,
            reservationCode = r.Code,
            voucherCode = $"VCH-{r.Code}",
            hotelName = r.Hotel.Name,
            hotelCity = r.Hotel.City,
            guestName = r.Guest.FullName,
            roomNumber = r.Room.RoomNumber,
            roomTypeName = r.Room.RoomType.Name,
            checkInDate = r.CheckInDate.ToString("yyyy-MM-dd"),
            checkOutDate = r.CheckOutDate.ToString("yyyy-MM-dd"),
            status = r.Status.ToString(),
            totalAmount = r.TotalAmount,
            verificationUrl = verificationUrl,
            issuedAt = r.CreatedAt,
            qrCodeBase64 = Convert.ToBase64String(qrBytes)
        });
    }

    // Defecto 2 / RF-43: Verificación pública y funcional de vouchers mediante código QR
    [AllowAnonymous]
    [HttpGet("verify-voucher/{code}")]
    public async Task<IActionResult> VerifyVoucher(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Código de voucher no suministrado." });
        }

        var normalizedCode = code.Trim().ToUpperInvariant();
        if (normalizedCode.StartsWith("VCH-"))
        {
            normalizedCode = normalizedCode.Substring(4);
        }

        var r = await _dbContext.Reservations
            .IgnoreQueryFilters()
            .Include(r => r.Hotel)
            .Include(r => r.Room)
                .ThenInclude(rm => rm.RoomType)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.Code.ToUpper() == normalizedCode);

        bool isHtml = Request.Headers.Accept.Any(a => a != null && a.Contains("text/html"));

        if (r == null)
        {
            if (isHtml)
            {
                var notFoundHtml = @"<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='utf-8'/>
    <meta name='viewport' content='width=device-width, initial-scale=1'/>
    <title>Comprobante No Válido | CONTINENTALIA SAS</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0A192F; color: #F8FAFC; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; box-sizing: border-box; }
        .card { background: #0F233D; border: 1px solid #1E3E62; border-radius: 20px; padding: 32px; max-width: 440px; width: 100%; text-align: center; box-shadow: 0 20px 40px rgba(0,0,0,0.4); }
        .icon { font-size: 48px; color: #EF4444; margin-bottom: 16px; }
        h1 { font-size: 20px; margin: 0 0 10px 0; color: #FFFFFF; }
        p { color: #94A3B8; font-size: 14px; line-height: 1.5; margin: 0; }
        .badge { display: inline-block; margin-top: 20px; padding: 6px 14px; background: rgba(239, 68, 68, 0.15); color: #F87171; border-radius: 9999px; font-weight: 700; font-size: 12px; }
    </style>
</head>
<body>
    <div class='card'>
        <div class='icon'>&#10006;</div>
        <h1>Comprobante No Válido</h1>
        <p>El código de voucher o reserva consultado no existe en los registros de CONTINENTALIA SAS o no es válido.</p>
        <span class='badge'>CÓDIGO NO ENCONTRADO</span>
    </div>
</body>
</html>";
                return Content(notFoundHtml, "text/html; charset=utf-8");
            }

            return NotFound(new
            {
                isValid = false,
                message = "El comprobante o voucher especificado no existe o no es válido."
            });
        }

        string statusText = r.Status.ToString();
        bool isCancelled = r.Status == ReservationStatus.Anulada || r.Status == ReservationStatus.NoShow;
        string validityNote = isCancelled
            ? "ATENCIÓN: Esta reserva se encuentra ANULADA o NO PRESENTADA (NO-SHOW)."
            : "Comprobante emitido legítimamente por la plataforma de CONTINENTALIA SAS.";

        string statusBadgeColor = r.Status switch
        {
            ReservationStatus.Confirmada => "#2563EB",
            ReservationStatus.CheckIn => "#10B981",
            ReservationStatus.CheckOut => "#475569",
            ReservationStatus.Liquidada => "#059669",
            ReservationStatus.Anulada => "#DC2626",
            ReservationStatus.NoShow => "#B91C1C",
            _ => "#D97706"
        };

        if (isHtml)
        {
            var html = $@"<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='utf-8'/>
    <meta name='viewport' content='width=device-width, initial-scale=1'/>
    <title>Verificación de Comprobante - {r.Code} | CONTINENTALIA SAS</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0A192F; color: #0F172A; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; box-sizing: border-box; }}
        .card {{ background: #FFFFFF; border-radius: 24px; padding: 32px; max-width: 480px; width: 100%; box-shadow: 0 25px 50px -12px rgba(0,0,0,0.5); border-top: 6px solid #D4AF37; }}
        .brand-header {{ text-align: center; margin-bottom: 24px; border-bottom: 1px solid #E2E8F0; padding-bottom: 16px; }}
        .brand-title {{ font-size: 18px; font-weight: 800; color: #17365D; letter-spacing: 0.05em; }}
        .brand-subtitle {{ font-size: 11px; font-weight: 700; color: #D4AF37; text-transform: uppercase; letter-spacing: 0.1em; }}
        .hotel-name {{ font-size: 16px; font-weight: 700; color: #1E293B; margin-top: 8px; }}
        .hotel-city {{ font-size: 13px; color: #64748B; }}
        .voucher-badge {{ display: inline-block; padding: 6px 14px; border-radius: 9999px; font-size: 12px; font-weight: 700; text-transform: uppercase; color: #FFFFFF; background: {statusBadgeColor}; margin: 12px 0 20px; }}
        .data-grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 20px; text-align: left; }}
        .data-item {{ background: #F8FAFC; padding: 12px; border-radius: 12px; border: 1px solid #F1F5F9; }}
        .data-label {{ font-size: 10px; font-weight: 700; text-transform: uppercase; color: #64748B; margin-bottom: 4px; }}
        .data-value {{ font-size: 13px; font-weight: 700; color: #0F172A; font-family: monospace; }}
        .guest-item {{ grid-column: span 2; }}
        .warning-box {{ background: {(isCancelled ? "#FEF2F2" : "#ECFDF5")}; border: 1px solid {(isCancelled ? "#FECDD3" : "#A7F3D0")}; color: {(isCancelled ? "#991B1B" : "#065F46")}; padding: 12px; border-radius: 12px; font-size: 12px; line-height: 1.4; margin-top: 16px; text-align: center; font-weight: 600; }}
        .footer {{ text-align: center; font-size: 11px; color: #94A3B8; margin-top: 24px; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='brand-header'>
            <div class='brand-title'>CONTINENTALIA SAS</div>
            <div class='brand-subtitle'>Comprobante Oficial de Reserva</div>
            <div class='hotel-name'>{r.Hotel.Name}</div>
            <div class='hotel-city'>{r.Hotel.City} — {r.Hotel.Address}</div>
            <div><span class='voucher-badge'>ESTADO: {statusText}</span></div>
        </div>
        <div class='data-grid'>
            <div class='data-item guest-item'>
                <div class='data-label'>Huésped Titular</div>
                <div class='data-value' style='font-family: inherit;'>{r.Guest.FullName}</div>
            </div>
            <div class='data-item'>
                <div class='data-label'>No. Comprobante</div>
                <div class='data-value'>VCH-{r.Code}</div>
            </div>
            <div class='data-item'>
                <div class='data-label'>Código Reserva</div>
                <div class='data-value'>{r.Code}</div>
            </div>
            <div class='data-item'>
                <div class='data-label'>Habitación</div>
                <div class='data-value'>{r.Room.RoomNumber} ({r.Room.RoomType.Name})</div>
            </div>
            <div class='data-item'>
                <div class='data-label'>Estadía</div>
                <div class='data-value'>{r.CheckInDate:yyyy-MM-dd} al {r.CheckOutDate:yyyy-MM-dd}</div>
            </div>
        </div>
        <div class='warning-box'>{validityNote}</div>
        <div class='footer'>
            Verificado en tiempo real con la base de datos de CONTINENTALIA SAS.<br/>
            Fecha de emisión: {r.CreatedAt:yyyy-MM-dd HH:mm} UTC
        </div>
    </div>
</body>
</html>";
            return Content(html, "text/html; charset=utf-8");
        }

        return Ok(new
        {
            isValid = !isCancelled,
            voucherCode = $"VCH-{r.Code}",
            reservationCode = r.Code,
            hotelName = r.Hotel.Name,
            hotelCity = r.Hotel.City,
            guestName = r.Guest.FullName,
            roomNumber = r.Room.RoomNumber,
            roomTypeName = r.Room.RoomType.Name,
            checkInDate = r.CheckInDate.ToString("yyyy-MM-dd"),
            checkOutDate = r.CheckOutDate.ToString("yyyy-MM-dd"),
            status = statusText,
            isCancelled = isCancelled,
            issuedAt = r.CreatedAt,
            validityMessage = validityNote
        });
    }
}
