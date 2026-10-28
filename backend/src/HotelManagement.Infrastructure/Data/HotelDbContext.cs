using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Infrastructure.Data;

public class HotelDbContext : DbContext, IHotelDbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public HotelDbContext(
        DbContextOptions<HotelDbContext> options,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService) : base(options)
    {
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<HotelUser> HotelUsers => Set<HotelUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<HotelTax> Taxes => Set<HotelTax>();
    public DbSet<CancellationPolicy> CancellationPolicies => Set<CancellationPolicy>();
    public DbSet<AdditionalService> AdditionalServices => Set<AdditionalService>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<DataConsent> DataConsents => Set<DataConsent>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationCompanion> ReservationCompanions => Set<ReservationCompanion>();
    public DbSet<ReservationStateHistory> ReservationStateHistories => Set<ReservationStateHistory>();
    public DbSet<Folio> Folios => Set<Folio>();
    public DbSet<FolioCharge> FolioCharges => Set<FolioCharge>();
    public DbSet<FolioPayment> FolioPayments => Set<FolioPayment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Hotel
        modelBuilder.Entity<Hotel>(b =>
        {
            b.ToTable("Hotels");
            b.HasKey(h => h.HotelId);
            b.HasIndex(h => h.Code).IsUnique();
            b.Property(h => h.Name).HasMaxLength(150).IsRequired();
            b.Property(h => h.City).HasMaxLength(100).IsRequired();
            b.Property(h => h.Address).HasMaxLength(250).IsRequired();
            b.Property(h => h.TimeZone).HasMaxLength(50).HasDefaultValue("America/Bogota");
            b.Property(h => h.Currency).HasMaxLength(10).HasDefaultValue("COP");
            b.Property(h => h.PrimaryColor).HasMaxLength(20).HasDefaultValue("#17365D");
            b.Property(h => h.SecondaryColor).HasMaxLength(20).HasDefaultValue("#D9EAF7");
            b.Property(h => h.LogoUrl).HasMaxLength(500);
        });

        // 2. User
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.UserId);
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Email).HasMaxLength(150).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            b.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            b.Property(u => u.Role).HasMaxLength(50).IsRequired();
            b.Property(u => u.RowVersion).IsRowVersion();
        });

        // 3. HotelUser
        modelBuilder.Entity<HotelUser>(b =>
        {
            b.ToTable("HotelUsers");
            b.HasKey(hu => hu.HotelUserId);
            b.HasIndex(hu => new { hu.UserId, hu.HotelId }).IsUnique();
            b.HasOne(hu => hu.User).WithMany(u => u.HotelUsers).HasForeignKey(hu => hu.UserId);
            b.HasOne(hu => hu.Hotel).WithMany(h => h.HotelUsers).HasForeignKey(hu => hu.HotelId);
        });

        // 4. RefreshToken
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(rt => rt.RefreshTokenId);
            b.HasIndex(rt => rt.Token).IsUnique();
            b.HasOne(rt => rt.User).WithMany(u => u.RefreshTokens).HasForeignKey(rt => rt.UserId);
        });

        // 5. RoomType
        modelBuilder.Entity<RoomType>(b =>
        {
            b.ToTable("RoomTypes");
            b.HasKey(rt => rt.RoomTypeId);
            b.HasIndex(rt => new { rt.HotelId, rt.Code }).IsUnique();
            b.Property(rt => rt.BasePrice).HasColumnType("decimal(18,2)");
            b.HasOne(rt => rt.Hotel).WithMany(h => h.RoomTypes).HasForeignKey(rt => rt.HotelId);
        });

        // 6. Room (Multitenant & Concurrency)
        modelBuilder.Entity<Room>(b =>
        {
            b.ToTable("Rooms");
            b.HasKey(r => r.RoomId);
            b.HasIndex(r => new { r.HotelId, r.RoomNumber }).IsUnique();
            b.Property(r => r.OperationalStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.CleaningStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(r => r.RowVersion).IsRowVersion();
            b.HasOne(r => r.Hotel).WithMany(h => h.Rooms).HasForeignKey(r => r.HotelId);
            b.HasOne(r => r.RoomType).WithMany(rt => rt.Rooms).HasForeignKey(r => r.RoomTypeId);

            // Multitenant Query Filter
            b.HasQueryFilter(r => _tenantContext.IsGlobalBypass || (_tenantContext.CurrentHotelId != null && r.HotelId == _tenantContext.CurrentHotelId.Value));
        });

        // 7. Season
        modelBuilder.Entity<Season>(b =>
        {
            b.ToTable("Seasons");
            b.HasKey(s => s.SeasonId);
            b.Property(s => s.PriceMultiplier).HasColumnType("decimal(5,2)");
            b.HasOne(s => s.Hotel).WithMany(h => h.Seasons).HasForeignKey(s => s.HotelId);
        });

        // 8. Promotion
        modelBuilder.Entity<Promotion>(b =>
        {
            b.ToTable("Promotions");
            b.HasKey(p => p.PromotionId);
            b.HasIndex(p => new { p.HotelId, p.Code }).IsUnique();
            b.Property(p => p.DiscountPercent).HasColumnType("decimal(5,2)");
            b.HasOne(p => p.Hotel).WithMany(h => h.Promotions).HasForeignKey(p => p.HotelId);
        });

        // 9. HotelTax
        modelBuilder.Entity<HotelTax>(b =>
        {
            b.ToTable("HotelTaxes");
            b.HasKey(t => t.TaxId);
            b.Property(t => t.Percentage).HasColumnType("decimal(5,2)");
            b.HasOne(t => t.Hotel).WithMany(h => h.Taxes).HasForeignKey(t => t.HotelId);
        });

        // 10. CancellationPolicy
        modelBuilder.Entity<CancellationPolicy>(b =>
        {
            b.ToTable("CancellationPolicies");
            b.HasKey(cp => cp.PolicyId);
            b.Property(cp => cp.PenaltyPercent).HasColumnType("decimal(5,2)");
            b.HasOne(cp => cp.Hotel).WithMany(h => h.CancellationPolicies).HasForeignKey(cp => cp.HotelId);
        });

        // 11. AdditionalService
        modelBuilder.Entity<AdditionalService>(b =>
        {
            b.ToTable("AdditionalServices");
            b.HasKey(s => s.ServiceId);
            b.Property(s => s.Price).HasColumnType("decimal(18,2)");
            b.HasOne(s => s.Hotel).WithMany(h => h.AdditionalServices).HasForeignKey(s => s.HotelId);

            b.HasQueryFilter(s => _tenantContext.IsGlobalBypass || (_tenantContext.CurrentHotelId != null && s.HotelId == _tenantContext.CurrentHotelId.Value));
        });

        // 12. CashShift
        modelBuilder.Entity<CashShift>(b =>
        {
            b.ToTable("CashShifts");
            b.HasKey(cs => cs.CashShiftId);
            b.Property(cs => cs.InitialAmount).HasColumnType("decimal(18,2)");
            b.Property(cs => cs.TotalCash).HasColumnType("decimal(18,2)");
            b.Property(cs => cs.TotalCard).HasColumnType("decimal(18,2)");
            b.Property(cs => cs.TotalTransfer).HasColumnType("decimal(18,2)");
            b.Property(cs => cs.Status).HasConversion<string>().HasMaxLength(30);
            b.HasOne(cs => cs.Hotel).WithMany(h => h.CashShifts).HasForeignKey(cs => cs.HotelId);
            b.HasOne(cs => cs.User).WithMany().HasForeignKey(cs => cs.UserId);

            b.HasQueryFilter(cs => _tenantContext.IsGlobalBypass || (_tenantContext.CurrentHotelId != null && cs.HotelId == _tenantContext.CurrentHotelId.Value));
        });

        // 13. Guest
        modelBuilder.Entity<Guest>(b =>
        {
            b.ToTable("Guests");
            b.HasKey(g => g.GuestId);
            b.HasIndex(g => new { g.HotelId, g.DocumentType, g.DocumentNumber }).IsUnique();
            b.HasOne(g => g.Hotel).WithMany().HasForeignKey(g => g.HotelId);

            b.HasQueryFilter(g => _tenantContext.IsGlobalBypass || (_tenantContext.CurrentHotelId != null && g.HotelId == _tenantContext.CurrentHotelId.Value));
        });

        // 14. DataConsent
        modelBuilder.Entity<DataConsent>(b =>
        {
            b.ToTable("DataConsents");
            b.HasKey(dc => dc.ConsentId);
            b.HasOne(dc => dc.Guest).WithMany(g => g.DataConsents).HasForeignKey(dc => dc.GuestId);
        });

        // 15. Reservation (Multitenant & Concurrency & Database Trigger)
        modelBuilder.Entity<Reservation>(b =>
        {
            b.ToTable("Reservations", tb => tb.HasTrigger("TR_Reservations_PreventOverbooking"));
            b.HasKey(res => res.ReservationId);
            b.HasIndex(res => res.Code).IsUnique();
            b.Property(res => res.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(res => res.BaseRate).HasColumnType("decimal(18,2)");
            b.Property(res => res.DiscountAmount).HasColumnType("decimal(18,2)");
            b.Property(res => res.TaxAmount).HasColumnType("decimal(18,2)");
            b.Property(res => res.TotalAmount).HasColumnType("decimal(18,2)");
            b.Property(res => res.PenaltyAmount).HasColumnType("decimal(18,2)");
            b.Property(res => res.RowVersion).IsRowVersion();

            b.HasOne(res => res.Hotel).WithMany(h => h.Reservations).HasForeignKey(res => res.HotelId);
            b.HasOne(res => res.Room).WithMany(r => r.Reservations).HasForeignKey(res => res.RoomId);
            b.HasOne(res => res.Guest).WithMany(g => g.Reservations).HasForeignKey(res => res.GuestId);
            b.HasOne(res => res.CancellationPolicy).WithMany().HasForeignKey(res => res.CancellationPolicyId);

            // Multitenant Query Filter
            b.HasQueryFilter(res => _tenantContext.IsGlobalBypass || (_tenantContext.CurrentHotelId != null && res.HotelId == _tenantContext.CurrentHotelId.Value));
        });

        // 16. ReservationCompanion
        modelBuilder.Entity<ReservationCompanion>(b =>
        {
            b.ToTable("ReservationCompanions");
            b.HasKey(c => c.CompanionId);
            b.HasOne(c => c.Reservation).WithMany(r => r.Companions).HasForeignKey(c => c.ReservationId);
        });

        // 17. ReservationStateHistory
        modelBuilder.Entity<ReservationStateHistory>(b =>
        {
            b.ToTable("ReservationStateHistories");
            b.HasKey(h => h.HistoryId);
            b.HasOne(h => h.Reservation).WithMany(r => r.StateHistories).HasForeignKey(h => h.ReservationId);
            b.HasOne(h => h.ChangedByUser).WithMany().HasForeignKey(h => h.ChangedByUserId);
        });

        // 18. Folio
        modelBuilder.Entity<Folio>(b =>
        {
            b.ToTable("Folios");
            b.HasKey(f => f.FolioId);
            b.HasIndex(f => f.ReservationId).IsUnique();
            b.Property(f => f.TotalCharges).HasColumnType("decimal(18,2)");
            b.Property(f => f.TotalTaxes).HasColumnType("decimal(18,2)");
            b.Property(f => f.TotalPayments).HasColumnType("decimal(18,2)");
            b.Property(f => f.Balance).HasColumnType("decimal(18,2)");
            b.Property(f => f.Status).HasConversion<string>().HasMaxLength(30);

            b.HasOne(f => f.Reservation).WithOne(r => r.Folio).HasForeignKey<Folio>(f => f.ReservationId);
            b.HasOne(f => f.Hotel).WithMany().HasForeignKey(f => f.HotelId);

            b.HasQueryFilter(f => _tenantContext.IsGlobalBypass || (_tenantContext.CurrentHotelId != null && f.HotelId == _tenantContext.CurrentHotelId.Value));
        });

        // 19. FolioCharge
        modelBuilder.Entity<FolioCharge>(b =>
        {
            b.ToTable("FolioCharges");
            b.HasKey(c => c.ChargeId);
            b.Property(c => c.UnitPrice).HasColumnType("decimal(18,2)");
            b.Property(c => c.Amount).HasColumnType("decimal(18,2)");
            b.Property(c => c.TaxAmount).HasColumnType("decimal(18,2)");

            b.HasOne(c => c.Folio).WithMany(f => f.Charges).HasForeignKey(c => c.FolioId);
            b.HasOne(c => c.Service).WithMany().HasForeignKey(c => c.ServiceId);
            b.HasOne(c => c.CreatedByUser).WithMany().HasForeignKey(c => c.CreatedByUserId);
        });

        // 20. FolioPayment
        modelBuilder.Entity<FolioPayment>(b =>
        {
            b.ToTable("FolioPayments");
            b.HasKey(p => p.PaymentId);
            b.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            b.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(50);

            b.HasOne(p => p.Folio).WithMany(f => f.Payments).HasForeignKey(p => p.FolioId);
            b.HasOne(p => p.CashShift).WithMany(cs => cs.Payments).HasForeignKey(p => p.CashShiftId);
            b.HasOne(p => p.CreatedByUser).WithMany().HasForeignKey(p => p.CreatedByUserId);
        });

        // 21. AuditLog
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(a => a.AuditLogId);
            b.Property(a => a.Level).HasConversion<string>().HasMaxLength(20);
            b.HasOne(a => a.Hotel).WithMany().HasForeignKey(a => a.HotelId);
            b.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId);
        });

        // 22. IdempotencyKey
        modelBuilder.Entity<IdempotencyKey>(b =>
        {
            b.ToTable("IdempotencyKeys");
            b.HasKey(k => k.IdempotencyKeyId);
            b.HasIndex(k => k.Key).IsUnique();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Interceptor de auditoría técnica (RF-55)
        var entries = ChangeTracker.Entries();
        var auditEntries = new System.Collections.Generic.List<AuditLog>();

        foreach (var entry in entries)
        {
            if (entry.Entity is AuditLog || entry.Entity is RefreshToken || entry.Entity is IdempotencyKey)
                continue;

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                var entityName = entry.Entity.GetType().Name;
                var action = entry.State.ToString();

                string? oldValues = null;
                string? newValues = null;

                var sensitiveKeys = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "PasswordHash", "Password", "Token", "RefreshToken", "SecurityStamp"
                };

                if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    var original = entry.OriginalValues.Properties.ToDictionary(
                        p => p.Name,
                        p => sensitiveKeys.Contains(p.Name) ? (object)"[REDACTED]" : entry.OriginalValues[p]);
                    oldValues = JsonSerializer.Serialize(original);
                }

                if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                {
                    var current = entry.CurrentValues.Properties.ToDictionary(
                        p => p.Name,
                        p => sensitiveKeys.Contains(p.Name) ? (object)"[REDACTED]" : entry.CurrentValues[p]);
                    newValues = JsonSerializer.Serialize(current);
                }

                auditEntries.Add(new AuditLog
                {
                    HotelId = _tenantContext.CurrentHotelId,
                    UserId = _currentUserService.UserId,
                    Level = AuditLevel.Tecnico,
                    Action = $"{action} {entityName}",
                    EntityName = entityName,
                    OldValues = oldValues,
                    NewValues = newValues,
                    Timestamp = DateTime.UtcNow,
                    IpAddress = _currentUserService.IpAddress ?? "127.0.0.1"
                });
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        if (auditEntries.Count > 0)
        {
            AuditLogs.AddRange(auditEntries);
            await base.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
