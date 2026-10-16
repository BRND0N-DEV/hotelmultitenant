-- ==============================================================================
-- SISTEMA DE GESTIÓN HOTELERA INTEGRAL MULTITENANT (PMS)
-- SCRIPT DE TRIGGER: PREVENCIÓN ESTRICTA DE OVERBOOKING EN BASE DE DATOS
-- REQUISITOS: RF-24, RNF-11
-- ==============================================================================

USE HotelManagementDb;
GO

IF OBJECT_ID(N'dbo.TR_Reservations_PreventOverbooking', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_Reservations_PreventOverbooking;
GO

CREATE TRIGGER dbo.TR_Reservations_PreventOverbooking
ON dbo.Reservations
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Solo evaluamos registros que tengan un estado activo que reserve inventario
    -- Estados activos: 'Pendiente', 'Confirmada', 'CheckIn'
    -- Estados liberados: 'CheckOut', 'Liquidada', 'Anulada', 'NoShow'
    IF NOT EXISTS (
        SELECT 1 
        FROM inserted 
        WHERE Status IN ('Pendiente', 'Confirmada', 'CheckIn')
    )
    BEGIN
        RETURN;
    END

    -- 1. Comprobación contra reservas existentes en la base de datos
    IF EXISTS (
        SELECT 1
        FROM inserted i
        INNER JOIN dbo.Reservations r ON r.RoomId = i.RoomId AND r.ReservationId <> i.ReservationId
        WHERE i.Status IN ('Pendiente', 'Confirmada', 'CheckIn')
          AND r.Status IN ('Pendiente', 'Confirmada', 'CheckIn')
          -- Lógica de traslape: [In1, Out1) traslapa con [In2, Out2) si In1 < Out2 Y Out1 > In2
          AND i.CheckInDate < r.CheckOutDate
          AND i.CheckOutDate > r.CheckInDate
    )
    BEGIN
        RAISERROR (N'CONFLICTO DE OVERBOOKING (RF-24 / RNF-11): La habitación seleccionada ya tiene una reserva activa en las fechas solicitadas.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- 2. Comprobación de inserciones por lotes concurrentes dentro del mismo comando INSERT
    IF EXISTS (
        SELECT 1
        FROM inserted i1
        INNER JOIN inserted i2 ON i1.RoomId = i2.RoomId AND i1.ReservationId <> i2.ReservationId
        WHERE i1.Status IN ('Pendiente', 'Confirmada', 'CheckIn')
          AND i2.Status IN ('Pendiente', 'Confirmada', 'CheckIn')
          AND i1.CheckInDate < i2.CheckOutDate
          AND i1.CheckOutDate > i2.CheckInDate
    )
    BEGIN
        RAISERROR (N'CONFLICTO DE OVERBOOKING: Múltiples reservas simultáneas para la misma habitación y rango de fechas.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO
