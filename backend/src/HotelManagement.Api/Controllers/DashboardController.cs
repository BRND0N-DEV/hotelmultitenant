using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
[Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.Socio},{UserRole.Auditor},{UserRole.AdminSede}")]
public class DashboardController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public DashboardController(IHotelDbContext dbContext, ITenantContext tenantContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics([FromQuery] int? hotelId)
    {
        // Si hotelId es null o 0, verificar si el usuario tiene permiso para consolidado
        int? filterHotelId = hotelId ?? _tenantContext.CurrentHotelId;

        // Matriz de Permisos CONTINENTALIA SAS:
        // AdminSede tiene 'Sin acceso' a Dashboard consolidado de cadena. Solo puede consultar su sede.
        if (_currentUserService.Role == UserRole.AdminSede)
        {
            if (!filterHotelId.HasValue || filterHotelId.Value == 0)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Acceso restringido: El rol AdminSede no tiene autorización para consultar el dashboard consolidado de toda la cadena hotelera. Debe especificar su sede asignada." });
            }

            bool isAssigned = await _dbContext.HotelUsers
                .AnyAsync(hu => hu.UserId == _currentUserService.UserId && hu.HotelId == filterHotelId.Value);

            if (!isAssigned)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Acceso restringido: No tiene autorización para consultar los indicadores de otra sede." });
            }
        }
        else if (!filterHotelId.HasValue || filterHotelId.Value == 0)
        {
            _tenantContext.IsGlobalBypass = true;
        }

        var today = DateTime.UtcNow.Date;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        var roomsQuery = _dbContext.Rooms.Where(r => r.IsActive).AsQueryable();
        var reservationsQuery = _dbContext.Reservations.AsQueryable();
        var paymentsQuery = _dbContext.FolioPayments.AsQueryable();

        if (filterHotelId.HasValue && filterHotelId.Value > 0)
        {
            roomsQuery = roomsQuery.Where(r => r.HotelId == filterHotelId.Value);
            reservationsQuery = reservationsQuery.Where(r => r.HotelId == filterHotelId.Value);
            paymentsQuery = paymentsQuery.Where(p => p.Folio.HotelId == filterHotelId.Value);
        }

        int totalRooms = await roomsQuery.CountAsync();
        int maintenanceRooms = await roomsQuery.CountAsync(r => r.OperationalStatus == RoomOperationalStatus.Mantenimiento);

        // Habitaciones ocupadas hoy por reservas activas
        var occupiedRoomIds = await reservationsQuery
            .Where(r => (r.Status == ReservationStatus.CheckIn || r.Status == ReservationStatus.Confirmada) &&
                        r.CheckInDate <= today &&
                        r.CheckOutDate > today)
            .Select(r => r.RoomId)
            .Distinct()
            .ToListAsync();

        int occupiedCount = occupiedRoomIds.Count;
        int availableCount = Math.Max(0, totalRooms - occupiedCount - maintenanceRooms);

        // Ingresos cobrados
        decimal todayRevenue = await paymentsQuery
            .Where(p => p.CreatedAt >= today)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        decimal weeklyRevenue = await paymentsQuery
            .Where(p => p.CreatedAt >= startOfWeek)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        decimal monthlyRevenue = await paymentsQuery
            .Where(p => p.CreatedAt >= startOfMonth)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        // Cancelaciones totales del mes
        int cancellations = await reservationsQuery
            .CountAsync(r => r.Status == ReservationStatus.Anulada && r.CreatedAt >= startOfMonth);

        int activeReservations = await reservationsQuery
            .CountAsync(r => r.Status == ReservationStatus.Confirmada || r.Status == ReservationStatus.CheckIn);

        // KPIs: Ocupación, ADR, RevPAR (RF-50)
        decimal occupancyPercent = HotelKpiCalculator.CalculateOccupancy(occupiedCount, totalRooms);
        decimal adr = HotelKpiCalculator.CalculateADR(todayRevenue, occupiedCount);
        decimal revPar = HotelKpiCalculator.CalculateRevPAR(todayRevenue, totalRooms);

        // Historial últimos 7 días
        var history = new List<DailyRevenueDto>();
        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var nextDay = day.AddDays(1);

            decimal dayRev = await paymentsQuery
                .Where(p => p.CreatedAt >= day && p.CreatedAt < nextDay)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            int checkIns = await reservationsQuery
                .CountAsync(r => r.CheckInDate == day && r.Status == ReservationStatus.CheckIn);

            int checkOuts = await reservationsQuery
                .CountAsync(r => r.CheckOutDate == day && (r.Status == ReservationStatus.CheckOut || r.Status == ReservationStatus.Liquidada));

            history.Add(new DailyRevenueDto
            {
                Date = day.ToString("yyyy-MM-dd"),
                Revenue = dayRev,
                CheckIns = checkIns,
                CheckOuts = checkOuts
            });
        }

        // Comparativa por hoteles si es vista consolidada (RF-49)
        var comparisons = new List<HotelComparisonDto>();
        var allHotels = await _dbContext.Hotels.Where(h => h.IsActive).ToListAsync();

        foreach (var h in allHotels)
        {
            int hTotalRooms = await _dbContext.Rooms.CountAsync(r => r.HotelId == h.HotelId && r.IsActive);
            int hOccupied = await _dbContext.Reservations
                .Where(r => r.HotelId == h.HotelId &&
                            (r.Status == ReservationStatus.CheckIn || r.Status == ReservationStatus.Confirmada) &&
                            r.CheckInDate <= today &&
                            r.CheckOutDate > today)
                .Select(r => r.RoomId)
                .Distinct()
                .CountAsync();

            decimal hMonthRev = await _dbContext.FolioPayments
                .Where(p => p.Folio.HotelId == h.HotelId && p.CreatedAt >= startOfMonth)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            comparisons.Add(new HotelComparisonDto
            {
                HotelId = h.HotelId,
                HotelName = h.Name,
                City = h.City,
                MonthlyRevenue = hMonthRev,
                TotalRooms = hTotalRooms,
                OccupancyPercent = HotelKpiCalculator.CalculateOccupancy(hOccupied, hTotalRooms)
            });
        }

        string scopeName = filterHotelId.HasValue && filterHotelId.Value > 0
            ? allHotels.FirstOrDefault(h => h.HotelId == filterHotelId.Value)?.Name ?? "Sede"
            : "Cadena Consolidada (4 Sedes)";

        return Ok(new DashboardMetricsDto
        {
            FilteredHotelId = filterHotelId,
            ScopeName = scopeName,
            TodayRevenue = todayRevenue,
            WeeklyRevenue = weeklyRevenue,
            MonthlyRevenue = monthlyRevenue,
            OccupancyRatePercent = occupancyPercent,
            ADR = adr,
            RevPAR = revPar,
            TotalRooms = totalRooms,
            AvailableRooms = availableCount,
            OccupiedRooms = occupiedCount,
            MaintenanceRooms = maintenanceRooms,
            ActiveReservationsCount = activeReservations,
            TotalCancellationsCount = cancellations,
            RevenueHistory = history,
            HotelComparisons = comparisons
        });
    }

    // RF-50, RF-51 / Requerimiento 3 y 4: Exportación de Tendencia de Ingresos & Movimientos (7 Días) y Métricas
    [HttpGet("export-revenue-trend")]
    public async Task<IActionResult> ExportRevenueTrend([FromQuery] int? hotelId)
    {
        int? filterHotelId = hotelId ?? _tenantContext.CurrentHotelId;

        // Validaciones multitenant estrictas (Matriz de permisos de exportación)
        if (_currentUserService.Role == UserRole.AdminSede)
        {
            if (!filterHotelId.HasValue || filterHotelId.Value == 0)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Acceso restringido: El rol AdminSede no tiene autorización para exportar el dashboard consolidado de toda la cadena hotelera. Debe exportar exclusivamente su sede asignada." });
            }

            bool isAssigned = await _dbContext.HotelUsers
                .AnyAsync(hu => hu.UserId == _currentUserService.UserId && hu.HotelId == filterHotelId.Value);

            if (!isAssigned)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Acceso restringido: No tiene autorización para exportar información de una sede distinta a la suya." });
            }
        }
        else if (_currentUserService.Role == UserRole.Recepcionista || _currentUserService.Role == UserRole.Housekeeping)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Acceso denegado: Su rol no tiene autorización para exportar datos gerenciales del dashboard." });
        }

        if (!filterHotelId.HasValue || filterHotelId.Value == 0)
        {
            _tenantContext.IsGlobalBypass = true;
        }

        var today = DateTime.UtcNow.Date;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        var roomsQuery = _dbContext.Rooms.Where(r => r.IsActive).AsQueryable();
        var reservationsQuery = _dbContext.Reservations.AsQueryable();
        var paymentsQuery = _dbContext.FolioPayments.AsQueryable();

        Hotel? currentHotel = null;
        if (filterHotelId.HasValue && filterHotelId.Value > 0)
        {
            currentHotel = await _dbContext.Hotels.FirstOrDefaultAsync(h => h.HotelId == filterHotelId.Value);
            roomsQuery = roomsQuery.Where(r => r.HotelId == filterHotelId.Value);
            reservationsQuery = reservationsQuery.Where(r => r.HotelId == filterHotelId.Value);
            paymentsQuery = paymentsQuery.Where(p => p.Folio.HotelId == filterHotelId.Value);
        }

        string scopeName = currentHotel != null
            ? $"{currentHotel.Name} ({currentHotel.City})"
            : "Cadena Consolidada (Todas las Sedes)";

        // Datos reales de los 7 días
        var history = new List<(string Date, decimal Revenue, int CheckIns, int CheckOuts)>();
        decimal totalRevenue = 0;
        int totalCheckIns = 0;
        int totalCheckOuts = 0;

        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var nextDay = day.AddDays(1);

            decimal dayRev = await paymentsQuery
                .Where(p => p.CreatedAt >= day && p.CreatedAt < nextDay)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            int checkIns = await reservationsQuery
                .CountAsync(r => r.CheckInDate == day && r.Status == ReservationStatus.CheckIn);

            int checkOuts = await reservationsQuery
                .CountAsync(r => r.CheckOutDate == day && (r.Status == ReservationStatus.CheckOut || r.Status == ReservationStatus.Liquidada));

            history.Add((day.ToString("yyyy-MM-dd"), dayRev, checkIns, checkOuts));
            totalRevenue += dayRev;
            totalCheckIns += checkIns;
            totalCheckOuts += checkOuts;
        }

        // Métricas complementarias reales del modelo
        int totalRooms = await roomsQuery.CountAsync();
        var occupiedRoomIds = await reservationsQuery
            .Where(r => (r.Status == ReservationStatus.CheckIn || r.Status == ReservationStatus.Confirmada) &&
                        r.CheckInDate <= today &&
                        r.CheckOutDate > today)
            .Select(r => r.RoomId)
            .Distinct()
            .ToListAsync();
        int occupiedCount = occupiedRoomIds.Count;
        decimal occupancyPercent = HotelKpiCalculator.CalculateOccupancy(occupiedCount, totalRooms);
        decimal todayRevenue = await paymentsQuery.Where(p => p.CreatedAt >= today).SumAsync(p => (decimal?)p.Amount) ?? 0;
        decimal monthlyRevenue = await paymentsQuery.Where(p => p.CreatedAt >= startOfMonth).SumAsync(p => (decimal?)p.Amount) ?? 0;

        var sb = new StringBuilder();
        sb.AppendLine("CONTINENTALIA SAS - INFORME GERENCIAL Y TENDENCIA DE INGRESOS (7 DÍAS)");
        sb.AppendLine($"Sede / Alcance del Informe: {scopeName}");
        sb.AppendLine($"Emitido por: {_currentUserService.FullName} ({_currentUserService.Email}) | Rol: {_currentUserService.Role}");
        sb.AppendLine($"Fecha y Hora de Emisión: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Período Auditado: {today.AddDays(-6):yyyy-MM-dd} al {today:yyyy-MM-dd} (Últimos 7 Días)");
        sb.AppendLine();
        sb.AppendLine("INDICADORES CLAVE DEL DASHBOARD EN TIEMPO REAL:");
        sb.AppendLine($"Tasa de Ocupación: {occupancyPercent:F1}%");
        sb.AppendLine($"Habitaciones Totales Activas: {totalRooms}");
        sb.AppendLine($"Habitaciones Ocupadas: {occupiedCount}");
        sb.AppendLine($"Recaudo del Día Actual: ${todayRevenue:N2} COP");
        sb.AppendLine($"Recaudo del Mes Acumulado: ${monthlyRevenue:N2} COP");
        sb.AppendLine();
        sb.AppendLine("TENDENCIA DE INGRESOS & MOVIMIENTOS (7 DÍAS):");
        sb.AppendLine("Fecha,Sede / Alcance,Check-Ins,Check-Outs,Ingresos Recaudados COP");

        foreach (var item in history)
        {
            sb.AppendLine($"{item.Date},\"{scopeName}\",{item.CheckIns},{item.CheckOuts},{item.Revenue:F2}");
        }

        sb.AppendLine();
        sb.AppendLine($"TOTALES 7 DÍAS,\"{scopeName}\",{totalCheckIns},{totalCheckOuts},{totalRevenue:F2}");

        var preamble = Encoding.UTF8.GetPreamble();
        var dataBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileBytes = preamble.Concat(dataBytes).ToArray();

        string safeScope = currentHotel != null ? currentHotel.Code : "Consolidado";
        string fileName = $"Tendencia_Ingresos_7Dias_{safeScope}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

        return File(fileBytes, "text/csv; charset=utf-8", fileName);
    }
}

