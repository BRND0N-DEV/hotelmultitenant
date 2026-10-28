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
public class HousekeepingController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public HousekeepingController(
        IHotelDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Housekeeping}")]
    [HttpGet("rooms")]
    public async Task<ActionResult<IEnumerable<RoomDto>>> GetHousekeepingRooms([FromQuery] int? floor, [FromQuery] RoomCleaningStatus? cleaningStatus)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var query = _dbContext.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.HotelId == targetHotelId && r.IsActive)
            .AsQueryable();

        if (floor.HasValue) query = query.Where(r => r.Floor == floor.Value);
        if (cleaningStatus.HasValue) query = query.Where(r => r.CleaningStatus == cleaningStatus.Value);

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

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede},{UserRole.Recepcionista},{UserRole.Housekeeping}")]
    [HttpPut("rooms/{roomId}/status")]
    public async Task<IActionResult> UpdateCleaningStatus(int roomId, [FromBody] UpdateRoomStatusDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;
        var room = await _dbContext.Rooms.FindAsync(roomId);
        if (room == null || (!_tenantContext.IsGlobalBypass && room.HotelId != targetHotelId))
        {
            return NotFound(new { message = "Habitación no encontrada en la sede activa." });
        }

        var oldOperational = room.OperationalStatus;
        var oldCleaning = room.CleaningStatus;

        if (dto.CleaningStatus.HasValue)
        {
            room.CleaningStatus = dto.CleaningStatus.Value;

            // Si se marca limpia y estaba fuera de servicio por aseo, habilitar como disponible
            if (dto.CleaningStatus.Value == RoomCleaningStatus.Limpia && room.OperationalStatus == RoomOperationalStatus.FueraDeServicio)
            {
                room.OperationalStatus = RoomOperationalStatus.Disponible;
            }
        }

        if (dto.OperationalStatus.HasValue)
        {
            // Regla de negocio: Si se retira de mantenimiento a disponible, verificar si está limpia
            // Si la habitación sigue sucia o en limpieza, no puede quedar inmediatamente disponible para huéspedes
            if (dto.OperationalStatus.Value == RoomOperationalStatus.Disponible && room.CleaningStatus != RoomCleaningStatus.Limpia)
            {
                room.OperationalStatus = RoomOperationalStatus.FueraDeServicio;
            }
            else
            {
                room.OperationalStatus = dto.OperationalStatus.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            room.Notes = dto.Notes;
        }

        // Registrar auditoría de trazabilidad operativa
        _dbContext.AuditLogs.Add(new AuditLog
        {
            HotelId = room.HotelId,
            UserId = _currentUserService.UserId,
            Level = AuditLevel.Negocio,
            Action = $"Cambio de estado habitación {room.RoomNumber}",
            EntityName = nameof(Room),
            EntityId = room.RoomId.ToString(),
            OldValues = System.Text.Json.JsonSerializer.Serialize(new { OperationalStatus = oldOperational, CleaningStatus = oldCleaning }),
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { room.OperationalStatus, room.CleaningStatus }),
            Timestamp = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
        return Ok(new
        {
            message = $"Estado de habitación '{room.RoomNumber}' actualizado (Operativo: '{room.OperationalStatus}', Limpieza: '{room.CleaningStatus}').",
            cleaningStatus = room.CleaningStatus,
            operationalStatus = room.OperationalStatus
        });
    }
}
