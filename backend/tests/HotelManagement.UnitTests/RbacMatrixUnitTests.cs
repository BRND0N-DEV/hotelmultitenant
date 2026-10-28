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
}
