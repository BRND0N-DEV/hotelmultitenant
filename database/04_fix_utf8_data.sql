-- ==============================================================================
-- SISTEMA DE GESTIÓN HOTELERA INTEGRAL MULTITENANT (PMS)
-- SCRIPT DE MIGRACIÓN: CORRECCIÓN INTEGRAL DE CODIFICACIÓN UTF-8 (DOUBLE-ENCODED)
-- ==============================================================================

USE HotelManagementDb;
GO

SET NOCOUNT ON;

BEGIN TRANSACTION;

-- Función o función escalar inline para reemplazo recursivo de secuencias UTF-8 corrompidas
-- 1. HOTELES
UPDATE dbo.Hotels
SET 
    Name = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    City = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(City, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Address = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Address, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Name LIKE '%Ã%' OR City LIKE '%Ã%' OR Address LIKE '%Ã%';

-- 2. TIPOS DE HABITACIÓN
UPDATE dbo.RoomTypes
SET 
    Name = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Description = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Description, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Amenities = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Amenities, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Name LIKE '%Ã%' OR Description LIKE '%Ã%' OR Amenities LIKE '%Ã%';

-- 3. USUARIOS
UPDATE dbo.Users
SET 
    FullName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(FullName, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Department = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Department, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Shift = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Shift, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE FullName LIKE '%Ã%' OR Department LIKE '%Ã%' OR Shift LIKE '%Ã%';

-- 4. HUÉSPEDES
UPDATE dbo.Guests
SET 
    FirstName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(FirstName, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    LastName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LastName, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Nationality = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Nationality, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE FirstName LIKE '%Ã%' OR LastName LIKE '%Ã%' OR Nationality LIKE '%Ã%';

-- 5. POLÍTICAS DE CANCELACIÓN
UPDATE dbo.CancellationPolicies
SET 
    Name = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Description = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Description, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Name LIKE '%Ã%' OR Description LIKE '%Ã%';

-- 6. TEMPORADAS
UPDATE dbo.Seasons
SET 
    Name = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Name LIKE '%Ã%';

-- 7. SERVICIOS ADICIONALES
UPDATE dbo.AdditionalServices
SET 
    Name = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ'),
    Category = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Category, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Name LIKE '%Ã%' OR Category LIKE '%Ã%';

-- 8. CARGOS DE FOLIO
UPDATE dbo.FolioCharges
SET 
    Description = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Description, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Description LIKE '%Ã%';

-- 9. RESERVAS
UPDATE dbo.Reservations
SET 
    SpecialRequests = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(SpecialRequests, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE SpecialRequests LIKE '%Ã%';

-- 10. HISTORIAL DE ESTADOS DE RESERVA
UPDATE dbo.ReservationStateHistories
SET 
    Reason = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Reason, N'Ã¡', N'á'), N'Ã©', N'é'), N'Ã­', N'í'), N'Ã³', N'ó'), N'Ãº', N'ú'), N'Ã±', N'ñ')
WHERE Reason LIKE '%Ã%';

COMMIT TRANSACTION;
GO
