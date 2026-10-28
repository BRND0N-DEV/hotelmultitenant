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
public class RatesController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public RatesController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    // --- SEASONS (TEMPORADAS) ---
    [HttpGet("seasons")]
    public async Task<ActionResult<IEnumerable<SeasonDto>>> GetSeasons()
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var seasons = await _dbContext.Seasons
            .Where(s => s.HotelId == targetHotelId)
            .OrderBy(s => s.StartDate)
            .Select(s => new SeasonDto
            {
                SeasonId = s.SeasonId,
                HotelId = s.HotelId,
                Name = s.Name,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                PriceMultiplier = s.PriceMultiplier,
                IsActive = s.IsActive
            })
            .ToListAsync();

        return Ok(seasons);
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPost("seasons")]
    public async Task<ActionResult<SeasonDto>> CreateSeason([FromBody] CreateSeasonDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        if (dto.EndDate < dto.StartDate)
        {
            return BadRequest(new { message = "La fecha de fin no puede ser anterior a la fecha de inicio." });
        }

        // RF-14: Rechazo estricto de temporadas traslapadas en el mismo hotel
        bool hasOverlap = await _dbContext.Seasons
            .AnyAsync(s => s.HotelId == targetHotelId &&
                           s.IsActive &&
                           dto.StartDate <= s.EndDate &&
                           dto.EndDate >= s.StartDate);

        if (hasOverlap)
        {
            return BadRequest(new { message = "Error RF-14: La temporada solicitada se traslapa con otra temporada existente en este hotel." });
        }

        var season = new Season
        {
            HotelId = targetHotelId,
            Name = dto.Name.Trim(),
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            PriceMultiplier = dto.PriceMultiplier,
            IsActive = true
        };

        _dbContext.Seasons.Add(season);
        await _dbContext.SaveChangesAsync();

        return Ok(new SeasonDto
        {
            SeasonId = season.SeasonId,
            HotelId = season.HotelId,
            Name = season.Name,
            StartDate = season.StartDate,
            EndDate = season.EndDate,
            PriceMultiplier = season.PriceMultiplier,
            IsActive = season.IsActive
        });
    }

    // --- PROMOTIONS (PROMOCIONES) ---
    [HttpGet("promotions")]
    public async Task<ActionResult<IEnumerable<PromotionDto>>> GetPromotions()
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var promos = await _dbContext.Promotions
            .Where(p => p.HotelId == targetHotelId)
            .Select(p => new PromotionDto
            {
                PromotionId = p.PromotionId,
                HotelId = p.HotelId,
                Code = p.Code,
                Name = p.Name,
                DiscountPercent = p.DiscountPercent,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsActive = p.IsActive
            })
            .ToListAsync();

        return Ok(promos);
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPost("promotions")]
    public async Task<ActionResult<PromotionDto>> CreatePromotion([FromBody] CreatePromotionDto dto)
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        bool codeExists = await _dbContext.Promotions
            .AnyAsync(p => p.HotelId == targetHotelId && p.Code == dto.Code.Trim().ToUpperInvariant());

        if (codeExists)
        {
            return BadRequest(new { message = $"El código de promoción '{dto.Code}' ya existe en esta sede." });
        }

        var promo = new Promotion
        {
            HotelId = targetHotelId,
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            DiscountPercent = dto.DiscountPercent,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsActive = true
        };

        _dbContext.Promotions.Add(promo);
        await _dbContext.SaveChangesAsync();

        return Ok(new PromotionDto
        {
            PromotionId = promo.PromotionId,
            HotelId = promo.HotelId,
            Code = promo.Code,
            Name = promo.Name,
            DiscountPercent = promo.DiscountPercent,
            StartDate = promo.StartDate,
            EndDate = promo.EndDate,
            IsActive = promo.IsActive
        });
    }

    // --- TAXES & POLICIES ---
    [HttpGet("taxes")]
    public async Task<ActionResult<IEnumerable<HotelTaxDto>>> GetTaxes()
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var taxes = await _dbContext.Taxes
            .Where(t => t.HotelId == targetHotelId)
            .Select(t => new HotelTaxDto
            {
                TaxId = t.TaxId,
                HotelId = t.HotelId,
                Name = t.Name,
                Percentage = t.Percentage,
                IsActive = t.IsActive
            })
            .ToListAsync();

        return Ok(taxes);
    }

    [HttpGet("cancellation-policies")]
    public async Task<ActionResult<IEnumerable<CancellationPolicyDto>>> GetCancellationPolicies()
    {
        int targetHotelId = _tenantContext.CurrentHotelId ?? 1;

        var policies = await _dbContext.CancellationPolicies
            .Where(cp => cp.HotelId == targetHotelId)
            .Select(cp => new CancellationPolicyDto
            {
                PolicyId = cp.PolicyId,
                HotelId = cp.HotelId,
                Name = cp.Name,
                FreeCancellationHours = cp.FreeCancellationHours,
                PenaltyPercent = cp.PenaltyPercent,
                Description = cp.Description,
                IsActive = cp.IsActive
            })
            .ToListAsync();

        return Ok(policies);
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServicesController : ControllerBase
{
    private readonly IHotelDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public ServicesController(IHotelDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdditionalServiceDto>>> GetServices([FromQuery] int? hotelId, [FromQuery] bool? onlyActive)
    {
        int targetHotelId = hotelId ?? _tenantContext.CurrentHotelId ?? 1;

        var query = _dbContext.AdditionalServices
            .Where(s => (_tenantContext.IsGlobalBypass && !hotelId.HasValue) || s.HotelId == targetHotelId)
            .AsQueryable();

        if (onlyActive == true)
        {
            query = query.Where(s => s.IsActive);
        }

        var services = await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .Select(s => new AdditionalServiceDto
            {
                ServiceId = s.ServiceId,
                HotelId = s.HotelId,
                Name = s.Name,
                Category = s.Category,
                Price = s.Price,
                IsActive = s.IsActive
            })
            .ToListAsync();

        return Ok(services);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdditionalServiceDto>> GetService(int id)
    {
        var s = await _dbContext.AdditionalServices.FindAsync(id);
        if (s == null) return NotFound(new { message = "Servicio no encontrado." });

        return Ok(new AdditionalServiceDto
        {
            ServiceId = s.ServiceId,
            HotelId = s.HotelId,
            Name = s.Name,
            Category = s.Category,
            Price = s.Price,
            IsActive = s.IsActive
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPost]
    public async Task<ActionResult<AdditionalServiceDto>> CreateService([FromBody] CreateAdditionalServiceDto dto, [FromQuery] int? hotelId)
    {
        int targetHotelId = hotelId ?? _tenantContext.CurrentHotelId ?? 1;

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "El nombre del servicio es obligatorio." });
        }

        if (string.IsNullOrWhiteSpace(dto.Category))
        {
            return BadRequest(new { message = "La categoría del servicio es obligatoria." });
        }

        if (dto.Price <= 0)
        {
            return BadRequest(new { message = "La tarifa del servicio debe ser un valor positivo mayor a $0.00 COP." });
        }

        var service = new AdditionalService
        {
            HotelId = targetHotelId,
            Name = dto.Name.Trim(),
            Category = dto.Category.Trim(),
            Price = dto.Price,
            IsActive = true
        };

        _dbContext.AdditionalServices.Add(service);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetService), new { id = service.ServiceId }, new AdditionalServiceDto
        {
            ServiceId = service.ServiceId,
            HotelId = service.HotelId,
            Name = service.Name,
            Category = service.Category,
            Price = service.Price,
            IsActive = service.IsActive
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPut("{id}")]
    public async Task<ActionResult<AdditionalServiceDto>> UpdateService(int id, [FromBody] UpdateAdditionalServiceDto dto)
    {
        var s = await _dbContext.AdditionalServices.FindAsync(id);
        if (s == null) return NotFound(new { message = "Servicio no encontrado." });

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "El nombre del servicio es obligatorio." });
        }

        if (string.IsNullOrWhiteSpace(dto.Category))
        {
            return BadRequest(new { message = "La categoría del servicio es obligatoria." });
        }

        if (dto.Price <= 0)
        {
            return BadRequest(new { message = "La tarifa del servicio debe ser un valor positivo mayor a $0.00 COP." });
        }

        s.Name = dto.Name.Trim();
        s.Category = dto.Category.Trim();
        s.Price = dto.Price;
        if (dto.IsActive.HasValue) s.IsActive = dto.IsActive.Value;

        await _dbContext.SaveChangesAsync();

        return Ok(new AdditionalServiceDto
        {
            ServiceId = s.ServiceId,
            HotelId = s.HotelId,
            Name = s.Name,
            Category = s.Category,
            Price = s.Price,
            IsActive = s.IsActive
        });
    }

    [Authorize(Roles = $"{UserRole.AdminGlobal},{UserRole.AdminSede}")]
    [HttpPatch("{id}/toggle-status")]
    public async Task<IActionResult> ToggleServiceStatus(int id)
    {
        var s = await _dbContext.AdditionalServices.FindAsync(id);
        if (s == null) return NotFound(new { message = "Servicio no encontrado." });

        s.IsActive = !s.IsActive;
        await _dbContext.SaveChangesAsync();
        return Ok(new { message = $"Servicio '{s.Name}' ahora está {(s.IsActive ? "Activo" : "Inactivo")}.", isActive = s.IsActive });
    }
}

