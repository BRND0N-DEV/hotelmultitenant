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
public class RoomTypesController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public RoomTypesController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoomTypeDto>>> GetRoomTypes([FromQuery] int? hotelId, [FromQuery] bool? onlyActive)
    {
        int targetHotelId = hotelId ?? _tenantContext.CurrentHotelId ?? 1;

        var query = _dbContext.RoomTypes.Where(rt => rt.HotelId == targetHotelId);
        if (onlyActive == true)
        {
            query = query.Where(rt => rt.IsActive);
        }

        var types = await query
            .OrderBy(rt => rt.Name)
            .Select(rt => new RoomTypeDto
            {
                RoomTypeId = rt.RoomTypeId,
                HotelId = rt.HotelId,
                Name = rt.Name,
                Code = rt.Code,
                BaseCapacity = rt.BaseCapacity,
                MaxCapacity = rt.MaxCapacity,
                BasePrice = rt.BasePrice,
                Description = rt.Description,
                Amenities = rt.Amenities,
                IsActive = rt.IsActive,
                PhysicalRoomCount = rt.Rooms.Count(r => r.IsActive)
            })
            .ToListAsync();

        return Ok(types);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RoomTypeDto>> GetRoomType(int id)
    {
        var rt = await _dbContext.RoomTypes
            .Include(t => t.Rooms)
            .FirstOrDefaultAsync(t => t.RoomTypeId == id);

        if (rt == null) return NotFound(new { message = "Tipo de habitación no encontrado." });

        return Ok(new RoomTypeDto
        {
            RoomTypeId = rt.RoomTypeId,
            HotelId = rt.HotelId,
            Name = rt.Name,
            Code = rt.Code,
            BaseCapacity = rt.BaseCapacity,
            MaxCapacity = rt.MaxCapacity,
            BasePrice = rt.BasePrice,
            Description = rt.Description,
            Amenities = rt.Amenities,
            IsActive = rt.IsActive,
            PhysicalRoomCount = rt.Rooms.Count(r => r.IsActive)
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPost]
    public async Task<ActionResult<RoomTypeDto>> CreateRoomType([FromBody] CreateRoomTypeDto dto, [FromQuery] int? hotelId)
    {
        int targetHotelId = hotelId ?? _tenantContext.CurrentHotelId ?? 1;

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "El nombre de la tipología de habitación es obligatorio." });
        }

        if (string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest(new { message = "El código es obligatorio." });
        }

        if (dto.BaseCapacity <= 0 || dto.MaxCapacity < dto.BaseCapacity)
        {
            return BadRequest(new { message = "La capacidad máxima debe ser mayor o igual a la capacidad base (mínimo 1 huésped)." });
        }

        if (dto.BasePrice <= 0)
        {
            return BadRequest(new { message = "La tarifa base debe ser un valor positivo mayor a $0.00 COP." });
        }

        bool codeExists = await _dbContext.RoomTypes
            .AnyAsync(rt => rt.HotelId == targetHotelId && rt.Code == dto.Code.Trim().ToUpperInvariant());

        if (codeExists)
        {
            return BadRequest(new { message = $"El código de tipo de habitación '{dto.Code}' ya existe en esta sede." });
        }

        var rt = new RoomType
        {
            HotelId = targetHotelId,
            Name = dto.Name.Trim(),
            Code = dto.Code.Trim().ToUpperInvariant(),
            BaseCapacity = dto.BaseCapacity,
            MaxCapacity = dto.MaxCapacity,
            BasePrice = dto.BasePrice,
            Description = dto.Description?.Trim(),
            Amenities = dto.Amenities?.Trim(),
            IsActive = true
        };

        _dbContext.RoomTypes.Add(rt);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoomType), new { id = rt.RoomTypeId }, new RoomTypeDto
        {
            RoomTypeId = rt.RoomTypeId,
            HotelId = rt.HotelId,
            Name = rt.Name,
            Code = rt.Code,
            BaseCapacity = rt.BaseCapacity,
            MaxCapacity = rt.MaxCapacity,
            BasePrice = rt.BasePrice,
            Description = rt.Description,
            Amenities = rt.Amenities,
            IsActive = rt.IsActive,
            PhysicalRoomCount = 0
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPut("{id}")]
    public async Task<ActionResult<RoomTypeDto>> UpdateRoomType(int id, [FromBody] UpdateRoomTypeDto dto)
    {
        var rt = await _dbContext.RoomTypes
            .Include(t => t.Rooms)
            .FirstOrDefaultAsync(t => t.RoomTypeId == id);

        if (rt == null) return NotFound(new { message = "Tipo de habitación no encontrado." });

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "El nombre de la tipología de habitación es obligatorio." });
        }

        if (string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest(new { message = "El código es obligatorio." });
        }

        if (dto.BaseCapacity <= 0 || dto.MaxCapacity < dto.BaseCapacity)
        {
            return BadRequest(new { message = "La capacidad máxima debe ser mayor o igual a la capacidad base (mínimo 1 huésped)." });
        }

        if (dto.BasePrice <= 0)
        {
            return BadRequest(new { message = "La tarifa base debe ser un valor positivo mayor a $0.00 COP." });
        }

        bool codeExists = await _dbContext.RoomTypes
            .AnyAsync(t => t.HotelId == rt.HotelId && t.Code == dto.Code.Trim().ToUpperInvariant() && t.RoomTypeId != id);

        if (codeExists)
        {
            return BadRequest(new { message = $"El código '{dto.Code}' ya está en uso por otra tipología en esta sede." });
        }

        rt.Name = dto.Name.Trim();
        rt.Code = dto.Code.Trim().ToUpperInvariant();
        rt.BaseCapacity = dto.BaseCapacity;
        rt.MaxCapacity = dto.MaxCapacity;
        rt.BasePrice = dto.BasePrice;
        rt.Description = dto.Description?.Trim();
        rt.Amenities = dto.Amenities?.Trim();
        if (dto.IsActive.HasValue) rt.IsActive = dto.IsActive.Value;

        await _dbContext.SaveChangesAsync();

        return Ok(new RoomTypeDto
        {
            RoomTypeId = rt.RoomTypeId,
            HotelId = rt.HotelId,
            Name = rt.Name,
            Code = rt.Code,
            BaseCapacity = rt.BaseCapacity,
            MaxCapacity = rt.MaxCapacity,
            BasePrice = rt.BasePrice,
            Description = rt.Description,
            Amenities = rt.Amenities,
            IsActive = rt.IsActive,
            PhysicalRoomCount = rt.Rooms.Count(r => r.IsActive)
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPatch("{id}/toggle-status")]
    public async Task<IActionResult> ToggleRoomTypeStatus(int id)
    {
        var rt = await _dbContext.RoomTypes.FindAsync(id);
        if (rt == null) return NotFound(new { message = "Tipo de habitación no encontrado." });

        rt.IsActive = !rt.IsActive;
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = $"Tipología '{rt.Name}' ahora está {(rt.IsActive ? "Activa" : "Inactiva")}.",
            isActive = rt.IsActive
        });
    }
}


[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RoomsController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public RoomsController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoomDto>>> GetRooms([FromQuery] int? hotelId, [FromQuery] int? floor, [FromQuery] RoomOperationalStatus? status, [FromQuery] bool includeInactive = false)
    {
        int targetHotelId = hotelId ?? _tenantContext.CurrentHotelId ?? 1;

        var query = _dbContext.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.HotelId == targetHotelId);

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        if (floor.HasValue) query = query.Where(r => r.Floor == floor.Value);
        if (status.HasValue) query = query.Where(r => r.OperationalStatus == status.Value);

        var rooms = await query
            .OrderBy(r => r.Floor)
            .ThenBy(r => r.RoomNumber)
            .Select(r => new RoomDto
            {
                RoomId = r.RoomId,
                HotelId = r.HotelId,
                RoomTypeId = r.RoomTypeId,
                RoomTypeName = r.RoomType.Name,
                RoomNumber = r.RoomNumber,
                Floor = r.Floor,
                OperationalStatus = r.OperationalStatus,
                CleaningStatus = r.CleaningStatus,
                Notes = r.Notes,
                IsActive = r.IsActive,
                RowVersion = Convert.ToBase64String(r.RowVersion)
            })
            .ToListAsync();

        return Ok(rooms);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RoomDto>> GetRoom(int id)
    {
        var r = await _dbContext.Rooms
            .Include(r => r.RoomType)
            .FirstOrDefaultAsync(r => r.RoomId == id);

        if (r == null) return NotFound(new { message = "Habitación no encontrada en la sede activa." });

        return Ok(new RoomDto
        {
            RoomId = r.RoomId,
            HotelId = r.HotelId,
            RoomTypeId = r.RoomTypeId,
            RoomTypeName = r.RoomType.Name,
            RoomNumber = r.RoomNumber,
            Floor = r.Floor,
            OperationalStatus = r.OperationalStatus,
            CleaningStatus = r.CleaningStatus,
            Notes = r.Notes,
            IsActive = r.IsActive,
            RowVersion = Convert.ToBase64String(r.RowVersion)
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPost]
    public async Task<ActionResult<RoomDto>> CreateRoom([FromBody] CreateRoomDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        if (string.IsNullOrWhiteSpace(dto.RoomNumber))
        {
            return BadRequest(new { message = "El número de habitación es obligatorio." });
        }

        // RF-11: Unicidad de número de habitación por hotel
        bool exists = await _dbContext.Rooms
            .AnyAsync(r => r.HotelId == targetHotelId && r.RoomNumber == dto.RoomNumber.Trim());

        if (exists)
        {
            return BadRequest(new { message = $"El número de habitación '{dto.RoomNumber}' ya existe en esta sede. Dos sedes distintas sí pueden tener el mismo número." });
        }

        var roomType = await _dbContext.RoomTypes.FindAsync(dto.RoomTypeId);
        if (roomType == null)
        {
            return BadRequest(new { message = "El tipo de habitación especificado no existe." });
        }

        var room = new Room
        {
            HotelId = targetHotelId,
            RoomTypeId = dto.RoomTypeId,
            RoomNumber = dto.RoomNumber.Trim(),
            Floor = dto.Floor,
            OperationalStatus = RoomOperationalStatus.Disponible,
            CleaningStatus = RoomCleaningStatus.Limpia,
            Notes = dto.Notes?.Trim(),
            IsActive = true
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoom), new { id = room.RoomId }, new RoomDto
        {
            RoomId = room.RoomId,
            HotelId = room.HotelId,
            RoomTypeId = room.RoomTypeId,
            RoomTypeName = roomType.Name,
            RoomNumber = room.RoomNumber,
            Floor = room.Floor,
            OperationalStatus = room.OperationalStatus,
            CleaningStatus = room.CleaningStatus,
            Notes = room.Notes,
            IsActive = room.IsActive
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPut("{id}")]
    public async Task<ActionResult<RoomDto>> UpdateRoom(int id, [FromBody] UpdateRoomDto dto)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.RoomType)
            .FirstOrDefaultAsync(r => r.RoomId == id);

        if (room == null) return NotFound(new { message = "Habitación no encontrada." });

        if (string.IsNullOrWhiteSpace(dto.RoomNumber))
        {
            return BadRequest(new { message = "El número de habitación es obligatorio." });
        }

        bool exists = await _dbContext.Rooms
            .AnyAsync(r => r.HotelId == room.HotelId && r.RoomNumber == dto.RoomNumber.Trim() && r.RoomId != id);

        if (exists)
        {
            return BadRequest(new { message = $"El número de habitación '{dto.RoomNumber}' ya existe en esta sede." });
        }

        var roomType = await _dbContext.RoomTypes.FindAsync(dto.RoomTypeId);
        if (roomType == null)
        {
            return BadRequest(new { message = "El tipo de habitación especificado no existe." });
        }

        room.RoomNumber = dto.RoomNumber.Trim();
        room.Floor = dto.Floor;
        room.RoomTypeId = dto.RoomTypeId;
        if (dto.OperationalStatus.HasValue) room.OperationalStatus = dto.OperationalStatus.Value;
        if (dto.CleaningStatus.HasValue) room.CleaningStatus = dto.CleaningStatus.Value;
        if (dto.Notes != null) room.Notes = dto.Notes.Trim();
        if (dto.IsActive.HasValue) room.IsActive = dto.IsActive.Value;

        await _dbContext.SaveChangesAsync();

        return Ok(new RoomDto
        {
            RoomId = room.RoomId,
            HotelId = room.HotelId,
            RoomTypeId = room.RoomTypeId,
            RoomTypeName = roomType.Name,
            RoomNumber = room.RoomNumber,
            Floor = room.Floor,
            OperationalStatus = room.OperationalStatus,
            CleaningStatus = room.CleaningStatus,
            Notes = room.Notes,
            IsActive = room.IsActive,
            RowVersion = Convert.ToBase64String(room.RowVersion)
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Housekeeping}")]
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateRoomStatus(int id, [FromBody] UpdateRoomStatusDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        var room = await _dbContext.Rooms.FindAsync(id);
        if (room == null || (!_tenantContext.IsGlobalBypass && room.HotelId != targetHotelId))
        {
            return NotFound(new { message = "Habitación no encontrada en la sede activa." });
        }

        if (dto.OperationalStatus.HasValue) room.OperationalStatus = dto.OperationalStatus.Value;
        if (dto.CleaningStatus.HasValue) room.CleaningStatus = dto.CleaningStatus.Value;
        if (dto.Notes != null) room.Notes = dto.Notes;

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPatch("{id}/activate")]
    public async Task<IActionResult> ActivateRoom(int id)
    {
        var room = await _dbContext.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { message = "Habitación no encontrada." });

        room.IsActive = true;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"Habitación '{room.RoomNumber}' reactivada exitosamente.", isActive = true });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeactivateRoom(int id)
    {
        var room = await _dbContext.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { message = "Habitación no encontrada." });

        // RF-12: Impedir desactivar habitaciones con estancias activas o reservas futuras
        int activeFutureReservations = await _dbContext.Reservations
            .CountAsync(r => r.RoomId == id &&
                             (r.Status == ReservationStatus.Pendiente ||
                              r.Status == ReservationStatus.Confirmada ||
                              r.Status == ReservationStatus.CheckIn) &&
                             r.CheckOutDate >= DateTime.UtcNow.Date);

        if (activeFutureReservations > 0)
        {
            return BadRequest(new
            {
                message = $"No es posible desactivar la habitación '{room.RoomNumber}' porque cuenta con {activeFutureReservations} reserva(s) activa(s) o futuras vinculadas.",
                blockingReservationsCount = activeFutureReservations
            });
        }

        room.IsActive = false;
        await _dbContext.SaveChangesAsync();
        return Ok(new { message = $"Habitación '{room.RoomNumber}' desactivada lógicamente.", isActive = false });
    }
}

