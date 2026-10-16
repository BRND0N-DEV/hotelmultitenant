using System;

namespace HotelManagement.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class InvalidStateTransitionException : DomainException
{
    public string CurrentState { get; }
    public string TargetState { get; }

    public InvalidStateTransitionException(string currentState, string targetState, string message)
        : base(message)
    {
        CurrentState = currentState;
        TargetState = targetState;
    }
}

public class OverbookingException : DomainException
{
    public int RoomId { get; }
    public DateTime CheckInDate { get; }
    public DateTime CheckOutDate { get; }

    public OverbookingException(int roomId, DateTime checkIn, DateTime checkOut, string message)
        : base(message)
    {
        RoomId = roomId;
        CheckInDate = checkIn;
        CheckOutDate = checkOut;
    }
}

public class FolioBalanceNonZeroException : DomainException
{
    public decimal Balance { get; }

    public FolioBalanceNonZeroException(decimal balance, string message)
        : base(message)
    {
        Balance = balance;
    }
}

public class TenantAccessViolationException : DomainException
{
    public int RequestedHotelId { get; }
    public int ActiveHotelId { get; }

    public TenantAccessViolationException(int requestedHotelId, int activeHotelId, string message)
        : base(message)
    {
        RequestedHotelId = requestedHotelId;
        ActiveHotelId = activeHotelId;
    }
}

public class EntityNotFoundException : DomainException
{
    public string EntityName { get; }
    public object Key { get; }

    public EntityNotFoundException(string entityName, object key)
        : base($"No se encontró la entidad '{entityName}' con identificador '{key}'.")
    {
        EntityName = entityName;
        Key = key;
    }
}
