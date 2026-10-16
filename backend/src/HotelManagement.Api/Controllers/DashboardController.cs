using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HotelManagement.Application.DTOs;
using HotelManagement.Application.Interfaces;
using HotelManagement.Application.Services;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public DashboardController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics([FromQuery] int? hotelId)
    {
        // Si hotelId es null, verificar si el usuario es AdminGlobal o Socio para consolidado
        int? filterHotelId = hotelId ?? _tenantContext.CurrentHotelId;

        // Si se solicita consolidado de cadena
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
}
