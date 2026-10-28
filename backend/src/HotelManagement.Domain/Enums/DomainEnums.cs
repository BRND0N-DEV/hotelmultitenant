using System.Text.Json.Serialization;

namespace HotelManagement.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReservationStatus
{
    Pendiente = 1,
    Confirmada = 2,
    CheckIn = 3,
    CheckOut = 4,
    Liquidada = 5,
    Anulada = 6,
    NoShow = 7
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoomOperationalStatus
{
    Disponible = 1,
    Ocupada = 2,
    FueraDeServicio = 3,
    Mantenimiento = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoomCleaningStatus
{
    Limpia = 1,
    Sucia = 2,
    EnLimpieza = 3,
    EnRevision = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentMethod
{
    Efectivo = 1,
    TarjetaDebito = 2,
    TarjetaCredito = 3,
    Transferencia = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FolioStatus
{
    Abierto = 1,
    Cerrado = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShiftStatus
{
    Abierto = 1,
    Cerrado = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditLevel
{
    Negocio = 1,
    Tecnico = 2,
    Http = 3
}

public static class UserRole
{
    public const string AdminGlobal = "AdminGlobal";
    public const string AdminSede = "AdminSede";
    public const string Recepcionista = "Recepcionista";
    public const string Housekeeping = "Housekeeping";
    public const string Auditor = "Auditor";
    public const string Socio = "Socio";
}
