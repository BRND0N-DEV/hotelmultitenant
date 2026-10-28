using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using HotelManagement.Application.DTOs;
using HotelManagement.Application.Services;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;
using HotelManagement.Domain.Exceptions;

namespace HotelManagement.UnitTests;

public class PricingEngineTests
{
    [Fact]
    public void CalculateStayBreakdown_ShouldApplyWeekendSurchargeAndPromotion()
    {
        // Arrange: Tipo de habitación estándar de $200,000 COP
        var roomType = new RoomType
        {
            RoomTypeId = 1,
            HotelId = 1,
            Name = "Estándar Sencilla",
            BasePrice = 200000m
        };

        // Estancia de Jueves a Domingo (3 noches: Jueves, Viernes, Sábado)
        var checkIn = new DateTime(2026, 10, 15);  // Jueves
        var checkOut = new DateTime(2026, 10, 18); // Domingo

        var seasons = new List<Season>
        {
            new() { SeasonId = 1, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31), PriceMultiplier = 1.20m, IsActive = true }
        };

        var promo = new Promotion
        {
            Code = "PROMO10",
            DiscountPercent = 10m,
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2026, 10, 31),
            IsActive = true
        };

        var taxes = new List<HotelTax>
        {
            new() { TaxId = 1, Name = "IVA", Percentage = 19m, IsActive = true }
        };

        // Act
        var result = PricingEngine.CalculateStayBreakdown(roomType, checkIn, checkOut, seasons, promo, taxes);

        // Assert
        result.NightlyBreakdown.Should().HaveCount(3);

        // Noche 1: Jueves -> Base: 200,000 * 1.20 (temporada) * 1.0 (no weekend) = 240,000. Descuento 10% = 24,000.
        var thursday = result.NightlyBreakdown[0];
        thursday.BaseRate.Should().Be(200000m);
        thursday.SeasonMultiplier.Should().Be(1.20m);
        thursday.WeekendMultiplier.Should().Be(1.0m);
        thursday.DiscountAmount.Should().Be(24000m);
        thursday.FinalRate.Should().Be(216000m);

        // Noche 2: Viernes -> Base: 200,000 * 1.20 * 1.15 (weekend) = 276,000. Descuento 10% = 27,600.
        var friday = result.NightlyBreakdown[1];
        friday.WeekendMultiplier.Should().Be(1.15m);
        friday.DiscountAmount.Should().Be(27600m);
        friday.FinalRate.Should().Be(248400m);

        // Validar cálculo de impuestos
        decimal expectedNet = result.BaseAmount - result.DiscountAmount;
        decimal expectedTax = Math.Round(expectedNet * 0.19m, 2);
        result.TaxAmount.Should().Be(expectedTax);
        result.TotalAmount.Should().Be(expectedNet + expectedTax);
    }
}

public class ReservationStateMachineTests
{
    [Theory]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.Confirmada)]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.Anulada)]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Confirmada, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.Confirmada, ReservationStatus.Anulada)]
    [InlineData(ReservationStatus.CheckIn, ReservationStatus.CheckOut)]
    [InlineData(ReservationStatus.CheckIn, ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.CheckOut, ReservationStatus.Liquidada)]
    public void ValidateTransition_WhenValid_ShouldNotThrow(ReservationStatus from, ReservationStatus to)
    {
        var action = () => ReservationStateMachine.ValidateTransition(from, to);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.CheckOut)]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.Confirmada, ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.Anulada, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.Liquidada, ReservationStatus.Pendiente)]
    public void ValidateTransition_WhenInvalid_ShouldThrowInvalidStateTransitionException(ReservationStatus from, ReservationStatus to)
    {
        var action = () => ReservationStateMachine.ValidateTransition(from, to);
        action.Should().Throw<InvalidStateTransitionException>();
    }
}

public class FolioEngineTests
{
    [Fact]
    public void CreateCounterEntry_ShouldNegateAmountAndAuditReason()
    {
        // Arrange
        var originalCharge = new FolioCharge
        {
            ChargeId = 101,
            FolioId = 1,
            Description = "Botella de Vino",
            Quantity = 2,
            UnitPrice = 50000m,
            Amount = 100000m,
            TaxAmount = 19000m,
            IsAnnulled = false
        };

        // Act
        var counter = FolioEngine.CreateCounterEntry(originalCharge, userId: 5, reason: "Error de digitación");

        // Assert
        originalCharge.IsAnnulled.Should().BeTrue();
        originalCharge.AnnulmentReason.Should().Be("Error de digitación");

        counter.Amount.Should().Be(-100000m);
        counter.TaxAmount.Should().Be(-19000m);
        counter.Quantity.Should().Be(-2);
        counter.AnnulledByChargeId.Should().Be(101);
        counter.Description.Should().Contain("[ANULACIÓN / CONTRA-ASIENTO]");
    }
}

public class UserSecurityTests
{
    [Fact]
    public void RecordFailedLogin_After5Attempts_ShouldLockAccount()
    {
        var user = new User { Email = "test@hotel.com" };

        for (int i = 0; i < 4; i++)
        {
            user.RecordFailedLogin();
            user.IsLockedOut().Should().BeFalse();
        }

        user.RecordFailedLogin(); // 5to intento
        user.IsLockedOut().Should().BeTrue();
        user.LockoutEnd.Should().BeAfter(DateTime.UtcNow);

        // Reset
        user.ResetLoginLockout();
        user.IsLockedOut().Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
    }
}

public class KpiCalculatorTests
{
    [Fact]
    public void CalculateKpis_ShouldReturnAccurateMetrics()
    {
        // 8 habitaciones ocupadas de 10 totales
        decimal occupancy = HotelKpiCalculator.CalculateOccupancy(8, 10);
        occupancy.Should().Be(80.00m);

        // $1,600,000 en ingresos con 8 ocupadas -> ADR = $200,000
        decimal adr = HotelKpiCalculator.CalculateADR(1600000m, 8);
        adr.Should().Be(200000m);

        // RevPAR con 10 habitaciones totales -> $160,000
        decimal revPar = HotelKpiCalculator.CalculateRevPAR(1600000m, 10);
        revPar.Should().Be(160000m);
    }
}
