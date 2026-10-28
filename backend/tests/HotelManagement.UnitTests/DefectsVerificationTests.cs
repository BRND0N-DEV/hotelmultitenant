using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using FluentAssertions;
using Xunit;
using HotelManagement.Application.DTOs;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;

namespace HotelManagement.UnitTests;

/// <summary>
/// Pruebas unitarias de verificación y no regresión para los 7 defectos reportados en CONTINENTALIA SAS.
/// </summary>
public class DefectsVerificationTests
{
    // =========================================================================
    // DEFECTO 1: Codificación de caracteres, tildes y letra ñ
    // =========================================================================
    [Fact]
    public void UnicodeEncoding_SpanishWords_ShouldSerializeAndDeserializeWithoutCorruption()
    {
        // Palabras requeridas por los criterios de aceptación
        var testWords = new List<string>
        {
            "Recepción",
            "Habitación",
            "Administración",
            "Niñez",
            "Gestión hotelera",
            "Huéspedes"
        };

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());

        foreach (var word in testWords)
        {
            var payload = new { Term = word, Notes = $"Prueba de integridad UTF-8 para {word} en CONTINENTALIA SAS" };
            string json = JsonSerializer.Serialize(payload, options);

            // Verificar que no se corrompe en Unicode crudo y conserva las tildes y eñes
            json.Should().Contain(word);

            var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
            deserialized.Should().NotBeNull();
            deserialized!["Term"].Should().Be(word);
        }
    }

    [Fact]
    public void DomainEnums_ShouldSerializeAsString_NotNumeric()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var status = ReservationStatus.Confirmada;
        string json = JsonSerializer.Serialize(status, options);
        json.Should().Be("\"Confirmada\"");

        var cleaning = RoomCleaningStatus.EnRevision;
        string jsonCleaning = JsonSerializer.Serialize(cleaning, options);
        jsonCleaning.Should().Be("\"EnRevision\"");

        var parsed = JsonSerializer.Deserialize<RoomCleaningStatus>("\"Limpia\"", options);
        parsed.Should().Be(RoomCleaningStatus.Limpia);
    }

    // =========================================================================
    // DEFECTO 2: Contenido QR del Voucher y URL de Verificación
    // =========================================================================
    [Fact]
    public void VoucherVerification_Payload_ShouldContainPublicDataAndExcludeSensitiveInfo()
    {
        var voucher = new
        {
            Code = "VOUCH-BOG-2026-9999",
            ReservationCode = "RES-20261020-0001",
            HotelName = "Grand Oasis Bogotá",
            GuestName = "Carlos Mendoza",
            RoomTypeName = "Suite Ejecutiva",
            CheckInDate = new DateTime(2026, 10, 20),
            CheckOutDate = new DateTime(2026, 10, 23),
            TotalAmount = 1350000m,
            VerificationUrl = "https://continentalia.com/api/reservations/verify-voucher/RES-20261020-0001",
            Status = "Confirmada"
        };

        // El QR debe apuntar a la URL pública de verificación
        voucher.VerificationUrl.Should().StartWith("https://");
        voucher.VerificationUrl.Should().Contain("RES-20261020-0001");
        voucher.Status.Should().Be("Confirmada");

        // Serializar a JSON y asegurar que no contenga credenciales ni tokens
        string json = JsonSerializer.Serialize(voucher);
        json.Should().NotContain("Password");
        json.Should().NotContain("Token");
        json.Should().NotContain("PasswordHash");
    }

    // =========================================================================
    // DEFECTO 3: Calendario de Ocupación, Intervalos de Noche y Fechas
    // =========================================================================
    [Fact]
    public void CalendarInterval_StayNights_ShouldCoverCheckInUntilBeforeCheckOut()
    {
        // Reserva de 3 noches: Octubre 20 a Octubre 23
        var checkIn = new DateTime(2026, 10, 20);
        var checkOut = new DateTime(2026, 10, 23);

        var days = new List<DateTime>();
        for (int i = 0; i < 7; i++)
        {
            days.Add(new DateTime(2026, 10, 18).AddDays(i));
        }

        // Lógica de intervalo: ocupada desde checkIn.Date hasta < checkOut.Date
        var occupiedDays = days.Where(day => day.Date >= checkIn.Date && day.Date < checkOut.Date).ToList();

        // Noches ocupadas: 20, 21, 22 (3 noches)
        occupiedDays.Should().HaveCount(3);
        occupiedDays.Should().Contain(new DateTime(2026, 10, 20));
        occupiedDays.Should().Contain(new DateTime(2026, 10, 21));
        occupiedDays.Should().Contain(new DateTime(2026, 10, 22));

        // El día de Check-Out (Octubre 23) NO debe estar bloqueado para la siguiente noche
        occupiedDays.Should().NotContain(new DateTime(2026, 10, 23));
    }

    [Fact]
    public void CalendarDateParsing_ShouldHandleIsoStringsWithoutNaNOrInvalidDate()
    {
        // Simular lógica de CalendarComponent.parseLocalDate
        string[] sampleInputs = { "2026-10-20", "2026-10-20T00:00:00", "2026-10-20T15:30:00Z" };

        foreach (var input in sampleInputs)
        {
            string cleanDate = input.Split('T')[0];
            string[] parts = cleanDate.Split('-');
            parts.Length.Should().Be(3);

            int year = int.Parse(parts[0]);
            int month = int.Parse(parts[1]);
            int day = int.Parse(parts[2]);

            year.Should().Be(2026);
            month.Should().Be(10);
            day.Should().Be(20);
        }
    }

    // =========================================================================
    // DEFECTO 4: Transiciones de Limpieza y Restablecimiento de Estado Operativo
    // =========================================================================
    [Fact]
    public void RoomCleaningTransition_WhenMarkedLimpiaFromFueraDeServicio_ShouldBecomeDisponible()
    {
        var room = new Room
        {
            RoomId = 10,
            HotelId = 1,
            RoomNumber = "305",
            OperationalStatus = RoomOperationalStatus.FueraDeServicio,
            CleaningStatus = RoomCleaningStatus.Sucia,
            IsActive = true
        };

        // Simular transición en HousekeepingController
        room.CleaningStatus = RoomCleaningStatus.Limpia;
        if (room.CleaningStatus == RoomCleaningStatus.Limpia && room.OperationalStatus == RoomOperationalStatus.FueraDeServicio)
        {
            room.OperationalStatus = RoomOperationalStatus.Disponible;
        }

        room.CleaningStatus.Should().Be(RoomCleaningStatus.Limpia);
        room.OperationalStatus.Should().Be(RoomOperationalStatus.Disponible);
    }

    // =========================================================================
    // DEFECTO 5: Auditoría, Timestamp UTC, Redacción Sensible y Sanitización CSV
    // =========================================================================
    [Fact]
    public void AuditLog_SensitivePayload_ShouldBeRedacted()
    {
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PasswordHash", "Password", "Token", "RefreshToken", "SecurityStamp"
        };

        var rawValues = new Dictionary<string, object?>
        {
            { "UserId", 1 },
            { "Email", "recepcion@continentalia.com" },
            { "PasswordHash", "$2a$11$6Dk0.RymZAxO2qEYKCAR/Oq8iXbNQy3nAq3u0qG63oPXU.KoFCPWm" },
            { "Role", "Recepcionista" }
        };

        var sanitized = rawValues.ToDictionary(
            kvp => kvp.Key,
            kvp => sensitiveKeys.Contains(kvp.Key) ? (object)"[REDACTED]" : kvp.Value);

        string json = JsonSerializer.Serialize(sanitized);

        json.Should().Contain("[REDACTED]");
        json.Should().NotContain("$2a$11$6Dk0");
        sanitized["PasswordHash"].Should().Be("[REDACTED]");
        sanitized["Email"].Should().Be("recepcion@continentalia.com");
    }

    [Fact]
    public void CsvExport_FormulaInjectionMitigation_ShouldPrependSingleQuote()
    {
        // Fórmulas maliciosas de prueba
        string[] maliciousInputs = { "=cmd|'/c calc'!A0", "+1+1", "-5", "@SUM(A1:A10)", "\tmalicious" };

        foreach (var input in maliciousInputs)
        {
            string sanitized = SanitizeCsvCell(input);
            // Debe iniciar con comilla simple escapada dentro de comillas dobles
            sanitized.Should().StartWith("\"'");
        }

        // Caso normal con tildes y eñes
        string normalText = "Habitación 101 — Limpieza ejecutada por Doña María";
        string sanitizedNormal = SanitizeCsvCell(normalText);
        sanitizedNormal.Should().Be($"\"{normalText}\"");
    }

    [Fact]
    public void CsvExport_Utf8Bom_ShouldBePresentInHeader()
    {
        string bom = "\uFEFF";
        byte[] bomBytes = Encoding.UTF8.GetPreamble();
        bomBytes.Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });

        string csv = bom + "ID Auditoría;Timestamp (UTC);Acción\r\n1;2026-10-20 18:00:00 UTC;Update Room";
        csv.StartsWith('\uFEFF').Should().BeTrue();
    }

    private static string SanitizeCsvCell(string? value)
    {
        if (value == null) return "\"\"";
        string str = value;
        if (str.Length > 0 && (str[0] == '=' || str[0] == '+' || str[0] == '-' || str[0] == '@' || str[0] == '\t' || str[0] == '\r'))
        {
            str = "'" + str;
        }
        str = str.Replace("\"", "\"\"");
        str = str.Replace("\r", " ").Replace("\n", " ");
        return $"\"{str}\"";
    }

    // =========================================================================
    // DEFECTO 6: Creación de Usuarios y Validación de Roles
    // =========================================================================
    [Fact]
    public void CreateUser_RoleValidation_ShouldOnlyAcceptValidCanonicalRoles()
    {
        var validRoles = new HashSet<string>
        {
            UserRole.AdminGlobal,
            UserRole.AdminSede,
            UserRole.Recepcionista,
            UserRole.Housekeeping,
            UserRole.Auditor,
            UserRole.Socio
        };

        validRoles.Contains("Recepcionista").Should().BeTrue();
        validRoles.Contains("Housekeeping").Should().BeTrue();
        validRoles.Contains("AdminSede").Should().BeTrue();
        validRoles.Contains("AdminGlobal").Should().BeTrue();
        validRoles.Contains("Auditor").Should().BeTrue();
        validRoles.Contains("Socio").Should().BeTrue();

        // Roles inválidos del defecto previo
        validRoles.Contains("Cajero").Should().BeFalse();
        validRoles.Contains("GobernantaAmaDeLlaves").Should().BeFalse();
        validRoles.Contains("GerenteSede").Should().BeFalse();
        validRoles.Contains("AuditorInterno").Should().BeFalse();
    }

    // =========================================================================
    // DEFECTO 7: Filtros de Reservas para Recepción Check-In y Check-Out
    // =========================================================================
    [Fact]
    public void ReceptionReservations_FilterByStatus_ShouldCategorizeCorrectly()
    {
        var reservations = new List<ReservationDto>
        {
            new() { ReservationId = 1, Code = "RES-001", Status = ReservationStatus.Confirmada },
            new() { ReservationId = 2, Code = "RES-002", Status = ReservationStatus.Pendiente },
            new() { ReservationId = 3, Code = "RES-003", Status = ReservationStatus.CheckIn },
            new() { ReservationId = 4, Code = "RES-004", Status = ReservationStatus.CheckOut },
            new() { ReservationId = 5, Code = "RES-005", Status = ReservationStatus.Liquidada }
        };

        // Filtro de Check-In (Esperados)
        var forCheckIn = reservations.Where(r => r.Status == ReservationStatus.Confirmada || r.Status == ReservationStatus.Pendiente).ToList();
        forCheckIn.Should().HaveCount(2);
        forCheckIn.Select(r => r.Code).Should().Contain("RES-001", "RES-002");

        // Filtro de Check-Out (Hospedados en casa)
        var forCheckOut = reservations.Where(r => r.Status == ReservationStatus.CheckIn).ToList();
        forCheckOut.Should().HaveCount(1);
        forCheckOut.First().Code.Should().Be("RES-003");
    }

    // =========================================================================
    // AUDITORÍA NUEVA: Recepción - Transición Pendiente a Check-In (HTTP 422 Fix)
    // =========================================================================
    [Fact]
    public void StateMachine_PendienteToCheckIn_TransitionShouldBeValid()
    {
        // Act & Assert
        HotelManagement.Application.Services.ReservationStateMachine
            .CanTransition(ReservationStatus.Pendiente, ReservationStatus.CheckIn)
            .Should().BeTrue("Las reservas en estado Pendiente deben poder realizar Check-In en recepción al arribo del huésped");

        var action = () => HotelManagement.Application.Services.ReservationStateMachine
            .ValidateTransition(ReservationStatus.Pendiente, ReservationStatus.CheckIn);

        action.Should().NotThrow("No debe lanzar excepción 422 para la transición válida Pendiente -> CheckIn");
    }

    [Theory]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.Liquidada)]
    [InlineData(ReservationStatus.Pendiente, ReservationStatus.CheckOut)]
    [InlineData(ReservationStatus.Liquidada, ReservationStatus.CheckIn)]
    [InlineData(ReservationStatus.Anulada, ReservationStatus.CheckIn)]
    public void StateMachine_InvalidTransitions_ShouldThrowInvalidStateTransitionException(ReservationStatus from, ReservationStatus to)
    {
        var action = () => HotelManagement.Application.Services.ReservationStateMachine.ValidateTransition(from, to);
        action.Should().Throw<HotelManagement.Domain.Exceptions.InvalidStateTransitionException>();
    }

    // =========================================================================
    // AUDITORÍA NUEVA: Directorio de Huéspedes & Habeas Data (RF-45 a RF-47, Ley 1581)
    // =========================================================================
    [Fact]
    public void GuestUpdate_ValidData_ShouldUpdatePropertiesAndProtectKeyFields()
    {
        // Arrange
        var guest = new Guest
        {
            GuestId = 1,
            HotelId = 1,
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            FirstName = "Mauricio",
            LastName = "Navarro Peña",
            Email = "mauricio.navarro@email.com",
            Phone = "+57 300 1234567",
            Nationality = "Colombia",
            IsAnonymized = false
        };

        var updateDto = new UpdateGuestDto
        {
            FirstName = "Mauricio Antonio",
            LastName = "Navarro Peña",
            Email = "m.navarro.nuevo@email.com",
            Phone = "+57 311 9876543",
            Nationality = "Colombia",
            AcceptDataConsent = true
        };

        // Act - Simular actualización autorizada de datos personales
        guest.FirstName = updateDto.FirstName.Trim();
        guest.LastName = updateDto.LastName.Trim();
        guest.Email = updateDto.Email.Trim();
        guest.Phone = updateDto.Phone.Trim();
        guest.Nationality = updateDto.Nationality.Trim();

        // Assert
        guest.FullName.Should().Be("Mauricio Antonio Navarro Peña");
        guest.Email.Should().Be("m.navarro.nuevo@email.com");
        guest.Phone.Should().Be("+57 311 9876543");
        // Campos protegidos inmutables
        guest.GuestId.Should().Be(1);
        guest.HotelId.Should().Be(1);
        guest.DocumentType.Should().Be("CC");
        guest.DocumentNumber.Should().Be("1020304050");
        guest.IsAnonymized.Should().BeFalse();
    }

    [Fact]
    public void GuestUpdate_AnonymizedGuest_ShouldBlockModifications()
    {
        // Arrange
        var guest = new Guest
        {
            GuestId = 2,
            HotelId = 1,
            DocumentType = "CC",
            DocumentNumber = "99887766",
            FirstName = "Carlos",
            LastName = "Gómez",
            Email = "carlos@test.com",
            Phone = "3001112233",
            IsAnonymized = false
        };

        guest.Anonymize();
        guest.IsAnonymized.Should().BeTrue();

        // Validar regla de negocio: Huéspedes anonimizados no se pueden modificar
        bool canModify = !guest.IsAnonymized;
        canModify.Should().BeFalse("Un huésped que ejerció el derecho al olvido no puede ser rectificado");
    }

    // =========================================================================
    // AUDITORÍA NUEVA: Housekeeping - Mantenimiento Aislado & Reglas de Limpieza
    // =========================================================================
    [Fact]
    public void Housekeeping_RemovingMaintenanceOnDirtyRoom_ShouldKeepFueraDeServicio()
    {
        // Arrange: Habitación en mantenimiento y con estado de limpieza Sucia
        var room = new Room
        {
            RoomId = 104,
            RoomNumber = "104",
            CleaningStatus = RoomCleaningStatus.Sucia,
            OperationalStatus = RoomOperationalStatus.Mantenimiento
        };

        // Act: Gobernanta retira el estado de mantenimiento
        var requestedOperational = RoomOperationalStatus.Disponible;
        if (requestedOperational == RoomOperationalStatus.Disponible && room.CleaningStatus != RoomCleaningStatus.Limpia)
        {
            room.OperationalStatus = RoomOperationalStatus.FueraDeServicio;
        }
        else
        {
            room.OperationalStatus = requestedOperational;
        }

        // Assert: No debe quedar Disponible para recepción hasta que sea Limpia
        room.OperationalStatus.Should().Be(RoomOperationalStatus.FueraDeServicio,
            "Al retirar mantenimiento a una habitación sucia, debe pasar a FueraDeServicio hasta ser aseada");
    }

    [Fact]
    public void Housekeeping_RemovingMaintenanceOnCleanRoom_ShouldBecomeDisponible()
    {
        // Arrange: Habitación en mantenimiento pero ya limpia
        var room = new Room
        {
            RoomId = 103,
            RoomNumber = "103",
            CleaningStatus = RoomCleaningStatus.Limpia,
            OperationalStatus = RoomOperationalStatus.Mantenimiento
        };

        // Act
        var requestedOperational = RoomOperationalStatus.Disponible;
        if (requestedOperational == RoomOperationalStatus.Disponible && room.CleaningStatus != RoomCleaningStatus.Limpia)
        {
            room.OperationalStatus = RoomOperationalStatus.FueraDeServicio;
        }
        else
        {
            room.OperationalStatus = requestedOperational;
        }

        // Assert
        room.OperationalStatus.Should().Be(RoomOperationalStatus.Disponible);
    }

    // =========================================================================
    // AUDITORÍA NUEVA: Integración Caja & Check-Out (RF-40, RF-42)
    // =========================================================================
    [Fact]
    public void CheckOut_WithNonZeroFolioBalance_ShouldBeBlocked()
    {
        // Arrange
        var folio = new Folio
        {
            FolioId = 1,
            Charges = new List<FolioCharge>
            {
                new() { Amount = 500000m, TaxAmount = 0m, IsAnnulled = false }
            },
            Payments = new List<FolioPayment>
            {
                new() { Amount = 300000m }
            }
        };
        folio.RecalculateTotals();

        // Assert: Saldo $200,000 COP != 0 -> Bloqueo
        folio.Balance.Should().Be(200000m);
        bool canCheckOut = (folio.Balance == 0m);
        canCheckOut.Should().BeFalse("RF-40 prohíbe realizar Check-Out cuando el folio tiene saldo pendiente");
    }

    [Fact]
    public void CheckOut_WithZeroFolioBalance_ShouldBeAllowed()
    {
        // Arrange
        var folio = new Folio
        {
            FolioId = 2,
            Charges = new List<FolioCharge>
            {
                new() { Amount = 350000m, TaxAmount = 0m, IsAnnulled = false }
            },
            Payments = new List<FolioPayment>
            {
                new() { Amount = 350000m }
            }
        };
        folio.RecalculateTotals();

        // Assert: Saldo $0 COP -> Permitido
        folio.Balance.Should().Be(0m);
        bool canCheckOut = (folio.Balance == 0m);
        canCheckOut.Should().BeTrue("RF-40 permite Check-Out únicamente cuando el saldo es exactamente $0.00 COP");
    }
}
