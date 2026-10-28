using System;
using System.Collections.Generic;

namespace HotelManagement.Application.DTOs;

// --- AUTH DTOs ---
public class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int? PreferredHotelId { get; set; }
}

public class LoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserInfoDto User { get; set; } = null!;
}

public class RefreshTokenRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
    public int? ActiveHotelId { get; set; }
}

public class SwitchHotelRequestDto
{
    public int HotelId { get; set; }
}


public class UserInfoDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Shift { get; set; }
    public int? ActiveHotelId { get; set; }
    public string? ActiveHotelName { get; set; }
    public List<HotelAssignmentDto> AssignedHotels { get; set; } = new();
}

public class HotelAssignmentDto
{
    public int HotelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class CreateUserDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Shift { get; set; }
    public List<int> AssignedHotelIds { get; set; } = new();
}

// --- HOTEL DTOs ---
public class HotelDto
{
    public int HotelId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public int RoomCount { get; set; }
    public int ActiveReservationCount { get; set; }
}

public class CreateHotelDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "America/Bogota";
    public string Currency { get; set; } = "COP";
    public string PrimaryColor { get; set; } = "#17365D";
    public string SecondaryColor { get; set; } = "#D9EAF7";
    public string? LogoUrl { get; set; }
}

public class UpdateHotelDto
{
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "America/Bogota";
    public string Currency { get; set; } = "COP";
    public string PrimaryColor { get; set; } = "#17365D";
    public string SecondaryColor { get; set; } = "#D9EAF7";
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
