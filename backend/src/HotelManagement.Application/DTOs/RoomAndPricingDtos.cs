using System;
using System.Collections.Generic;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Application.DTOs;

// --- ROOM TYPES & ROOMS ---
public class RoomTypeDto
{
    public int RoomTypeId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int BaseCapacity { get; set; }
    public int MaxCapacity { get; set; }
    public decimal BasePrice { get; set; }
    public string? Description { get; set; }
    public string? Amenities { get; set; }
    public bool IsActive { get; set; }
    public int PhysicalRoomCount { get; set; }
}

public class CreateRoomTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int BaseCapacity { get; set; } = 1;
    public int MaxCapacity { get; set; } = 2;
    public decimal BasePrice { get; set; }
    public string? Description { get; set; }
    public string? Amenities { get; set; }
}

public class UpdateRoomTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int BaseCapacity { get; set; } = 1;
    public int MaxCapacity { get; set; } = 2;
    public decimal BasePrice { get; set; }
    public string? Description { get; set; }
    public string? Amenities { get; set; }
    public bool? IsActive { get; set; }
}

public class RoomDto
{
    public int RoomId { get; set; }
    public int HotelId { get; set; }
    public int RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public RoomOperationalStatus OperationalStatus { get; set; }
    public RoomCleaningStatus CleaningStatus { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateRoomDto
{
    public int RoomTypeId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public string? Notes { get; set; }
}

public class UpdateRoomDto
{
    public int RoomTypeId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public RoomOperationalStatus? OperationalStatus { get; set; }
    public RoomCleaningStatus? CleaningStatus { get; set; }
    public string? Notes { get; set; }
    public bool? IsActive { get; set; }
}

public class UpdateRoomStatusDto
{
    public RoomOperationalStatus? OperationalStatus { get; set; }
    public RoomCleaningStatus? CleaningStatus { get; set; }
    public string? Notes { get; set; }
}


// --- SEASONS, PROMOTIONS, TAXES & SERVICES ---
public class SeasonDto
{
    public int SeasonId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal PriceMultiplier { get; set; }
    public bool IsActive { get; set; }
}

public class CreateSeasonDto
{
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal PriceMultiplier { get; set; } = 1.00m;
}

public class PromotionDto
{
    public int PromotionId { get; set; }
    public int HotelId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
}

public class CreatePromotionDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class HotelTaxDto
{
    public int TaxId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Percentage { get; set; }
    public bool IsActive { get; set; }
}

public class CancellationPolicyDto
{
    public int PolicyId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FreeCancellationHours { get; set; }
    public decimal PenaltyPercent { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class AdditionalServiceDto
{
    public int ServiceId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

public class CreateAdditionalServiceDto
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class UpdateAdditionalServiceDto
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool? IsActive { get; set; }
}

