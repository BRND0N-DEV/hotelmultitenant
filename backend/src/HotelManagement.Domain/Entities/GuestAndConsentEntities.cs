using System;
using System.Collections.Generic;

namespace HotelManagement.Domain.Entities;

public class Guest
{
    public int GuestId { get; set; }
    public int HotelId { get; set; }
    public string DocumentType { get; set; } = string.Empty; // CC, CE, Pasaporte, TI, NIT
    public string DocumentNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Nationality { get; set; } = "Colombia";
    public bool IsAnonymized { get; set; }
    public DateTime? AnonymizedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Hotel Hotel { get; set; } = null!;
    public ICollection<DataConsent> DataConsents { get; set; } = new List<DataConsent>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public string FullName => $"{FirstName} {LastName}".Trim();

    public void Anonymize()
    {
        IsAnonymized = true;
        AnonymizedAt = DateTime.UtcNow;
        FirstName = "ANONIMO";
        LastName = $"ID_{GuestId}";
        DocumentNumber = $"ANON_{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        Email = $"anonymized_{GuestId}@gdpr-forgotten.local";
        Phone = "+00 000000000";
    }
}

public class DataConsent
{
    public int ConsentId { get; set; }
    public int GuestId { get; set; }
    public bool Accepted { get; set; }
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    public string IpAddress { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = "1.0";

    public Guest Guest { get; set; } = null!;
}
