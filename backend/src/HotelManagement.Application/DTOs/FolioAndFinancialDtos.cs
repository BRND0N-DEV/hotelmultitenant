using System;
using System.Collections.Generic;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Application.DTOs;

// --- FOLIO DTOs ---
public class FolioDto
{
    public int FolioId { get; set; }
    public int ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public string GuestName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public decimal TotalCharges { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal Balance { get; set; }
    public FolioStatus Status { get; set; }
    public DateTime? ClosedAt { get; set; }
    public List<FolioChargeDto> Charges { get; set; } = new();
    public List<FolioPaymentDto> Payments { get; set; } = new();
}

public class FolioChargeDto
{
    public int ChargeId { get; set; }
    public int? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalLine => Amount + TaxAmount;
    public bool IsAnnulled { get; set; }
    public int? AnnulledByChargeId { get; set; }
    public string? AnnulmentReason { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AddChargeDto
{
    public int? ServiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal? TaxPercentage { get; set; }
}

public class AnnulChargeDto
{
    public string Reason { get; set; } = string.Empty;
}

public class FolioPaymentDto
{
    public int PaymentId { get; set; }
    public int? CashShiftId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string ReceiptNumber { get; set; } = string.Empty;
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AddPaymentDto
{
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int? CashShiftId { get; set; }
}

// --- CASH SHIFT DTOs ---
public class CashShiftDto
{
    public int CashShiftId { get; set; }
    public int HotelId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal InitialAmount { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalCard { get; set; }
    public decimal TotalTransfer { get; set; }
    public decimal TotalCollected => TotalCash + TotalCard + TotalTransfer;
    public ShiftStatus Status { get; set; }
    public string? Notes { get; set; }
}

public class OpenShiftDto
{
    public decimal InitialAmount { get; set; }
    public string? Notes { get; set; }
}

public class CloseShiftDto
{
    public string? Notes { get; set; }
}

// --- DASHBOARD & METRICS DTOs ---
public class DashboardMetricsDto
{
    public int? FilteredHotelId { get; set; }
    public string ScopeName { get; set; } = string.Empty;
    public decimal TodayRevenue { get; set; }
    public decimal WeeklyRevenue { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal OccupancyRatePercent { get; set; }
    public decimal ADR { get; set; } // Average Daily Rate
    public decimal RevPAR { get; set; } // Revenue Per Available Room
    public int TotalRooms { get; set; }
    public int AvailableRooms { get; set; }
    public int OccupiedRooms { get; set; }
    public int MaintenanceRooms { get; set; }
    public int ActiveReservationsCount { get; set; }
    public int TotalCancellationsCount { get; set; }
    public List<DailyRevenueDto> RevenueHistory { get; set; } = new();
    public List<HotelComparisonDto> HotelComparisons { get; set; } = new();
}

public class DailyRevenueDto
{
    public string Date { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int CheckIns { get; set; }
    public int CheckOuts { get; set; }
}

public class HotelComparisonDto
{
    public int HotelId { get; set; }
    public string HotelName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal MonthlyRevenue { get; set; }
    public decimal OccupancyPercent { get; set; }
    public int TotalRooms { get; set; }
}

// --- OCCUPANCY CALENDAR DTOs ---
public class OccupancyCalendarDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<DateTime> Days { get; set; } = new();
    public List<RoomCalendarRowDto> Rooms { get; set; } = new();
}

public class RoomCalendarRowDto
{
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public int Floor { get; set; }
    public List<DayOccupancyDto> Days { get; set; } = new();
}

public class DayOccupancyDto
{
    public DateTime Date { get; set; }
    public bool IsOccupied { get; set; }
    public int? ReservationId { get; set; }
    public string? ReservationCode { get; set; }
    public string? GuestName { get; set; }
    public ReservationStatus? Status { get; set; }
    public string StatusText { get; set; } = "Disponible";
    public string ColorHex { get; set; } = "#10B981"; // Verde disponible
}

// --- AUDIT DTOs ---
public class AuditLogDto
{
    public long AuditLogId { get; set; }
    public int? HotelId { get; set; }
    public string? HotelName { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditLevel Level { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
}

public class AuditFilterDto
{
    public int? HotelId { get; set; }
    public int? UserId { get; set; }
    public AuditLevel? Level { get; set; }
    public string? EntityName { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
