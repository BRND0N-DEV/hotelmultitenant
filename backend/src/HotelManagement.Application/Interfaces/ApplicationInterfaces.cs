using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HotelManagement.Domain.Entities;

namespace HotelManagement.Application.Interfaces;

public interface IHotelDbContext
{
    DbSet<Hotel> Hotels { get; }
    DbSet<User> Users { get; }
    DbSet<HotelUser> HotelUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<RoomType> RoomTypes { get; }
    DbSet<Room> Rooms { get; }
    DbSet<Season> Seasons { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<HotelTax> Taxes { get; }
    DbSet<CancellationPolicy> CancellationPolicies { get; }
    DbSet<AdditionalService> AdditionalServices { get; }
    DbSet<CashShift> CashShifts { get; }
    DbSet<Guest> Guests { get; }
    DbSet<DataConsent> DataConsents { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<ReservationCompanion> ReservationCompanions { get; }
    DbSet<ReservationStateHistory> ReservationStateHistories { get; }
    DbSet<Folio> Folios { get; }
    DbSet<FolioCharge> FolioCharges { get; }
    DbSet<FolioPayment> FolioPayments { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITenantContext
{
    int? CurrentHotelId { get; }
    bool IsGlobalBypass { get; set; }
    void SetHotelContext(int hotelId);
    void ClearHotelContext();
}

public interface ICurrentUserService
{
    int? UserId { get; }
    string? Email { get; }
    string? Role { get; }
    string? FullName { get; }
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    string GenerateAccessToken(User user, int? hotelId);
    RefreshToken GenerateRefreshToken(int userId);
}

public interface IQrCodeService
{
    byte[] GenerateQrCodePng(string payload);
}

public interface IEmailNotificationService
{
    Task SendReservationVoucherAsync(Reservation reservation, string toEmail, byte[] qrCode);
}
