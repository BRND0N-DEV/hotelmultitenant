using System;
using System.Collections.Generic;

namespace HotelManagement.Domain.Entities;

public class User
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Shift { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<HotelUser> HotelUsers { get; set; } = new List<HotelUser>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public bool IsLockedOut()
    {
        return LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;
    }

    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= 5)
        {
            LockoutEnd = DateTime.UtcNow.AddMinutes(15);
        }
    }

    public void ResetLoginLockout()
    {
        FailedLoginAttempts = 0;
        LockoutEnd = null;
    }
}

public class HotelUser
{
    public int HotelUserId { get; set; }
    public int UserId { get; set; }
    public int HotelId { get; set; }
    public bool IsDefault { get; set; }

    public User User { get; set; } = null!;
    public Hotel Hotel { get; set; } = null!;
}

public class RefreshToken
{
    public int RefreshTokenId { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }

    public User User { get; set; } = null!;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
