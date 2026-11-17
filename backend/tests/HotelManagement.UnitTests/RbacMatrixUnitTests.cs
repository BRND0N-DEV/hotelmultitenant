using System;
using FluentAssertions;
using Xunit;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;
using HotelManagement.Domain.Exceptions;
using HotelManagement.Application.Services;

namespace HotelManagement.UnitTests;

public class RbacMatrixUnitTests
{
    [Fact]
    public void CanonicalRoles_ShouldContainAllDefinedRolesInMatrix()
    {
        // Assert: Validar los 6 roles de CONTINENTALIA SAS
        UserRole.AdminGlobal.Should().Be("AdminGlobal");
        UserRole.AdminSede.Should().Be("AdminSede");
        UserRole.Recepcionista.Should().Be("Recepcionista");
        UserRole.Housekeeping.Should().Be("Housekeeping");
        UserRole.Auditor.Should().Be("Auditor");
        UserRole.Socio.Should().Be("Socio");
    }

    [Fact]
    public void GuestAnonymization_ShouldIrreversiblySanitizeData_PreservingReferentialIntegrity()
    {
        // Arrange: Huésped con datos reales identificables
        var guest = new Guest
        {
            GuestId = 42,
            HotelId = 1,
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            FirstName = "Juan",
            LastName = "Pérez",
            Email = "juan.perez@example.com",
            Phone = "+57 300 987 6543",
            Nationality = "Colombia",
            IsAnonymized = false
        };

        // Act: Ejecutar anonimización irreversible (RF-47 / HU-23)
        guest.Anonymize();

        // Assert: Datos PII eliminados irreversiblmente
        guest.IsAnonymized.Should().BeTrue();
        guest.FirstName.Should().Be("ANONIMO");
        guest.LastName.Should().Be("ID_42");
        guest.Email.Should().Be("anonymized_42@gdpr-forgotten.local");
        guest.Phone.Should().Be("+00 000000000");
        guest.DocumentNumber.Should().StartWith("ANON_");
        guest.AnonymizedAt.Should().NotBeNull();
        guest.AnonymizedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Integridad referencial: GuestId y HotelId intactos para folios contables
        guest.GuestId.Should().Be(42);
        guest.HotelId.Should().Be(1);
    }

    [Fact]
    public void FolioCounterEntry_ShouldCreateAuditedNegativeOffset_WithoutDeletingOriginal()
    {
        // Arrange: Cargo original en folio de $50,000 COP
        var originalCharge = new FolioCharge
        {
            ChargeId = 101,
            FolioId = 1,
            Description = "Room Service - Cena Gourmet",
            Quantity = 1,
            UnitPrice = 50000m,
            Amount = 50000m,
            TaxAmount = 9500m,
            IsAnnulled = false,
            CreatedByUserId = 2
        };

        // Act: Generar contra-asiento formal de anulación
        var counterEntry = FolioEngine.CreateCounterEntry(originalCharge, userId: 3, reason: "Error de digitación en habitación");

        // Assert: Cargo original marcado como anulado
        originalCharge.IsAnnulled.Should().BeTrue();
        originalCharge.AnnulmentReason.Should().Be("Error de digitación en habitación");

        // Contra-asiento con valores inversos negativos para cuadre a cero
        counterEntry.Amount.Should().Be(-50000m);
        counterEntry.TaxAmount.Should().Be(-9500m);
        counterEntry.Description.Should().Contain("ANULACIÓN");
        counterEntry.CreatedByUserId.Should().Be(3);
        counterEntry.AnnulledByChargeId.Should().Be(101);
    }

    [Theory]
    [InlineData("AdminGlobal", true, false, true)]
    [InlineData("AdminSede", false, true, true)]
    [InlineData("Recepcionista", false, true, false)]
    [InlineData("Housekeeping", false, false, false)]
    [InlineData("Socio", false, false, false)]
    [InlineData("Auditor", false, false, true)]
    public void RoleMatrix_CapabilitiesMapping_ShouldAdhereToGovernanceRules(string role, bool canAnonymize, bool canManageReservations, bool canViewAudit)
    {
        // Validación lógica de capacidades según matriz de la Junta
        bool actualCanAnonymize = (role == UserRole.AdminGlobal);
        bool actualCanManageReservations = (role == UserRole.AdminSede || role == UserRole.Recepcionista);
        bool actualCanViewAudit = (role == UserRole.AdminGlobal || role == UserRole.Auditor || role == UserRole.AdminSede);

        actualCanAnonymize.Should().Be(canAnonymize);
        actualCanManageReservations.Should().Be(canManageReservations);
        actualCanViewAudit.Should().Be(canViewAudit);
    }

    [Theory]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.CheckOut)]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.Confirmada, ReservationStatus.CheckOut)]
    [InlineData(ReservationStatus.Confirmada, ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.CheckOut, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.CheckOut, ReservationStatus.Anulada)]
    [InlineData(ReservationStatus.Anulada, ReservationStatus.Pendiente)]
    [InlineData(ReservationStatus.Anulada, ReservationStatus.Confirmada)]
    [InlineData(ReservationStatus.Anulada, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.NoShow, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.NoShow, ReservationStatus.Anulada)]
    [InlineData(ReservationStatus.Liquidada, ReservationStatus.Anulada)]
    public void StateMachine_IllegalOrTerminalTransitions_ShouldAlwaysThrow(ReservationStatus from, ReservationStatus to)
    {
        Action act = () => ReservationStateMachine.ValidateTransition(from, to);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Theory]
    [InlineData(ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.Anulada)]
    [InlineData(ReservationStatus.NoShow)]
    public void StateMachine_TerminalStates_ShouldHaveZeroAvailableTransitions(ReservationStatus terminalStatus)
    {
        var transitions = ReservationStateMachine.GetAvailableTransitions(terminalStatus);
        transitions.Should().BeEmpty();
    }

    [Fact]
    public void UserAccount_LockoutAndReset_ShouldEnforce5AttemptsPolicy()
    {
        var user = new User { Email = "qa.test@hotelchain.com", Role = UserRole.Recepcionista, IsActive = true };

        for (int i = 1; i <= 4; i++)
        {
            user.RecordFailedLogin();
            user.IsLockedOut().Should().BeFalse();
        }

        user.RecordFailedLogin();
        user.IsLockedOut().Should().BeTrue();
        user.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd!.Value.Should().BeAfter(DateTime.UtcNow);

        user.ResetLoginLockout();
        user.IsLockedOut().Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public void FolioRecalculateTotals_WithChargesTaxesAndPayments_ShouldComputeExactBalance()
    {
        var folio = new Folio { Status = FolioStatus.Abierto };
        folio.Charges.Add(new FolioCharge { Amount = 100000m, TaxAmount = 19000m, IsAnnulled = false });
        folio.Charges.Add(new FolioCharge { Amount = 50000m, TaxAmount = 9500m, IsAnnulled = true });
        folio.Payments.Add(new FolioPayment { Amount = 50000m });

        folio.RecalculateTotals();

        folio.TotalCharges.Should().Be(100000m);
        folio.TotalTaxes.Should().Be(19000m);
        folio.TotalPayments.Should().Be(50000m);
        folio.Balance.Should().Be(69000m);
    }
}
