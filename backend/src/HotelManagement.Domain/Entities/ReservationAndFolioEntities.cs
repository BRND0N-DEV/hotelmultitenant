using System;
using System.Collections.Generic;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Domain.Entities;

public class Reservation
{
    public int ReservationId { get; set; }
    public int HotelId { get; set; }
    public int RoomId { get; set; }
    public int GuestId { get; set; }
    public int CancellationPolicyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int AdultCount { get; set; } = 1;
    public int ChildCount { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Pendiente;
    public decimal BaseRate { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PenaltyAmount { get; set; }
    public string? SpecialRequests { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Hotel Hotel { get; set; } = null!;
    public Room Room { get; set; } = null!;
    public Guest Guest { get; set; } = null!;
    public CancellationPolicy CancellationPolicy { get; set; } = null!;
    public Folio? Folio { get; set; }

    public ICollection<ReservationCompanion> Companions { get; set; } = new List<ReservationCompanion>();
    public ICollection<ReservationStateHistory> StateHistories { get; set; } = new List<ReservationStateHistory>();
}

public class ReservationCompanion
{
    public int CompanionId { get; set; }
    public int ReservationId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Relationship { get; set; }

    public Reservation Reservation { get; set; } = null!;
}

public class ReservationStateHistory
{
    public int HistoryId { get; set; }
    public int ReservationId { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }

    public Reservation Reservation { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
}

public class Folio
{
    public int FolioId { get; set; }
    public int ReservationId { get; set; }
    public int HotelId { get; set; }
    public decimal TotalCharges { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal Balance { get; set; }
    public FolioStatus Status { get; set; } = FolioStatus.Abierto;
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Reservation Reservation { get; set; } = null!;
    public Hotel Hotel { get; set; } = null!;
    public ICollection<FolioCharge> Charges { get; set; } = new List<FolioCharge>();
    public ICollection<FolioPayment> Payments { get; set; } = new List<FolioPayment>();

    public void RecalculateTotals()
    {
        decimal activeCharges = 0;
        decimal activeTaxes = 0;
        foreach (var c in Charges)
        {
            if (!c.IsAnnulled)
            {
                activeCharges += c.Amount;
                activeTaxes += c.TaxAmount;
            }
        }

        decimal payments = 0;
        foreach (var p in Payments)
        {
            payments += p.Amount;
        }

        TotalCharges = activeCharges;
        TotalTaxes = activeTaxes;
        TotalPayments = payments;
        Balance = (activeCharges + activeTaxes) - payments;
    }
}

public class FolioCharge
{
    public int ChargeId { get; set; }
    public int FolioId { get; set; }
    public int? ServiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public bool IsAnnulled { get; set; }
    public int? AnnulledByChargeId { get; set; }
    public string? AnnulmentReason { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Folio Folio { get; set; } = null!;
    public AdditionalService? Service { get; set; }
    public User CreatedByUser { get; set; } = null!;
}

public class FolioPayment
{
    public int PaymentId { get; set; }
    public int FolioId { get; set; }
    public int? CashShiftId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Efectivo;
    public string Reference { get; set; } = string.Empty;
    public string ReceiptNumber { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Folio Folio { get; set; } = null!;
    public CashShift? CashShift { get; set; }
    public User CreatedByUser { get; set; } = null!;
}

public class CashShift
{
    public int CashShiftId { get; set; }
    public int HotelId { get; set; }
    public int UserId { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public decimal InitialAmount { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalCard { get; set; }
    public decimal TotalTransfer { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Abierto;
    public string? Notes { get; set; }

    public Hotel Hotel { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<FolioPayment> Payments { get; set; } = new List<FolioPayment>();
}

public class AuditLog
{
    public long AuditLogId { get; set; }
    public int? HotelId { get; set; }
    public int? UserId { get; set; }
    public AuditLevel Level { get; set; } = AuditLevel.Negocio;
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }

    public Hotel? Hotel { get; set; }
    public User? User { get; set; }
}

public class IdempotencyKey
{
    public long IdempotencyKeyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int ResponseCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
