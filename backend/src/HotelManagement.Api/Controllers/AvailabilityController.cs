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
public class AvailabilityController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public AvailabilityController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpPost("search")]
    public async Task<ActionResult<IEnumerable<AvailableRoomResponseDto>>> SearchAvailability([FromBody] AvailabilitySearchRequestDto dto)
    {
        if (dto.CheckOutDate <= dto.CheckInDate)
        {
            return BadRequest(new { message = "La fecha de salida debe ser posterior a la fecha de entrada." });
        }

        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        // 1. Obtener IDs de habitaciones con reservas activas que se traslapen con el rango
        var occupiedRoomIds = await _dbContext.Reservations
            .Where(r => r.HotelId == targetHotelId &&
                        (r.Status == ReservationStatus.Pendiente ||
                         r.Status == ReservationStatus.Confirmada ||
                         r.Status == ReservationStatus.CheckIn) &&
                        r.CheckInDate < dto.CheckOutDate.Date &&
                        r.CheckOutDate > dto.CheckInDate.Date)
            .Select(r => r.RoomId)
            .Distinct()
            .ToListAsync();

        // 2. Consultar habitaciones disponibles
        var query = _dbContext.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.HotelId == targetHotelId &&
                        r.IsActive &&
                        r.OperationalStatus != RoomOperationalStatus.FueraDeServicio &&
                        r.OperationalStatus != RoomOperationalStatus.Mantenimiento &&
                        !occupiedRoomIds.Contains(r.RoomId));

        if (dto.RoomTypeId.HasValue)
        {
            query = query.Where(r => r.RoomTypeId == dto.RoomTypeId.Value);
        }

        int requiredCapacity = dto.Adults + dto.Children;
        query = query.Where(r => r.RoomType.MaxCapacity >= requiredCapacity);

        var candidateRooms = await query.ToListAsync();

        // 3. Cargar temporadas, promociones e impuestos para cotización
        var seasons = await _dbContext.Seasons
            .Where(s => s.HotelId == targetHotelId && s.IsActive)
            .ToListAsync();

        var taxes = await _dbContext.Taxes
            .Where(t => t.HotelId == targetHotelId && t.IsActive)
            .ToListAsync();

        var promotion = string.IsNullOrWhiteSpace(dto.PromotionCode)
            ? null
            : await _dbContext.Promotions.FirstOrDefaultAsync(p => p.HotelId == targetHotelId && p.Code == dto.PromotionCode.Trim().ToUpperInvariant() && p.IsActive);

        var result = new List<AvailableRoomResponseDto>();

        foreach (var room in candidateRooms)
        {
            var pricing = PricingEngine.CalculateStayBreakdown(
                room.RoomType,
                dto.CheckInDate,
                dto.CheckOutDate,
                seasons,
                promotion,
                taxes);

            result.Add(new AvailableRoomResponseDto
            {
                RoomId = room.RoomId,
                RoomNumber = room.RoomNumber,
                Floor = room.Floor,
                RoomTypeId = room.RoomTypeId,
                RoomTypeName = room.RoomType.Name,
                Amenities = room.RoomType.Amenities,
                BaseCapacity = room.RoomType.BaseCapacity,
                MaxCapacity = room.RoomType.MaxCapacity,
                BasePricePerNight = room.RoomType.BasePrice,
                TotalAmount = pricing.TotalAmount,
                TotalTaxes = pricing.TaxAmount,
                TotalDiscounts = pricing.DiscountAmount,
                NightlyBreakdown = pricing.NightlyBreakdown
            });
        }

        return Ok(result.OrderBy(r => r.TotalAmount));
    }

    // RF-20, RF-21 / HU-09: Consulta de disponibilidad inter-sedes protegida
    [HttpPost("cross-hotel")]
    public async Task<ActionResult<IEnumerable<CrossHotelAvailabilityResponseDto>>> SearchCrossHotelAvailability([FromBody] AvailabilitySearchRequestDto dto)
    {
        if (dto.CheckOutDate <= dto.CheckInDate)
        {
            return BadRequest(new { message = "La fecha de salida debe ser posterior a la fecha de entrada." });
        }

        // Se omite el filtro de tenant para esta consulta específica de agregación
        _tenantContext.IsGlobalBypass = true;

        var activeHotels = await _dbContext.Hotels
            .Where(h => h.IsActive)
            .ToListAsync();

        var response = new List<CrossHotelAvailabilityResponseDto>();

        foreach (var hotel in activeHotels)
        {
            var occupiedRoomIds = await _dbContext.Reservations
                .Where(r => r.HotelId == hotel.HotelId &&
                            (r.Status == ReservationStatus.Pendiente ||
                             r.Status == ReservationStatus.Confirmada ||
                             r.Status == ReservationStatus.CheckIn) &&
                            r.CheckInDate < dto.CheckOutDate.Date &&
                            r.CheckOutDate > dto.CheckInDate.Date)
                .Select(r => r.RoomId)
                .Distinct()
                .ToListAsync();

            var roomTypes = await _dbContext.RoomTypes
                .Where(rt => rt.HotelId == hotel.HotelId && rt.IsActive)
                .Include(rt => rt.Rooms)
                .ToListAsync();

            var roomTypeSummaries = new List<CrossHotelRoomTypeSummaryDto>();

            foreach (var rt in roomTypes)
            {
                int availableCount = rt.Rooms.Count(r => r.IsActive &&
                                                         r.OperationalStatus == RoomOperationalStatus.Disponible &&
                                                         !occupiedRoomIds.Contains(r.RoomId));

                if (availableCount > 0)
                {
                    roomTypeSummaries.Add(new CrossHotelRoomTypeSummaryDto
                    {
                        RoomTypeId = rt.RoomTypeId,
                        RoomTypeName = rt.Name,
                        AvailableCount = availableCount,
                        ReferenceRatePerNight = rt.BasePrice,
                        Capacity = rt.MaxCapacity
                    });
                }
            }

            response.Add(new CrossHotelAvailabilityResponseDto
            {
                HotelId = hotel.HotelId,
                HotelName = hotel.Name,
                City = hotel.City,
                Currency = hotel.Currency,
                AvailableRoomTypes = roomTypeSummaries
            });
        }

        return Ok(response);
    }

    // RF-29 / HU-14: Calendario de ocupación de 14 días
    [HttpGet("calendar")]
    public async Task<ActionResult<OccupancyCalendarDto>> GetOccupancyCalendar([FromQuery] DateTime? startDate)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        var start = (startDate ?? DateTime.UtcNow).Date;
        var end = start.AddDays(14);

        var days = new List<DateTime>();
        for (int i = 0; i < 14; i++)
        {
            days.Add(start.AddDays(i));
        }

        var rooms = await _dbContext.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.HotelId == targetHotelId && r.IsActive)
            .OrderBy(r => r.Floor)
            .ThenBy(r => r.RoomNumber)
            .ToListAsync();

        var reservations = await _dbContext.Reservations
            .Include(r => r.Guest)
            .Where(r => r.HotelId == targetHotelId &&
                        (r.Status == ReservationStatus.Pendiente ||
                         r.Status == ReservationStatus.Confirmada ||
                         r.Status == ReservationStatus.CheckIn) &&
                        r.CheckInDate < end &&
                        r.CheckOutDate > start)
            .ToListAsync();

        var calendarRows = new List<RoomCalendarRowDto>();

        foreach (var room in rooms)
        {
            var dayList = new List<DayOccupancyDto>();

            foreach (var day in days)
            {
                var matchingRes = reservations.FirstOrDefault(r => r.RoomId == room.RoomId && day >= r.CheckInDate && day < r.CheckOutDate);

                if (room.OperationalStatus == RoomOperationalStatus.Mantenimiento)
                {
                    dayList.Add(new DayOccupancyDto
                    {
                        Date = day,
                        IsOccupied = true,
                        StatusText = "Mantenimiento",
                        ColorHex = "#F59E0B" // Naranja
                    });
                }
                else if (matchingRes != null)
                {
                    string color = matchingRes.Status switch
                    {
                        ReservationStatus.CheckIn => "#EF4444", // Rojo ocupada
                        ReservationStatus.Confirmada => "#3B82F6", // Azul confirmada
                        ReservationStatus.Pendiente => "#F97316", // Naranja pendiente
                        _ => "#6B7280"
                    };

                    dayList.Add(new DayOccupancyDto
                    {
                        Date = day,
                        IsOccupied = true,
                        ReservationId = matchingRes.ReservationId,
                        ReservationCode = matchingRes.Code,
                        GuestName = matchingRes.Guest.FullName,
                        Status = matchingRes.Status,
                        StatusText = matchingRes.Status.ToString(),
                        ColorHex = color
                    });
                }
                else
                {
                    dayList.Add(new DayOccupancyDto
                    {
                        Date = day,
                        IsOccupied = false,
                        StatusText = "Disponible",
                        ColorHex = "#10B981" // Verde disponible
                    });
                }
            }

            calendarRows.Add(new RoomCalendarRowDto
            {
                RoomId = room.RoomId,
                RoomNumber = room.RoomNumber,
                RoomTypeName = room.RoomType.Name,
                Floor = room.Floor,
                Days = dayList
            });
        }

        return Ok(new OccupancyCalendarDto
        {
            StartDate = start,
            EndDate = end,
            Days = days,
            Rooms = calendarRows
        });
    }
}
