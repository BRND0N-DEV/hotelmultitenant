using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
public class ReportsController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public ReportsController(
        IHotelDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    // RF-52, RF-53 / HU-26: Exportación de transacciones con metadatos y totales
    [HttpGet("transactions/excel")]
    public async Task<IActionResult> ExportTransactionsExcel([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        var hotel = await _dbContext.Hotels.FindAsync(targetHotelId);

        var query = _dbContext.FolioPayments
            .Include(p => p.Folio)
                .ThenInclude(f => f.Reservation)
                    .ThenInclude(r => r.Guest)
            .Include(p => p.CreatedByUser)
            .Where(p => p.Folio.HotelId == targetHotelId)
            .AsQueryable();

        if (fromDate.HasValue) query = query.Where(p => p.CreatedAt >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(p => p.CreatedAt <= toDate.Value.Date.AddDays(1));

        var payments = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"REPORTE DE TRANSACCIONES Y PAGOS - {hotel?.Name?.ToUpperInvariant()}");
        sb.AppendLine($"Emitido por: {_currentUserService.FullName} ({_currentUserService.Email}) | Fecha Emisión: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Filtro: Desde {(fromDate?.ToString("yyyy-MM-dd") ?? "Inicio")} Hasta {(toDate?.ToString("yyyy-MM-dd") ?? "Hoy")}");
        sb.AppendLine();
        sb.AppendLine("ID Transacción,Código Reserva,Huésped,Fecha y Hora,Método de Pago,Referencia,No. Recibo,Importe COP,Cajero");

        decimal totalAmount = 0;
        foreach (var p in payments)
        {
            totalAmount += p.Amount;
            sb.AppendLine($"{p.PaymentId},\"{p.Folio.Reservation.Code}\",\"{p.Folio.Reservation.Guest.FullName}\",{p.CreatedAt:yyyy-MM-dd HH:mm},{p.PaymentMethod},\"{p.Reference}\",\"{p.ReceiptNumber}\",{p.Amount:F2},\"{p.CreatedByUser?.FullName ?? "Sistema"}\"");
        }

        sb.AppendLine();
        sb.AppendLine($",,,,,TOTAL RECAUDADO,,{totalAmount:F2},");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        string filename = $"Transacciones_{hotel?.Code}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    [HttpGet("guests/excel")]
    public async Task<IActionResult> ExportGuestsExcel()
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        var hotel = await _dbContext.Hotels.FindAsync(targetHotelId);

        var guests = await _dbContext.Guests
            .Include(g => g.Reservations)
            .Where(g => g.HotelId == targetHotelId)
            .OrderBy(g => g.LastName)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"LISTADO GENERAL DE HUÉSPEDES - {hotel?.Name?.ToUpperInvariant()}");
        sb.AppendLine($"Emitido por: {_currentUserService.FullName} | Fecha Emisión: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("ID Huésped,Tipo Documento,Número Documento,Nombre Completo,Correo Electrónico,Teléfono,Nacionalidad,Anonimizado,Reservas Realizadas");

        foreach (var g in guests)
        {
            sb.AppendLine($"{g.GuestId},{g.DocumentType},\"{g.DocumentNumber}\",\"{g.FullName}\",\"{g.Email}\",\"{g.Phone}\",\"{g.Nationality}\",{(g.IsAnonymized ? "SI" : "NO")},{g.Reservations.Count}");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        string filename = $"Huespedes_{hotel?.Code}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    // Comprobante / Factura imprimible en PDF/HTML (RF-41)
    [HttpGet("invoice/{reservationId}")]
    public async Task<IActionResult> GetInvoiceHtml(int reservationId)
    {
        var r = await _dbContext.Reservations
            .Include(r => r.Hotel)
            .Include(r => r.Room)
                .ThenInclude(rm => rm.RoomType)
            .Include(r => r.Guest)
            .Include(r => r.Folio)
                .ThenInclude(f => f!.Charges)
            .Include(r => r.Folio)
                .ThenInclude(f => f!.Payments)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

        if (r == null || r.Folio == null) return NotFound("Reserva o folio no encontrado.");

        var html = $@"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='utf-8'/>
    <title>Comprobante de Estancia - {r.Code}</title>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; margin: 40px; color: #1E293B; }}
        .header {{ display: flex; justify-content: space-between; border-bottom: 2px solid {r.Hotel.PrimaryColor}; padding-bottom: 20px; }}
        .hotel-name {{ font-size: 24px; font-weight: bold; color: {r.Hotel.PrimaryColor}; }}
        .invoice-title {{ font-size: 20px; color: #64748B; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 25px; }}
        th, td {{ border: 1px solid #E2E8F0; padding: 10px; text-align: left; }}
        th {{ background-color: {r.Hotel.SecondaryColor}; color: {r.Hotel.PrimaryColor}; }}
        .total-row {{ font-weight: bold; background-color: #F8FAFC; }}
        .footer {{ margin-top: 40px; text-align: center; color: #94A3B8; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='header'>
        <div>
            <div class='hotel-name'>{r.Hotel.Name}</div>
            <div>{r.Hotel.Address}, {r.Hotel.City}</div>
            <div>Moneda: {r.Hotel.Currency}</div>
        </div>
        <div style='text-align: right;'>
            <div class='invoice-title'>COMPROBANTE FINAL DE ESTANCIA</div>
            <div><strong>Código:</strong> {r.Code}</div>
            <div><strong>Fecha:</strong> {DateTime.UtcNow:yyyy-MM-dd}</div>
        </div>
    </div>
    <div style='margin-top: 20px;'>
        <p><strong>Huésped:</strong> {r.Guest.FullName} ({r.Guest.DocumentType} {r.Guest.DocumentNumber})</p>
        <p><strong>Habitación:</strong> {r.Room.RoomNumber} - {r.Room.RoomType.Name} | <strong>Estancia:</strong> {r.CheckInDate:yyyy-MM-dd} al {r.CheckOutDate:yyyy-MM-dd}</p>
    </div>
    <table>
        <thead>
            <tr><th>Descripción</th><th>Cant.</th><th>Precio Unitario</th><th>Subtotal</th><th>Impuesto</th><th>Total</th></tr>
        </thead>
        <tbody>
            {string.Join("", r.Folio.Charges.Where(c => !c.IsAnnulled).Select(c => $"<tr><td>{c.Description}</td><td>{c.Quantity}</td><td>${c.UnitPrice:N2}</td><td>${c.Amount:N2}</td><td>${c.TaxAmount:N2}</td><td>${(c.Amount + c.TaxAmount):N2}</td></tr>"))}
        </tbody>
        <tfoot>
            <tr class='total-row'><td colspan='5'>TOTAL CARGOS E IMPUESTOS:</td><td>${(r.Folio.TotalCharges + r.Folio.TotalTaxes):N2}</td></tr>
            <tr class='total-row'><td colspan='5'>TOTAL ABONOS Y PAGOS:</td><td>${r.Folio.TotalPayments:N2}</td></tr>
            <tr class='total-row' style='color: green;'><td colspan='5'>SALDO PENDIENTE (LIQUIDADO):</td><td>$0.00 COP</td></tr>
        </tfoot>
    </table>
    <div class='footer'>
        <p>Comprobante generado electrónicamente por el Sistema de Gestión Hotelera Integral PMS.</p>
        <p>¡Gracias por su estancia en {r.Hotel.Name}!</p>
    </div>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public AuditController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> GetAuditLogs([FromQuery] AuditFilterDto filter)
    {
        var query = _dbContext.AuditLogs
            .Include(a => a.Hotel)
            .Include(a => a.User)
            .AsQueryable();

        if (filter.HotelId.HasValue && filter.HotelId.Value > 0)
        {
            query = query.Where(a => a.HotelId == filter.HotelId.Value);
        }
        else if (_tenantContext.CurrentHotelId.HasValue && !_tenantContext.IsGlobalBypass)
        {
            query = query.Where(a => a.HotelId == _tenantContext.CurrentHotelId.Value);
        }

        if (filter.Level.HasValue) query = query.Where(a => a.Level == filter.Level.Value);
        if (filter.UserId.HasValue) query = query.Where(a => a.UserId == filter.UserId.Value);
        if (!string.IsNullOrWhiteSpace(filter.EntityName)) query = query.Where(a => a.EntityName == filter.EntityName.Trim());
        if (filter.FromDate.HasValue) query = query.Where(a => a.Timestamp >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(a => a.Timestamp <= filter.ToDate.Value);

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(100)
            .Select(a => new AuditLogDto
            {
                AuditLogId = a.AuditLogId,
                HotelId = a.HotelId,
                HotelName = a.Hotel != null ? a.Hotel.Name : "Cadena / Global",
                UserId = a.UserId,
                UserName = a.User != null ? a.User.FullName : "Anónimo / Sistema",
                Level = a.Level,
                Action = a.Action,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                Timestamp = a.Timestamp,
                IpAddress = a.IpAddress,
                CorrelationId = a.CorrelationId
            })
            .ToListAsync();

        return Ok(logs);
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = UserRole.AdminGlobal)]
public class UsersController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public UsersController(IHotelDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserInfoDto>>> GetUsers()
    {
        var users = await _dbContext.Users
            .Include(u => u.HotelUsers)
                .ThenInclude(hu => hu.Hotel)
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new UserInfoDto
            {
                UserId = u.UserId,
                Email = u.Email,
                FullName = u.FullName,
                Role = u.Role,
                Department = u.Department,
                Shift = u.Shift,
                AssignedHotels = u.HotelUsers.Select(hu => new HotelAssignmentDto
                {
                    HotelId = hu.HotelId,
                    Name = hu.Hotel.Name,
                    City = hu.Hotel.City,
                    Code = hu.Hotel.Code,
                    IsDefault = hu.IsDefault
                }).ToList()
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserInfoDto>> CreateUser([FromBody] CreateUserDto dto)
    {
        bool exists = await _dbContext.Users.AnyAsync(u => u.Email == dto.Email.Trim().ToLowerInvariant());
        if (exists) return BadRequest(new { message = "Ya existe un usuario con este correo electrónico." });

        var user = new User
        {
            Email = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = _passwordHasher.Hash(dto.Password),
            FullName = dto.FullName.Trim(),
            Role = dto.Role.Trim(),
            Department = dto.Department?.Trim(),
            Shift = dto.Shift?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var hId in dto.AssignedHotelIds)
        {
            user.HotelUsers.Add(new HotelUser
            {
                HotelId = hId,
                IsDefault = user.HotelUsers.Count == 0
            });
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(new UserInfoDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            Department = user.Department,
            Shift = user.Shift
        });
    }

    // RF-04 / HU-02: Desactivación lógica y revocación de refresh tokens
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        var user = await _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        user.IsActive = false;

        // Revocar todos los refresh tokens activos
        foreach (var rt in user.RefreshTokens.Where(t => t.IsActive))
        {
            rt.RevokedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { message = $"Usuario '{user.FullName}' desactivado correctamente y sus sesiones activas fueron revocadas." });
    }
}
