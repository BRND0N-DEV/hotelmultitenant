-- ==============================================================================
-- SISTEMA DE GESTIÓN HOTELERA INTEGRAL MULTITENANT (PMS)
-- SCRIPT DML: DATOS SEMILLA (SEED DATA) PARA 4 HOTELES ASOCIADOS
-- REQUISITOS: RF-08 A RF-17, RF-49 A RF-55
-- CONTRASEÑA POR DEFECTO PARA TODOS LOS USUARIOS: Admin123*!
-- HASH BCRYPT: $2a$11$6Dk0.RymZAxO2qEYKCAR/Oq8iXbNQy3nAq3u0qG63oPXU.KoFCPWm
-- ==============================================================================

USE HotelManagementDb;
GO

SET NOCOUNT ON;

-- 1. INSERTAR LAS 4 SEDES DE LA CADENA HOTELERA
IF NOT EXISTS (SELECT 1 FROM dbo.Hotels WHERE Code = 'BOG-01')
BEGIN
    INSERT INTO dbo.Hotels (Code, Name, City, Address, TimeZone, Currency, PrimaryColor, SecondaryColor, LogoUrl, IsActive)
    VALUES 
    ('BOG-01', N'Grand Oasis Bogotá', N'Bogotá', N'Calle 93 # 14-20, Chico', 'America/Bogota', 'COP', '#17365D', '#D9EAF7', '/assets/branding/hotel1-logo.svg', 1),
    ('CTG-02', N'Caribe Real Cartagena', N'Cartagena', N'Bocagrande Carrera 1 # 12-45', 'America/Bogota', 'COP', '#0077B6', '#90E0EF', '/assets/branding/hotel2-logo.svg', 1),
    ('MDE-03', N'Montaña Dorada Medellín', N'Medellín', N'El Poblado Carrera 43A # 7-50', 'America/Bogota', 'COP', '#2D6A4F', '#74C69D', '/assets/branding/hotel3-logo.svg', 1),
    ('CLO-04', N'Valle Encantado Cali', N'Cali', N'Barrio Granada Calle 9N # 4-32', 'America/Bogota', 'COP', '#8338EC', '#C77DFF', '/assets/branding/hotel4-logo.svg', 1);
END
GO

DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @HotelCtgId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CTG-02');
DECLARE @HotelMdeId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'MDE-03');
DECLARE @HotelCloId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CLO-04');
DECLARE @PwdHash NVARCHAR(255) = N'$2a$11$6Dk0.RymZAxO2qEYKCAR/Oq8iXbNQy3nAq3u0qG63oPXU.KoFCPWm';

-- 2. INSERTAR USUARIOS DEL SISTEMA
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = 'admin@hotelchain.com')
BEGIN
    INSERT INTO dbo.Users (Email, PasswordHash, FullName, Role, Department, Shift, IsActive)
    VALUES 
    ('admin@hotelchain.com', @PwdHash, N'Carlos Mendoza (Admin Global)', 'AdminGlobal', 'Dirección General', 'Mañana', 1),
    ('socio@hotelchain.com', @PwdHash, N'Dra. Patricia Arango (Junta Socios)', 'Socio', 'Junta Directiva', 'Mañana', 1),
    ('auditor@hotelchain.com', @PwdHash, N'Rodrigo Velásquez (Auditor Cadena)', 'Auditor', 'Auditoría Interna', 'Mañana', 1),
    -- Sede Bogotá
    ('admin.bogota@hotelchain.com', @PwdHash, N'Mariana Gómez (Admin Bogotá)', 'AdminSede', 'Administración', 'Mañana', 1),
    ('recepcion.bogota@hotelchain.com', @PwdHash, N'Juan Camilo Morales (Recepción Bogotá)', 'Recepcionista', 'Recepción', 'Mañana', 1),
    ('recepcion2.bogota@hotelchain.com', @PwdHash, N'Luisa Fernanda Castro (Recepción Tarde)', 'Recepcionista', 'Recepción', 'Tarde', 1),
    ('housekeeping.bogota@hotelchain.com', @PwdHash, N'Rosa Elena Benítez (Camarera Bogotá)', 'Housekeeping', 'Pisos y Limpieza', 'Mañana', 1),
    -- Sede Cartagena
    ('admin.cartagena@hotelchain.com', @PwdHash, N'Gabriel Puerta (Admin Cartagena)', 'AdminSede', 'Administración', 'Mañana', 1),
    ('recepcion.cartagena@hotelchain.com', @PwdHash, N'Sofía Vergara (Recepción Cartagena)', 'Recepcionista', 'Recepción', 'Mañana', 1),
    ('housekeeping.cartagena@hotelchain.com', @PwdHash, N'Marta Lucia Reyes (Camarera CTG)', 'Housekeeping', 'Pisos y Limpieza', 'Mañana', 1),
    -- Sede Medellín
    ('admin.medellin@hotelchain.com', @PwdHash, N'Andrés Botero (Admin Medellín)', 'AdminSede', 'Administración', 'Mañana', 1),
    ('recepcion.medellin@hotelchain.com', @PwdHash, N'Camila Restrepo (Recepción MDE)', 'Recepcionista', 'Recepción', 'Mañana', 1),
    ('housekeeping.medellin@hotelchain.com', @PwdHash, N'Luz Dary Ochoa (Camarera MDE)', 'Housekeeping', 'Pisos y Limpieza', 'Mañana', 1),
    -- Sede Cali
    ('admin.cali@hotelchain.com', @PwdHash, N'Julián Osorio (Admin Cali)', 'AdminSede', 'Administración', 'Mañana', 1),
    ('recepcion.cali@hotelchain.com', @PwdHash, N'Valentina Caicedo (Recepción Cali)', 'Recepcionista', 'Recepción', 'Mañana', 1),
    ('housekeeping.cali@hotelchain.com', @PwdHash, N'Gloria Inés Paz (Camarera Cali)', 'Housekeeping', 'Pisos y Limpieza', 'Mañana', 1);
END
GO

-- 3. ASIGNACIÓN DE USUARIOS A HOTELES
DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @HotelCtgId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CTG-02');
DECLARE @HotelMdeId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'MDE-03');
DECLARE @HotelCloId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CLO-04');

IF NOT EXISTS (SELECT 1 FROM dbo.HotelUsers)
BEGIN
    -- Admin Global y Socios asignados a todas las sedes
    INSERT INTO dbo.HotelUsers (UserId, HotelId, IsDefault)
    SELECT u.UserId, h.HotelId, CASE WHEN h.HotelId = @HotelBogId THEN 1 ELSE 0 END
    FROM dbo.Users u
    CROSS JOIN dbo.Hotels h
    WHERE u.Role IN ('AdminGlobal', 'Socio', 'Auditor');

    -- Usuarios de Bogotá
    INSERT INTO dbo.HotelUsers (UserId, HotelId, IsDefault)
    SELECT u.UserId, @HotelBogId, 1
    FROM dbo.Users u WHERE u.Email LIKE '%.bogota@hotelchain.com';

    -- Usuarios de Cartagena
    INSERT INTO dbo.HotelUsers (UserId, HotelId, IsDefault)
    SELECT u.UserId, @HotelCtgId, 1
    FROM dbo.Users u WHERE u.Email LIKE '%.cartagena@hotelchain.com';

    -- Usuarios de Medellín
    INSERT INTO dbo.HotelUsers (UserId, HotelId, IsDefault)
    SELECT u.UserId, @HotelMdeId, 1
    FROM dbo.Users u WHERE u.Email LIKE '%.medellin@hotelchain.com';

    -- Usuarios de Cali
    INSERT INTO dbo.HotelUsers (UserId, HotelId, IsDefault)
    SELECT u.UserId, @HotelCloId, 1
    FROM dbo.Users u WHERE u.Email LIKE '%.cali@hotelchain.com';
END
GO

-- 4. PARÁMETROS POR HOTEL: TIPOS DE HABITACIÓN
DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @HotelCtgId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CTG-02');
DECLARE @HotelMdeId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'MDE-03');
DECLARE @HotelCloId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CLO-04');

IF NOT EXISTS (SELECT 1 FROM dbo.RoomTypes)
BEGIN
    -- Sede Bogotá
    INSERT INTO dbo.RoomTypes (HotelId, Name, Code, BaseCapacity, MaxCapacity, BasePrice, Description, Amenities, IsActive)
    VALUES 
    (@HotelBogId, N'Estándar Sencilla', 'STD-SGL', 1, 1, 180000.00, N'Habitación cómoda con cama queen y escritorio de trabajo', 'WiFi 100Mbps, TV 50", Minibar, Aire Acondicionado, Escritorio', 1),
    (@HotelBogId, N'Doble Superior', 'DBL-SUP', 2, 3, 260000.00, N'Espaciosa con dos camas dobles y vista a la ciudad', 'WiFi 100Mbps, TV 55", Minibar, Caja Fuerte, Tina, Cafetera', 1),
    (@HotelBogId, N'Suite Ejecutiva', 'STE-EJE', 2, 4, 450000.00, N'Suite de lujo con sala de estar, jacuzzi y balcón', 'WiFi 200Mbps, 2 TV 65", Jacuzzi, Sala, Minibar Premium, Vista Panorámica', 1);

    -- Sede Cartagena
    INSERT INTO dbo.RoomTypes (HotelId, Name, Code, BaseCapacity, MaxCapacity, BasePrice, Description, Amenities, IsActive)
    VALUES 
    (@HotelCtgId, N'Estándar Playa', 'STD-PLY', 2, 2, 290000.00, N'Habitación fresca con balcón y brisa marina', 'WiFi, TV 50", Balcón, Aire Acondicionado, Vista Marina', 1),
    (@HotelCtgId, N'Junior Suite Marina', 'JR-MAR', 2, 4, 420000.00, N'Vista directa al mar caribe con terraza privada', 'WiFi, TV 55", Terraza, Minibar, Hamaca, Desayuno Incluido', 1),
    (@HotelCtgId, N'Suite Presidencial Colonial', 'STE-COL', 4, 6, 850000.00, N'Gran suite colonial con piscina privada y mayordomo', 'WiFi, Smart TV, Piscina Privada, Cava de Vinos, Servicio Exclusivo', 1);

    -- Sede Medellín
    INSERT INTO dbo.RoomTypes (HotelId, Name, Code, BaseCapacity, MaxCapacity, BasePrice, Description, Amenities, IsActive)
    VALUES 
    (@HotelMdeId, N'Estándar Urbana', 'STD-URB', 1, 2, 210000.00, N'Diseño vanguardista con luz natural en El Poblado', 'WiFi 150Mbps, TV 50", Ducha de Lluvia, Blackout, Caja Fuerte', 1),
    (@HotelMdeId, N'Suite Montaña', 'STE-MTN', 2, 4, 380000.00, N'Suite con ventanales de piso a techo y vista a las montañas', 'WiFi 200Mbps, TV 65", Jacuzzi en Terraza, Minibar Gourmet', 1);

    -- Sede Cali
    INSERT INTO dbo.RoomTypes (HotelId, Name, Code, BaseCapacity, MaxCapacity, BasePrice, Description, Amenities, IsActive)
    VALUES 
    (@HotelCloId, N'Estándar Brisas', 'STD-BRI', 1, 2, 175000.00, N'Habitación acogedora y fresca en el tradicional barrio Granada', 'WiFi, TV 43", Aire Acondicionado, Escritorio, Cafetera', 1),
    (@HotelCloId, N'Suite Sultana', 'STE-SUL', 2, 4, 320000.00, N'Amplia suite con sala independiente y baño de mármol', 'WiFi, TV 55", Tina Hidromasaje, Sala de Estar, Minibar', 1);
END
GO

-- 5. HABITACIONES FÍSICAS (UNIDAD DE INVENTARIO)
DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @HotelCtgId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CTG-02');
DECLARE @HotelMdeId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'MDE-03');
DECLARE @HotelCloId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CLO-04');

DECLARE @BogStd INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelBogId AND Code = 'STD-SGL');
DECLARE @BogDbl INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelBogId AND Code = 'DBL-SUP');
DECLARE @BogSte INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelBogId AND Code = 'STE-EJE');

IF NOT EXISTS (SELECT 1 FROM dbo.Rooms WHERE HotelId = @HotelBogId)
BEGIN
    INSERT INTO dbo.Rooms (HotelId, RoomTypeId, RoomNumber, Floor, OperationalStatus, CleaningStatus, IsActive)
    VALUES 
    -- Piso 1
    (@HotelBogId, @BogStd, '101', 1, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogStd, '102', 1, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogDbl, '103', 1, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogDbl, '104', 1, 'Disponible', 'Sucia', 1),
    -- Piso 2
    (@HotelBogId, @BogStd, '201', 2, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogStd, '202', 2, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogDbl, '203', 2, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogDbl, '204', 2, 'Disponible', 'EnLimpieza', 1),
    -- Piso 3 Suites
    (@HotelBogId, @BogSte, '301', 3, 'Disponible', 'Limpia', 1),
    (@HotelBogId, @BogSte, '302', 3, 'Mantenimiento', 'EnRevision', 1);
END
GO

-- Habitaciones Cartagena
DECLARE @HotelCtgId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CTG-02');
DECLARE @CtgStd INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelCtgId AND Code = 'STD-PLY');
DECLARE @CtgJr  INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelCtgId AND Code = 'JR-MAR');
DECLARE @CtgSte INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelCtgId AND Code = 'STE-COL');

IF NOT EXISTS (SELECT 1 FROM dbo.Rooms WHERE HotelId = @HotelCtgId)
BEGIN
    INSERT INTO dbo.Rooms (HotelId, RoomTypeId, RoomNumber, Floor, OperationalStatus, CleaningStatus, IsActive)
    VALUES 
    (@HotelCtgId, @CtgStd, '101', 1, 'Disponible', 'Limpia', 1),
    (@HotelCtgId, @CtgStd, '102', 1, 'Disponible', 'Limpia', 1),
    (@HotelCtgId, @CtgJr,  '201', 2, 'Disponible', 'Limpia', 1),
    (@HotelCtgId, @CtgJr,  '202', 2, 'Disponible', 'Limpia', 1),
    (@HotelCtgId, @CtgSte, '301', 3, 'Disponible', 'Limpia', 1);
END
GO

-- Habitaciones Medellín
DECLARE @HotelMdeId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'MDE-03');
DECLARE @MdeStd INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelMdeId AND Code = 'STD-URB');
DECLARE @MdeSte INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelMdeId AND Code = 'STE-MTN');

IF NOT EXISTS (SELECT 1 FROM dbo.Rooms WHERE HotelId = @HotelMdeId)
BEGIN
    INSERT INTO dbo.Rooms (HotelId, RoomTypeId, RoomNumber, Floor, OperationalStatus, CleaningStatus, IsActive)
    VALUES 
    (@HotelMdeId, @MdeStd, '101', 1, 'Disponible', 'Limpia', 1),
    (@HotelMdeId, @MdeStd, '102', 1, 'Disponible', 'Limpia', 1),
    (@HotelMdeId, @MdeSte, '201', 2, 'Disponible', 'Limpia', 1),
    (@HotelMdeId, @MdeSte, '202', 2, 'Disponible', 'Limpia', 1);
END
GO

-- Habitaciones Cali
DECLARE @HotelCloId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CLO-04');
DECLARE @CloStd INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelCloId AND Code = 'STD-BRI');
DECLARE @CloSte INT = (SELECT RoomTypeId FROM dbo.RoomTypes WHERE HotelId = @HotelCloId AND Code = 'STE-SUL');

IF NOT EXISTS (SELECT 1 FROM dbo.Rooms WHERE HotelId = @HotelCloId)
BEGIN
    INSERT INTO dbo.Rooms (HotelId, RoomTypeId, RoomNumber, Floor, OperationalStatus, CleaningStatus, IsActive)
    VALUES 
    (@HotelCloId, @CloStd, '101', 1, 'Disponible', 'Limpia', 1),
    (@HotelCloId, @CloStd, '102', 1, 'Disponible', 'Limpia', 1),
    (@HotelCloId, @CloSte, '201', 2, 'Disponible', 'Limpia', 1);
END
GO

-- 6. TEMPORADAS, PROMOCIONES E IMPUESTOS POR HOTEL
DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @HotelCtgId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CTG-02');
DECLARE @HotelMdeId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'MDE-03');
DECLARE @HotelCloId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'CLO-04');

IF NOT EXISTS (SELECT 1 FROM dbo.Seasons)
BEGIN
    INSERT INTO dbo.Seasons (HotelId, Name, StartDate, EndDate, PriceMultiplier, IsActive)
    VALUES 
    (@HotelBogId, N'Temporada Alta Fin de Año', '2026-12-15', '2027-01-20', 1.35, 1),
    (@HotelBogId, N'Temporada Fiestas de Agosto', '2026-08-01', '2026-08-25', 1.20, 1),
    (@HotelCtgId, N'Temporada Vacaciones de Verano', '2026-06-15', '2026-08-15', 1.45, 1),
    (@HotelCtgId, N'Temporada Alta de Navidad y Reyes', '2026-12-10', '2027-01-30', 1.60, 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Promotions)
BEGIN
    INSERT INTO dbo.Promotions (HotelId, Code, Name, DiscountPercent, StartDate, EndDate, IsActive)
    SELECT h.HotelId, 'PROMO10', N'Descuento Apertura 10%', 10.00, '2026-01-01', '2027-12-31', 1
    FROM dbo.Hotels h;

    INSERT INTO dbo.Promotions (HotelId, Code, Name, DiscountPercent, StartDate, EndDate, IsActive)
    SELECT h.HotelId, 'BIENVENIDA20', N'Bienvenida Clientes Frecuentes 20%', 20.00, '2026-01-01', '2027-12-31', 1
    FROM dbo.Hotels h;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.HotelTaxes)
BEGIN
    INSERT INTO dbo.HotelTaxes (HotelId, Name, Percentage, IsActive)
    SELECT h.HotelId, N'IVA Alojamiento', 19.00, 1 FROM dbo.Hotels h;

    INSERT INTO dbo.HotelTaxes (HotelId, Name, Percentage, IsActive)
    SELECT h.HotelId, N'Impuesto Nacional al Consumo', 8.00, 1 FROM dbo.Hotels h;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.CancellationPolicies)
BEGIN
    INSERT INTO dbo.CancellationPolicies (HotelId, Name, FreeCancellationHours, PenaltyPercent, Description, IsActive)
    SELECT h.HotelId, N'Política Flexible', 48, 0.00, N'Cancelación sin penalidad hasta 48 horas antes del check-in. Posterior a este plazo penalidad del 100% de la primera noche.', 1
    FROM dbo.Hotels h;

    INSERT INTO dbo.CancellationPolicies (HotelId, Name, FreeCancellationHours, PenaltyPercent, Description, IsActive)
    SELECT h.HotelId, N'Política Moderada', 72, 50.00, N'Cancelación gratuita hasta 72h antes. Después de 72h se cobra el 50% de la reserva.', 1
    FROM dbo.Hotels h;

    INSERT INTO dbo.CancellationPolicies (HotelId, Name, FreeCancellationHours, PenaltyPercent, Description, IsActive)
    SELECT h.HotelId, N'Política No Reembolsable', 0, 100.00, N'Tarifa especial con 100% de penalidad en caso de cancelación o no-show.', 1
    FROM dbo.Hotels h;
END
GO

-- 7. CATÁLOGO DE SERVICIOS ADICIONALES
IF NOT EXISTS (SELECT 1 FROM dbo.AdditionalServices)
BEGIN
    INSERT INTO dbo.AdditionalServices (HotelId, Name, Category, Price, IsActive)
    SELECT h.HotelId, N'Desayuno Buffet Gourmet', N'Desayuno', 35000.00, 1 FROM dbo.Hotels h UNION ALL
    SELECT h.HotelId, N'Sesión de Spa y Masaje Relajante', N'Spa', 120000.00, 1 FROM dbo.Hotels h UNION ALL
    SELECT h.HotelId, N'Parqueadero Cubierto 24 Horas', N'Parqueadero', 25000.00, 1 FROM dbo.Hotels h UNION ALL
    SELECT h.HotelId, N'Botella de Vino Reserva', N'Minibar', 95000.00, 1 FROM dbo.Hotels h UNION ALL
    SELECT h.HotelId, N'Servicio de Lavandería Express', N'Lavandería', 40000.00, 1 FROM dbo.Hotels h UNION ALL
    SELECT h.HotelId, N'Traslado Privado Aeropuerto', N'Traslado', 80000.00, 1 FROM dbo.Hotels h;
END
GO

-- 8. HUÉSPEDES DE EJEMPLO Y CONSENTIMIENTOS
DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @Guest1Id INT, @Guest2Id INT, @Guest3Id INT;

IF NOT EXISTS (SELECT 1 FROM dbo.Guests WHERE HotelId = @HotelBogId)
BEGIN
    INSERT INTO dbo.Guests (HotelId, DocumentType, DocumentNumber, FirstName, LastName, Email, Phone, Nationality, IsAnonymized)
    VALUES 
    (@HotelBogId, 'CC', '1020304050', N'Mauricio', N'Navarro Peña', 'mauricio.navarro@gmail.com', '+57 3105551234', 'Colombia', 0),
    (@HotelBogId, 'CC', '1030405060', N'Diana Marcela', N'Duque Morales', 'diana.duque@outlook.com', '+57 3158884321', 'Colombia', 0),
    (@HotelBogId, 'Pasaporte', 'PA98765432', N'John', N'Smith', 'john.smith@consulting.com', '+1 415 890 1234', 'Estados Unidos', 0);

    SET @Guest1Id = (SELECT GuestId FROM dbo.Guests WHERE HotelId = @HotelBogId AND DocumentNumber = '1020304050');
    SET @Guest2Id = (SELECT GuestId FROM dbo.Guests WHERE HotelId = @HotelBogId AND DocumentNumber = '1030405060');
    SET @Guest3Id = (SELECT GuestId FROM dbo.Guests WHERE HotelId = @HotelBogId AND DocumentNumber = 'PA98765432');

    -- Registro de Consentimientos Habeas Data (RF-45)
    INSERT INTO dbo.DataConsents (GuestId, Accepted, AcceptedAt, IpAddress, PolicyVersion)
    VALUES 
    (@Guest1Id, 1, SYSUTCDATETIME(), '190.144.10.25', '1.0'),
    (@Guest2Id, 1, SYSUTCDATETIME(), '186.84.21.112', '1.0'),
    (@Guest3Id, 1, SYSUTCDATETIME(), '200.75.50.88', '1.0');
END
GO

-- 9. RESERVAS INICIALES EN DIVERSOS ESTADOS
DECLARE @HotelBogId INT = (SELECT HotelId FROM dbo.Hotels WHERE Code = 'BOG-01');
DECLARE @Room101 INT = (SELECT RoomId FROM dbo.Rooms WHERE HotelId = @HotelBogId AND RoomNumber = '101');
DECLARE @Room102 INT = (SELECT RoomId FROM dbo.Rooms WHERE HotelId = @HotelBogId AND RoomNumber = '102');
DECLARE @Room103 INT = (SELECT RoomId FROM dbo.Rooms WHERE HotelId = @HotelBogId AND RoomNumber = '103');
DECLARE @Guest1 INT = (SELECT TOP 1 GuestId FROM dbo.Guests WHERE HotelId = @HotelBogId ORDER BY GuestId ASC);
DECLARE @Guest2 INT = (SELECT TOP 1 GuestId FROM dbo.Guests WHERE HotelId = @HotelBogId ORDER BY GuestId DESC);
DECLARE @PolicyFlex INT = (SELECT TOP 1 PolicyId FROM dbo.CancellationPolicies WHERE HotelId = @HotelBogId AND PenaltyPercent = 0.00);
DECLARE @AdminUser INT = (SELECT TOP 1 UserId FROM dbo.Users WHERE Email = 'recepcion.bogota@hotelchain.com');

IF NOT EXISTS (SELECT 1 FROM dbo.Reservations WHERE HotelId = @HotelBogId)
BEGIN
    -- Reserva 1: CheckIn activo en Room 101
    INSERT INTO dbo.Reservations (HotelId, RoomId, GuestId, CancellationPolicyId, Code, CheckInDate, CheckOutDate, AdultCount, ChildCount, Status, BaseRate, DiscountAmount, TaxAmount, TotalAmount, SpecialRequests)
    VALUES (@HotelBogId, @Room101, @Guest1, @PolicyFlex, 'RES-BOG-2026-001', '2026-10-14', '2026-10-17', 1, 0, 'CheckIn', 540000.00, 0.00, 102600.00, 642600.00, N'Piso alto, no fumador');

    DECLARE @Res1Id INT = SCOPE_IDENTITY();
    UPDATE dbo.Rooms SET OperationalStatus = 'Ocupada' WHERE RoomId = @Room101;

    -- Historial de Reserva 1
    INSERT INTO dbo.ReservationStateHistories (ReservationId, PreviousStatus, NewStatus, ChangedByUserId, ChangedAt, Reason)
    VALUES 
    (@Res1Id, 'None', 'Pendiente', @AdminUser, '2026-10-10 10:00:00', N'Creación inicial de reserva'),
    (@Res1Id, 'Pendiente', 'Confirmada', @AdminUser, '2026-10-10 11:30:00', N'Confirmación con pago de garantía'),
    (@Res1Id, 'Confirmada', 'CheckIn', @AdminUser, '2026-10-14 15:00:00', N'Ingreso de huésped con habitación limpia');

    -- Folio de Reserva 1
    INSERT INTO dbo.Folios (ReservationId, HotelId, TotalCharges, TotalTaxes, TotalPayments, Balance, Status)
    VALUES (@Res1Id, @HotelBogId, 642600.00 + 35000.00, 102600.00 + 6650.00, 300000.00, (642600.00 + 35000.00 + 6650.00) - 300000.00, 'Abierto');

    DECLARE @Folio1Id INT = SCOPE_IDENTITY();

    -- Cargos al Folio 1
    INSERT INTO dbo.FolioCharges (FolioId, ServiceId, Description, Quantity, UnitPrice, Amount, TaxAmount, IsAnnulled, CreatedByUserId)
    VALUES 
    (@Folio1Id, NULL, N'Alojamiento 3 Noches Estándar Sencilla', 3, 180000.00, 540000.00, 102600.00, 0, @AdminUser),
    (@Folio1Id, (SELECT TOP 1 ServiceId FROM dbo.AdditionalServices WHERE HotelId = @HotelBogId AND Category = 'Desayuno'), N'Desayuno Buffet Gourmet', 1, 35000.00, 35000.00, 6650.00, 0, @AdminUser);

    -- Pago inicial al Folio 1
    INSERT INTO dbo.FolioPayments (FolioId, CashShiftId, Amount, PaymentMethod, Reference, ReceiptNumber, CreatedByUserId)
    VALUES (@Folio1Id, NULL, 300000.00, 'TarjetaCredito', 'AUTH-892102', 'REC-2026-0001', @AdminUser);

    -- Reserva 2: Confirmada a futuro en Room 102
    INSERT INTO dbo.Reservations (HotelId, RoomId, GuestId, CancellationPolicyId, Code, CheckInDate, CheckOutDate, AdultCount, ChildCount, Status, BaseRate, DiscountAmount, TaxAmount, TotalAmount, SpecialRequests)
    VALUES (@HotelBogId, @Room102, @Guest2, @PolicyFlex, 'RES-BOG-2026-002', '2026-10-20', '2026-10-23', 2, 0, 'Confirmada', 780000.00, 78000.00, 133380.00, 835380.00, N'Cama adicional para niño');

    DECLARE @Res2Id INT = SCOPE_IDENTITY();

    INSERT INTO dbo.ReservationStateHistories (ReservationId, PreviousStatus, NewStatus, ChangedByUserId, ChangedAt, Reason)
    VALUES 
    (@Res2Id, 'None', 'Pendiente', @AdminUser, '2026-10-12 09:00:00', N'Creación de cotización'),
    (@Res2Id, 'Pendiente', 'Confirmada', @AdminUser, '2026-10-12 09:15:00', N'Confirmada con código PROMO10');

    -- Folio de Reserva 2
    INSERT INTO dbo.Folios (ReservationId, HotelId, TotalCharges, TotalTaxes, TotalPayments, Balance, Status)
    VALUES (@Res2Id, @HotelBogId, 702000.00, 133380.00, 835380.00, 0.00, 'Abierto');

    DECLARE @Folio2Id INT = SCOPE_IDENTITY();
    INSERT INTO dbo.FolioCharges (FolioId, ServiceId, Description, Quantity, UnitPrice, Amount, TaxAmount, IsAnnulled, CreatedByUserId)
    VALUES (@Folio2Id, NULL, N'Alojamiento 3 Noches Doble Superior (Promo 10%)', 3, 234000.00, 702000.00, 133380.00, 0, @AdminUser);

    INSERT INTO dbo.FolioPayments (FolioId, CashShiftId, Amount, PaymentMethod, Reference, ReceiptNumber, CreatedByUserId)
    VALUES (@Folio2Id, NULL, 835380.00, 'Transferencia', 'TRF-BANCOLOMBIA-4401', 'REC-2026-0002', @AdminUser);
END
GO

PRINT 'Datos semilla insertados correctamente para las 4 sedes de la cadena hotelera.';
GO
