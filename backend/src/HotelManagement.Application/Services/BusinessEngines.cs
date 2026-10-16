using System;
using System.Collections.Generic;
using System.Linq;
using HotelManagement.Application.DTOs;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;
using HotelManagement.Domain.Exceptions;

namespace HotelManagement.Application.Services;

// 1. MOTOR DE CÁLCULO DE TARIFAS (RF-13, RF-14, RF-15, RF-18)
public static class PricingEngine
{
    public static StayCalculationResult CalculateStayBreakdown(
        RoomType roomType,
        DateTime checkIn,
        DateTime checkOut,
        IEnumerable<Season> seasons,
        Promotion? activePromotion,
        IEnumerable<HotelTax> taxes)
    {
        var breakdown = new List<NightlyRateBreakdownDto>();
        decimal totalBase = 0;
        decimal totalDiscounts = 0;

        var currentDate = checkIn.Date;
        var seasonsList = seasons.Where(s => s.IsActive).ToList();

        while (currentDate < checkOut.Date)
        {
            var dayOfWeek = currentDate.DayOfWeek;
            // Recargo de fin de semana (Viernes y Sábado +15%)
            decimal weekendMultiplier = (dayOfWeek == DayOfWeek.Friday || dayOfWeek == DayOfWeek.Saturday) ? 1.15m : 1.0m;

            // Temporada activa para la fecha
            var matchingSeason = seasonsList.FirstOrDefault(s => currentDate >= s.StartDate.Date && currentDate <= s.EndDate.Date);
            decimal seasonMultiplier = matchingSeason?.PriceMultiplier ?? 1.0m;

            decimal nightlyBase = roomType.BasePrice * seasonMultiplier * weekendMultiplier;

            // Descuento de promoción si aplica
            decimal nightlyDiscount = 0;
            if (activePromotion != null && activePromotion.IsActive &&
                currentDate >= activePromotion.StartDate.Date && currentDate <= activePromotion.EndDate.Date)
            {
                nightlyDiscount = Math.Round(nightlyBase * (activePromotion.DiscountPercent / 100m), 2);
            }

            decimal finalNightlyRate = nightlyBase - nightlyDiscount;

            totalBase += nightlyBase;
            totalDiscounts += nightlyDiscount;

            breakdown.Add(new NightlyRateBreakdownDto
            {
                Date = currentDate,
                DayOfWeek = dayOfWeek.ToString(),
                BaseRate = roomType.BasePrice,
                SeasonMultiplier = seasonMultiplier,
                SeasonName = matchingSeason?.Name,
                WeekendMultiplier = weekendMultiplier,
                DiscountAmount = nightlyDiscount,
                FinalRate = finalNightlyRate
            });

            currentDate = currentDate.AddDays(1);
        }

        // Impuestos
        decimal netAmount = totalBase - totalDiscounts;
        decimal totalTaxPercent = taxes.Where(t => t.IsActive).Sum(t => t.Percentage);
        decimal totalTaxes = Math.Round(netAmount * (totalTaxPercent / 100m), 2);
        decimal grandTotal = netAmount + totalTaxes;

        return new StayCalculationResult
        {
            NightlyBreakdown = breakdown,
            BaseAmount = totalBase,
            DiscountAmount = totalDiscounts,
            TaxAmount = totalTaxes,
            TotalAmount = grandTotal
        };
    }
}

public class StayCalculationResult
{
    public List<NightlyRateBreakdownDto> NightlyBreakdown { get; set; } = new();
    public decimal BaseAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

// 2. MÁQUINA DE ESTADOS DE LA RESERVA (RF-26, RF-27, HU-12)
public static class ReservationStateMachine
{
    private static readonly Dictionary<ReservationStatus, HashSet<ReservationStatus>> AllowedTransitions = new()
    {
        [ReservationStatus.Pendiente] = new() { ReservationStatus.Confirmada, ReservationStatus.Anulada, ReservationStatus.NoShow },
        [ReservationStatus.Confirmada] = new() { ReservationStatus.CheckIn, ReservationStatus.Anulada, ReservationStatus.NoShow },
        [ReservationStatus.CheckIn] = new() { ReservationStatus.CheckOut },
        [ReservationStatus.CheckOut] = new() { ReservationStatus.Liquidada },
        [ReservationStatus.Liquidada] = new(),
        [ReservationStatus.Anulada] = new(),
        [ReservationStatus.NoShow] = new()
    };

    public static bool CanTransition(ReservationStatus current, ReservationStatus target)
    {
        if (AllowedTransitions.TryGetValue(current, out var targets))
        {
            return targets.Contains(target);
        }
        return false;
    }

    public static void ValidateTransition(ReservationStatus current, ReservationStatus target)
    {
        if (!CanTransition(current, target))
        {
            throw new InvalidStateTransitionException(
                current.ToString(),
                target.ToString(),
                $"Transición no permitida: no es posible pasar de estado '{current}' a '{target}'. (HTTP 422)");
        }
    }

    public static IEnumerable<ReservationStatus> GetAvailableTransitions(ReservationStatus current)
    {
        return AllowedTransitions.TryGetValue(current, out var targets) ? targets : Enumerable.Empty<ReservationStatus>();
    }
}

// 3. MOTOR DE FOLIO Y CONTRA-ASIENTOS (RF-35, RF-36, RF-37, RF-40)
public static class FolioEngine
{
    public static FolioCharge CreateCounterEntry(FolioCharge originalCharge, int userId, string reason)
    {
        if (originalCharge.IsAnnulled)
        {
            throw new DomainException("El cargo ya ha sido anulado previamente.");
        }

        originalCharge.IsAnnulled = true;
        originalCharge.AnnulmentReason = reason;

        var counterEntry = new FolioCharge
        {
            FolioId = originalCharge.FolioId,
            ServiceId = originalCharge.ServiceId,
            Description = $"[ANULACIÓN / CONTRA-ASIENTO] {originalCharge.Description}",
            Quantity = -originalCharge.Quantity,
            UnitPrice = originalCharge.UnitPrice,
            Amount = -originalCharge.Amount,
            TaxAmount = -originalCharge.TaxAmount,
            IsAnnulled = false,
            AnnulledByChargeId = originalCharge.ChargeId,
            AnnulmentReason = reason,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        return counterEntry;
    }
}

// 4. CALCULADOR DE MÉTRICAS KPI (RF-49, RF-50)
public static class HotelKpiCalculator
{
    public static decimal CalculateOccupancy(int occupiedRooms, int totalRooms)
    {
        if (totalRooms <= 0) return 0;
        return Math.Round(((decimal)occupiedRooms / totalRooms) * 100m, 2);
    }

    public static decimal CalculateADR(decimal roomRevenue, int occupiedRooms)
    {
        if (occupiedRooms <= 0) return 0;
        return Math.Round(roomRevenue / occupiedRooms, 2);
    }

    public static decimal CalculateRevPAR(decimal roomRevenue, int totalRooms)
    {
        if (totalRooms <= 0) return 0;
        return Math.Round(roomRevenue / totalRooms, 2);
    }
}
