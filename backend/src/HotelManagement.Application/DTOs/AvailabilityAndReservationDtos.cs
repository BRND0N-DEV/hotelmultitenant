using System;
using System.Collections.Generic;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Application.DTOs;

// --- AVAILABILITY DTOs ---
public class AvailabilitySearchRequestDto
{
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Adults { get; set; } = 1;
    public int Children { get; set; } = 0;
    public int? RoomTypeId { get; set; }
    public string? PromotionCode { get; set; }
}

public class AvailableRoomResponseDto
{
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public int RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public string? Amenities { get; set; }
    public int BaseCapacity { get; set; }
    public int MaxCapacity { get; set; }
    public decimal BasePricePerNight { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal TotalDiscounts { get; set; }
    public List<NightlyRateBreakdownDto> NightlyBreakdown { get; set; } = new();
}

public class NightlyRateBreakdownDto
{
    public DateTime Date { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public decimal BaseRate { get; set; }
    public decimal SeasonMultiplier { get; set; } = 1.0m;
    public string? SeasonName { get; set; }
    public decimal WeekendMultiplier { get; set; } = 1.0m;
    public decimal DiscountAmount { get; set; }
    public decimal FinalRate { get; set; }
}

// Inter-sedes (RF-20, RF-21: Solo expone sede, tipo de habitación, cantidad disponible y tarifa referencial)
public class CrossHotelAvailabilityResponseDto
{
    public int HotelId { get; set; }
    public string HotelName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Currency { get; set; } = "COP";
    public List<CrossHotelRoomTypeSummaryDto> AvailableRoomTypes { get; set; } = new();
}

public class CrossHotelRoomTypeSummaryDto
{
    public int RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public int AvailableCount { get; set; }
    public decimal ReferenceRatePerNight { get; set; }
    public int Capacity { get; set; }
}

// --- GUEST DTOs ---
public class GuestDto
{
    public int GuestId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public bool IsAnonymized { get; set; }
    public bool HasConsent { get; set; }
}

public class CreateGuestDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Nationality { get; set; } = "Colombia";
    public bool AcceptDataConsent { get; set; }
    public string? IpAddress { get; set; }
}

public class UpdateGuestDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Nationality { get; set; } = "Colombia";
    public bool? AcceptDataConsent { get; set; }
}

// --- RESERVATION DTOs ---
public class CreateReservationDto
{
    public int RoomId { get; set; }
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Adults { get; set; } = 1;
    public int Children { get; set; } = 0;
    public string? PromotionCode { get; set; }
    public string? SpecialRequests { get; set; }
    public int CancellationPolicyId { get; set; }
    public CreateGuestDto PrimaryGuest { get; set; } = null!;
    public List<CompanionDto> Companions { get; set; } = new();
}

public class CompanionDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Relationship { get; set; }
}

public class ReservationDto
{
    public int ReservationId { get; set; }
    public int HotelId { get; set; }
    public string HotelName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public int GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string GuestDocument { get; set; } = string.Empty;
    public string GuestEmail { get; set; } = string.Empty;
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Nights => (int)(CheckOutDate.Date - CheckInDate.Date).TotalDays;
    public int AdultCount { get; set; }
    public int ChildCount { get; set; }
    public ReservationStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal FolioBalance { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ReservationDetailDto : ReservationDto
{
    public decimal BaseRate { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal PenaltyAmount { get; set; }
    public string? SpecialRequests { get; set; }
    public string CancellationPolicyName { get; set; } = string.Empty;
    public int FreeCancellationHours { get; set; }
    public List<CompanionDto> Companions { get; set; } = new();
    public List<ReservationStateHistoryDto> StateHistories { get; set; } = new();
    public FolioDto? Folio { get; set; }
}

public class ReservationStateHistoryDto
{
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string ChangedByUserName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
}

public class ChangeReservationStateDto
{
    public ReservationStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

public class CancelReservationDto
{
    public string Reason { get; set; } = string.Empty;
}
