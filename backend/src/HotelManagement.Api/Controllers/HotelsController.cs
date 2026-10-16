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
public class HotelsController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;

    public HotelsController(IHotelDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<HotelDto>>> GetHotels()
    {
        var hotels = await _dbContext.Hotels
            .Select(h => new HotelDto
            {
                HotelId = h.HotelId,
                Code = h.Code,
                Name = h.Name,
                City = h.City,
                Address = h.Address,
                TimeZone = h.TimeZone,
                Currency = h.Currency,
                PrimaryColor = h.PrimaryColor,
                SecondaryColor = h.SecondaryColor,
                LogoUrl = h.LogoUrl,
                IsActive = h.IsActive,
                RoomCount = h.Rooms.Count(r => r.IsActive),
                ActiveReservationCount = h.Reservations.Count(r => r.Status == ReservationStatus.Confirmada || r.Status == ReservationStatus.CheckIn)
            })
            .ToListAsync();

        return Ok(hotels);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<HotelDto>> GetHotel(int id)
    {
        var h = await _dbContext.Hotels
            .Include(h => h.Rooms)
            .Include(h => h.Reservations)
            .FirstOrDefaultAsync(h => h.HotelId == id);

        if (h == null) return NotFound(new { message = "Hotel no encontrado." });

        return Ok(new HotelDto
        {
            HotelId = h.HotelId,
            Code = h.Code,
            Name = h.Name,
            City = h.City,
            Address = h.Address,
            TimeZone = h.TimeZone,
            Currency = h.Currency,
            PrimaryColor = h.PrimaryColor,
            SecondaryColor = h.SecondaryColor,
            LogoUrl = h.LogoUrl,
            IsActive = h.IsActive,
            RoomCount = h.Rooms.Count(r => r.IsActive),
            ActiveReservationCount = h.Reservations.Count(r => r.Status == ReservationStatus.Confirmada || r.Status == ReservationStatus.CheckIn)
        });
    }

    [Authorize(Roles = UserRole.AdminGlobal)]
    [HttpPost]
    public async Task<ActionResult<HotelDto>> CreateHotel([FromBody] CreateHotelDto dto)
    {
        bool codeExists = await _dbContext.Hotels.AnyAsync(h => h.Code == dto.Code.Trim().ToUpperInvariant());
        if (codeExists)
        {
            return BadRequest(new { message = $"El código de hotel '{dto.Code}' ya se encuentra registrado." });
        }

        var hotel = new Hotel
        {
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            City = dto.City.Trim(),
            Address = dto.Address.Trim(),
            TimeZone = dto.TimeZone,
            Currency = dto.Currency,
            PrimaryColor = dto.PrimaryColor,
            SecondaryColor = dto.SecondaryColor,
            LogoUrl = dto.LogoUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Hotels.Add(hotel);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetHotel), new { id = hotel.HotelId }, new HotelDto
        {
            HotelId = hotel.HotelId,
            Code = hotel.Code,
            Name = hotel.Name,
            City = hotel.City,
            Address = hotel.Address,
            TimeZone = hotel.TimeZone,
            Currency = hotel.Currency,
            PrimaryColor = hotel.PrimaryColor,
            SecondaryColor = hotel.SecondaryColor,
            LogoUrl = hotel.LogoUrl,
            IsActive = hotel.IsActive
        });
    }

    [Authorize(Roles = UserRole.AdminGlobal)]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateHotel(int id, [FromBody] UpdateHotelDto dto)
    {
        var hotel = await _dbContext.Hotels.FindAsync(id);
        if (hotel == null) return NotFound(new { message = "Hotel no encontrado." });

        // Si se intenta desactivar el hotel, validar reservas futuras activas (RF-09)
        if (hotel.IsActive && !dto.IsActive)
        {
            int blockingReservations = await _dbContext.Reservations
                .CountAsync(r => r.HotelId == id &&
                                 (r.Status == ReservationStatus.Pendiente ||
                                  r.Status == ReservationStatus.Confirmada ||
                                  r.Status == ReservationStatus.CheckIn) &&
                                 r.CheckOutDate >= DateTime.UtcNow.Date);

            if (blockingReservations > 0)
            {
                return BadRequest(new
                {
                    message = $"No es posible desactivar el hotel '{hotel.Name}' porque cuenta con {blockingReservations} reserva(s) activa(s) o futuras.",
                    blockingReservationsCount = blockingReservations
                });
            }
        }

        hotel.Name = dto.Name.Trim();
        hotel.City = dto.City.Trim();
        hotel.Address = dto.Address.Trim();
        hotel.TimeZone = dto.TimeZone;
        hotel.Currency = dto.Currency;
        hotel.PrimaryColor = dto.PrimaryColor;
        hotel.SecondaryColor = dto.SecondaryColor;
        hotel.LogoUrl = dto.LogoUrl;
        hotel.IsActive = dto.IsActive;

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }
}
