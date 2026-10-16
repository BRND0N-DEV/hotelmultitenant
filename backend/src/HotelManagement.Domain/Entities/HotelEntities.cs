using System;
using System.Collections.Generic;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Domain.Entities;

public class Hotel
{
    public int HotelId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "America/Bogota";
    public string Currency { get; set; } = "COP";
    public string PrimaryColor { get; set; } = "#17365D";
    public string SecondaryColor { get; set; } = "#D9EAF7";
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Relaciones
    public ICollection<HotelUser> HotelUsers { get; set; } = new List<HotelUser>();
    public ICollection<RoomType> RoomTypes { get; set; } = new List<RoomType>();
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
    public ICollection<Season> Seasons { get; set; } = new List<Season>();
    public ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();
    public ICollection<HotelTax> Taxes { get; set; } = new List<HotelTax>();
    public ICollection<CancellationPolicy> CancellationPolicies { get; set; } = new List<CancellationPolicy>();
    public ICollection<AdditionalService> AdditionalServices { get; set; } = new List<AdditionalService>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<CashShift> CashShifts { get; set; } = new List<CashShift>();
}

public class RoomType
{
    public int RoomTypeId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int BaseCapacity { get; set; } = 1;
    public int MaxCapacity { get; set; } = 2;
    public decimal BasePrice { get; set; }
    public string? Description { get; set; }
    public string? Amenities { get; set; }
    public bool IsActive { get; set; } = true;

    public Hotel Hotel { get; set; } = null!;
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}

public class Room
{
    public int RoomId { get; set; }
    public int HotelId { get; set; }
    public int RoomTypeId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public RoomOperationalStatus OperationalStatus { get; set; } = RoomOperationalStatus.Disponible;
    public RoomCleaningStatus CleaningStatus { get; set; } = RoomCleaningStatus.Limpia;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Hotel Hotel { get; set; } = null!;
    public RoomType RoomType { get; set; } = null!;
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}

public class Season
{
    public int SeasonId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal PriceMultiplier { get; set; } = 1.00m;
    public bool IsActive { get; set; } = true;

    public Hotel Hotel { get; set; } = null!;
}

public class Promotion
{
    public int PromotionId { get; set; }
    public int HotelId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    public Hotel Hotel { get; set; } = null!;
}

public class HotelTax
{
    public int TaxId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Percentage { get; set; }
    public bool IsActive { get; set; } = true;

    public Hotel Hotel { get; set; } = null!;
}

public class CancellationPolicy
{
    public int PolicyId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FreeCancellationHours { get; set; } = 48;
    public decimal PenaltyPercent { get; set; } = 0.00m;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Hotel Hotel { get; set; } = null!;
}

public class AdditionalService
{
    public int ServiceId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Desayuno, Spa, Parqueadero, Minibar, Lavandería, Restaurante, Traslado
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    public Hotel Hotel { get; set; } = null!;
}
