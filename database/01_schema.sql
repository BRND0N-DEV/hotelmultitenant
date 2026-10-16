-- ==============================================================================
-- SISTEMA DE GESTIÓN HOTELERA INTEGRAL MULTITENANT (PMS)
-- SCRIPT DDL: CREACIÓN DE BASE DE DATOS Y ESQUEMA RELACIONAL
-- MOTOR: MICROSOFT SQL SERVER
-- ==============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'HotelManagementDb')
BEGIN
    CREATE DATABASE HotelManagementDb;
END
GO

USE HotelManagementDb;
GO

-- 1. TABLA: HOTELES (SEDES DE LA CADENA)
IF OBJECT_ID(N'dbo.Hotels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Hotels (
        HotelId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Hotels PRIMARY KEY,
        Code NVARCHAR(20) NOT NULL CONSTRAINT UQ_Hotels_Code UNIQUE,
        Name NVARCHAR(150) NOT NULL,
        City NVARCHAR(100) NOT NULL,
        Address NVARCHAR(250) NOT NULL,
        TimeZone NVARCHAR(50) NOT NULL CONSTRAINT DF_Hotels_TimeZone DEFAULT 'America/Bogota',
        Currency NVARCHAR(10) NOT NULL CONSTRAINT DF_Hotels_Currency DEFAULT 'COP',
        PrimaryColor NVARCHAR(20) NOT NULL CONSTRAINT DF_Hotels_PrimaryColor DEFAULT '#17365D',
        SecondaryColor NVARCHAR(20) NOT NULL CONSTRAINT DF_Hotels_SecondaryColor DEFAULT '#D9EAF7',
        LogoUrl NVARCHAR(500) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Hotels_IsActive DEFAULT 1,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Hotels_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 2. TABLA: USUARIOS Y EMPLEADOS
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        UserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Email NVARCHAR(150) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
        PasswordHash NVARCHAR(255) NOT NULL,
        FullName NVARCHAR(150) NOT NULL,
        Role NVARCHAR(50) NOT NULL, -- AdminGlobal, AdminSede, Recepcionista, Housekeeping, Auditor, Socio
        Department NVARCHAR(100) NULL,
        Shift NVARCHAR(50) NULL, -- Mañana, Tarde, Noche
        FailedLoginAttempts INT NOT NULL CONSTRAINT DF_Users_FailedAttempts DEFAULT 0,
        LockoutEnd DATETIME2(7) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
        RowVersion ROWVERSION NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

-- 3. TABLA: ASIGNACIÓN DE USUARIOS A HOTELES (MULTITENANT N:M)
IF OBJECT_ID(N'dbo.HotelUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HotelUsers (
        HotelUserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HotelUsers PRIMARY KEY,
        UserId INT NOT NULL CONSTRAINT FK_HotelUsers_Users FOREIGN KEY REFERENCES dbo.Users(UserId) ON DELETE CASCADE,
        HotelId INT NOT NULL CONSTRAINT FK_HotelUsers_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId) ON DELETE CASCADE,
        IsDefault BIT NOT NULL CONSTRAINT DF_HotelUsers_IsDefault DEFAULT 0,
        CONSTRAINT UQ_HotelUsers_User_Hotel UNIQUE (UserId, HotelId)
    );
END
GO

-- 4. TABLA: REFRESH TOKENS (SESIÓN SEGURA Y ROTACIÓN)
IF OBJECT_ID(N'dbo.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshTokens (
        RefreshTokenId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
        UserId INT NOT NULL CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY REFERENCES dbo.Users(UserId) ON DELETE CASCADE,
        Token NVARCHAR(255) NOT NULL CONSTRAINT UQ_RefreshTokens_Token UNIQUE,
        ExpiresAt DATETIME2(7) NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT SYSUTCDATETIME(),
        RevokedAt DATETIME2(7) NULL,
        ReplacedByToken NVARCHAR(255) NULL
    );
    CREATE INDEX IX_RefreshTokens_UserId ON dbo.RefreshTokens(UserId);
END
GO

-- 5. TABLA: TIPOS DE HABITACIÓN POR HOTEL
IF OBJECT_ID(N'dbo.RoomTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RoomTypes (
        RoomTypeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoomTypes PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_RoomTypes_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        Name NVARCHAR(100) NOT NULL,
        Code NVARCHAR(20) NOT NULL,
        BaseCapacity INT NOT NULL,
        MaxCapacity INT NOT NULL,
        BasePrice DECIMAL(18,2) NOT NULL,
        Description NVARCHAR(500) NULL,
        Amenities NVARCHAR(500) NULL, -- Coma separada (WiFi, TV 4K, Jacuzzi, Vista al mar, Minibar)
        IsActive BIT NOT NULL CONSTRAINT DF_RoomTypes_IsActive DEFAULT 1,
        CONSTRAINT UQ_RoomTypes_Hotel_Code UNIQUE (HotelId, Code)
    );
    CREATE INDEX IX_RoomTypes_HotelId ON dbo.RoomTypes(HotelId);
END
GO

-- 6. TABLA: HABITACIONES FÍSICAS (UNIDAD DE INVENTARIO)
IF OBJECT_ID(N'dbo.Rooms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rooms (
        RoomId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Rooms PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_Rooms_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        RoomTypeId INT NOT NULL CONSTRAINT FK_Rooms_RoomTypes FOREIGN KEY REFERENCES dbo.RoomTypes(RoomTypeId),
        RoomNumber NVARCHAR(20) NOT NULL,
        Floor INT NOT NULL,
        OperationalStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Rooms_OpStatus DEFAULT 'Disponible', -- Disponible, Ocupada, FueraDeServicio, Mantenimiento
        CleaningStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Rooms_CleanStatus DEFAULT 'Limpia', -- Limpia, Sucia, EnLimpieza, EnRevision
        Notes NVARCHAR(500) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Rooms_IsActive DEFAULT 1,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_Rooms_Hotel_RoomNumber UNIQUE (HotelId, RoomNumber)
    );
    CREATE INDEX IX_Rooms_Hotel_Status ON dbo.Rooms(HotelId, OperationalStatus, CleaningStatus);
END
GO

-- 7. TABLA: TEMPORADAS TARIFARIAS
IF OBJECT_ID(N'dbo.Seasons', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Seasons (
        SeasonId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Seasons PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_Seasons_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        Name NVARCHAR(100) NOT NULL,
        StartDate DATE NOT NULL,
        EndDate DATE NOT NULL,
        PriceMultiplier DECIMAL(5,2) NOT NULL CONSTRAINT DF_Seasons_Multiplier DEFAULT 1.00,
        IsActive BIT NOT NULL CONSTRAINT DF_Seasons_IsActive DEFAULT 1,
        CONSTRAINT CK_Seasons_Dates CHECK (EndDate >= StartDate)
    );
    CREATE INDEX IX_Seasons_Hotel_Dates ON dbo.Seasons(HotelId, StartDate, EndDate);
END
GO

-- 8. TABLA: PROMOCIONES Y DESCUENTOS
IF OBJECT_ID(N'dbo.Promotions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Promotions (
        PromotionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Promotions PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_Promotions_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        DiscountPercent DECIMAL(5,2) NOT NULL,
        StartDate DATE NOT NULL,
        EndDate DATE NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Promotions_IsActive DEFAULT 1,
        CONSTRAINT UQ_Promotions_Hotel_Code UNIQUE (HotelId, Code),
        CONSTRAINT CK_Promotions_Dates CHECK (EndDate >= StartDate)
    );
END
GO

-- 9. TABLA: IMPUESTOS POR HOTEL
IF OBJECT_ID(N'dbo.HotelTaxes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HotelTaxes (
        TaxId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HotelTaxes PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_HotelTaxes_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        Name NVARCHAR(100) NOT NULL,
        Percentage DECIMAL(5,2) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HotelTaxes_IsActive DEFAULT 1
    );
END
GO

-- 10. TABLA: POLÍTICAS DE CANCELACIÓN
IF OBJECT_ID(N'dbo.CancellationPolicies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CancellationPolicies (
        PolicyId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CancellationPolicies PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_CancellationPolicies_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        Name NVARCHAR(100) NOT NULL,
        FreeCancellationHours INT NOT NULL CONSTRAINT DF_CancelPolicies_Hours DEFAULT 48,
        PenaltyPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_CancelPolicies_Penalty DEFAULT 0.00,
        Description NVARCHAR(500) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_CancelPolicies_IsActive DEFAULT 1
    );
END
GO

-- 11. TABLA: CATÁLOGO DE SERVICIOS ADICIONALES
IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdditionalServices (
        ServiceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdditionalServices PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_AdditionalServices_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        Name NVARCHAR(150) NOT NULL,
        Category NVARCHAR(50) NOT NULL, -- Desayuno, Spa, Parqueadero, Minibar, Lavandería, Restaurante, Traslado
        Price DECIMAL(18,2) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_AdditionalServices_IsActive DEFAULT 1
    );
    CREATE INDEX IX_AdditionalServices_Hotel_Category ON dbo.AdditionalServices(HotelId, Category);
END
GO

-- 12. TABLA: CIERRES DE CAJA POR TURNO
IF OBJECT_ID(N'dbo.CashShifts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CashShifts (
        CashShiftId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashShifts PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_CashShifts_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        UserId INT NOT NULL CONSTRAINT FK_CashShifts_Users FOREIGN KEY REFERENCES dbo.Users(UserId),
        OpenedAt DATETIME2(7) NOT NULL CONSTRAINT DF_CashShifts_OpenedAt DEFAULT SYSUTCDATETIME(),
        ClosedAt DATETIME2(7) NULL,
        InitialAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_CashShifts_InitialAmount DEFAULT 0.00,
        TotalCash DECIMAL(18,2) NOT NULL CONSTRAINT DF_CashShifts_TotalCash DEFAULT 0.00,
        TotalCard DECIMAL(18,2) NOT NULL CONSTRAINT DF_CashShifts_TotalCard DEFAULT 0.00,
        TotalTransfer DECIMAL(18,2) NOT NULL CONSTRAINT DF_CashShifts_TotalTransfer DEFAULT 0.00,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_CashShifts_Status DEFAULT 'Abierto', -- Abierto, Cerrado
        Notes NVARCHAR(500) NULL
    );
    CREATE INDEX IX_CashShifts_Hotel_Status ON dbo.CashShifts(HotelId, Status);
END
GO

-- 13. TABLA: HUÉSPEDES
IF OBJECT_ID(N'dbo.Guests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Guests (
        GuestId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Guests PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_Guests_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        DocumentType NVARCHAR(20) NOT NULL, -- CC, CE, Pasaporte, TI, NIT
        DocumentNumber NVARCHAR(50) NOT NULL,
        FirstName NVARCHAR(100) NOT NULL,
        LastName NVARCHAR(100) NOT NULL,
        Email NVARCHAR(150) NOT NULL,
        Phone NVARCHAR(50) NOT NULL,
        Nationality NVARCHAR(100) NOT NULL CONSTRAINT DF_Guests_Nationality DEFAULT 'Colombia',
        IsAnonymized BIT NOT NULL CONSTRAINT DF_Guests_IsAnonymized DEFAULT 0,
        AnonymizedAt DATETIME2(7) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Guests_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Guests_Hotel_Document UNIQUE (HotelId, DocumentType, DocumentNumber)
    );
    CREATE INDEX IX_Guests_Hotel_Doc ON dbo.Guests(HotelId, DocumentNumber);
END
GO

-- 14. TABLA: CONSENTIMIENTOS DE DATOS (HABEAS DATA)
IF OBJECT_ID(N'dbo.DataConsents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataConsents (
        ConsentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataConsents PRIMARY KEY,
        GuestId INT NOT NULL CONSTRAINT FK_DataConsents_Guests FOREIGN KEY REFERENCES dbo.Guests(GuestId) ON DELETE CASCADE,
        Accepted BIT NOT NULL,
        AcceptedAt DATETIME2(7) NOT NULL CONSTRAINT DF_DataConsents_AcceptedAt DEFAULT SYSUTCDATETIME(),
        IpAddress NVARCHAR(50) NOT NULL,
        PolicyVersion NVARCHAR(20) NOT NULL CONSTRAINT DF_DataConsents_Version DEFAULT '1.0'
    );
    CREATE INDEX IX_DataConsents_GuestId ON dbo.DataConsents(GuestId);
END
GO

-- 15. TABLA: RESERVAS
IF OBJECT_ID(N'dbo.Reservations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Reservations (
        ReservationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reservations PRIMARY KEY,
        HotelId INT NOT NULL CONSTRAINT FK_Reservations_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        RoomId INT NOT NULL CONSTRAINT FK_Reservations_Rooms FOREIGN KEY REFERENCES dbo.Rooms(RoomId),
        GuestId INT NOT NULL CONSTRAINT FK_Reservations_Guests FOREIGN KEY REFERENCES dbo.Guests(GuestId),
        CancellationPolicyId INT NOT NULL CONSTRAINT FK_Reservations_CancelPolicy FOREIGN KEY REFERENCES dbo.CancellationPolicies(PolicyId),
        Code NVARCHAR(30) NOT NULL CONSTRAINT UQ_Reservations_Code UNIQUE,
        CheckInDate DATE NOT NULL,
        CheckOutDate DATE NOT NULL,
        AdultCount INT NOT NULL CONSTRAINT DF_Reservations_Adults DEFAULT 1,
        ChildCount INT NOT NULL CONSTRAINT DF_Reservations_Children DEFAULT 0,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Reservations_Status DEFAULT 'Pendiente', -- Pendiente, Confirmada, CheckIn, CheckOut, Liquidada, Anulada, NoShow
        BaseRate DECIMAL(18,2) NOT NULL,
        DiscountAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Reservations_Discount DEFAULT 0.00,
        TaxAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Reservations_Tax DEFAULT 0.00,
        TotalAmount DECIMAL(18,2) NOT NULL,
        PenaltyAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Reservations_Penalty DEFAULT 0.00,
        SpecialRequests NVARCHAR(500) NULL,
        RowVersion ROWVERSION NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Reservations_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Reservations_Dates CHECK (CheckOutDate > CheckInDate)
    );
    CREATE INDEX IX_Reservations_Hotel_Dates_Status ON dbo.Reservations(HotelId, CheckInDate, CheckOutDate, Status);
    CREATE INDEX IX_Reservations_Room_Dates ON dbo.Reservations(RoomId, CheckInDate, CheckOutDate, Status);
END
GO

-- 16. TABLA: ACOMPAÑANTES DE RESERVA
IF OBJECT_ID(N'dbo.ReservationCompanions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReservationCompanions (
        CompanionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReservationCompanions PRIMARY KEY,
        ReservationId INT NOT NULL CONSTRAINT FK_ReservationCompanions_Reservations FOREIGN KEY REFERENCES dbo.Reservations(ReservationId) ON DELETE CASCADE,
        DocumentType NVARCHAR(20) NOT NULL,
        DocumentNumber NVARCHAR(50) NOT NULL,
        FullName NVARCHAR(150) NOT NULL,
        Relationship NVARCHAR(50) NULL
    );
    CREATE INDEX IX_ReservationCompanions_ResId ON dbo.ReservationCompanions(ReservationId);
END
GO

-- 17. TABLA: HISTORIAL DE TRANSICIONES DE ESTADO DE RESERVAS
IF OBJECT_ID(N'dbo.ReservationStateHistories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReservationStateHistories (
        HistoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReservationStateHistories PRIMARY KEY,
        ReservationId INT NOT NULL CONSTRAINT FK_ReservationStateHistories_Reservations FOREIGN KEY REFERENCES dbo.Reservations(ReservationId) ON DELETE CASCADE,
        PreviousStatus NVARCHAR(30) NOT NULL,
        NewStatus NVARCHAR(30) NOT NULL,
        ChangedByUserId INT NOT NULL CONSTRAINT FK_ReservationStateHistories_Users FOREIGN KEY REFERENCES dbo.Users(UserId),
        ChangedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ResStateHist_ChangedAt DEFAULT SYSUTCDATETIME(),
        Reason NVARCHAR(500) NULL
    );
    CREATE INDEX IX_ReservationStateHistories_ResId ON dbo.ReservationStateHistories(ReservationId);
END
GO

-- 18. TABLA: FOLIOS DE ESTANCIA
IF OBJECT_ID(N'dbo.Folios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Folios (
        FolioId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Folios PRIMARY KEY,
        ReservationId INT NOT NULL CONSTRAINT FK_Folios_Reservations FOREIGN KEY REFERENCES dbo.Reservations(ReservationId) ON DELETE CASCADE,
        HotelId INT NOT NULL CONSTRAINT FK_Folios_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        TotalCharges DECIMAL(18,2) NOT NULL CONSTRAINT DF_Folios_Charges DEFAULT 0.00,
        TotalTaxes DECIMAL(18,2) NOT NULL CONSTRAINT DF_Folios_Taxes DEFAULT 0.00,
        TotalPayments DECIMAL(18,2) NOT NULL CONSTRAINT DF_Folios_Payments DEFAULT 0.00,
        Balance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Folios_Balance DEFAULT 0.00,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Folios_Status DEFAULT 'Abierto', -- Abierto, Cerrado
        ClosedAt DATETIME2(7) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Folios_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX IX_Folios_ReservationId ON dbo.Folios(ReservationId);
    CREATE INDEX IX_Folios_Hotel_Status ON dbo.Folios(HotelId, Status);
END
GO

-- 19. TABLA: CARGOS Y CONSUMOS EN FOLIO (INMUTABLES)
IF OBJECT_ID(N'dbo.FolioCharges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FolioCharges (
        ChargeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FolioCharges PRIMARY KEY,
        FolioId INT NOT NULL CONSTRAINT FK_FolioCharges_Folios FOREIGN KEY REFERENCES dbo.Folios(FolioId) ON DELETE CASCADE,
        ServiceId INT NULL CONSTRAINT FK_FolioCharges_Services FOREIGN KEY REFERENCES dbo.AdditionalServices(ServiceId),
        Description NVARCHAR(250) NOT NULL,
        Quantity INT NOT NULL CONSTRAINT DF_FolioCharges_Quantity DEFAULT 1,
        UnitPrice DECIMAL(18,2) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        TaxAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_FolioCharges_Tax DEFAULT 0.00,
        IsAnnulled BIT NOT NULL CONSTRAINT DF_FolioCharges_IsAnnulled DEFAULT 0,
        AnnulledByChargeId INT NULL,
        AnnulmentReason NVARCHAR(500) NULL,
        CreatedByUserId INT NOT NULL CONSTRAINT FK_FolioCharges_Users FOREIGN KEY REFERENCES dbo.Users(UserId),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_FolioCharges_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_FolioCharges_FolioId ON dbo.FolioCharges(FolioId);
END
GO

-- 20. TABLA: PAGOS Y ABONOS EN FOLIO
IF OBJECT_ID(N'dbo.FolioPayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FolioPayments (
        PaymentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FolioPayments PRIMARY KEY,
        FolioId INT NOT NULL CONSTRAINT FK_FolioPayments_Folios FOREIGN KEY REFERENCES dbo.Folios(FolioId) ON DELETE CASCADE,
        CashShiftId INT NULL CONSTRAINT FK_FolioPayments_CashShifts FOREIGN KEY REFERENCES dbo.CashShifts(CashShiftId),
        Amount DECIMAL(18,2) NOT NULL,
        PaymentMethod NVARCHAR(50) NOT NULL, -- Efectivo, TarjetaDebito, TarjetaCredito, Transferencia
        Reference NVARCHAR(100) NOT NULL,
        ReceiptNumber NVARCHAR(50) NOT NULL,
        CreatedByUserId INT NOT NULL CONSTRAINT FK_FolioPayments_Users FOREIGN KEY REFERENCES dbo.Users(UserId),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_FolioPayments_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_FolioPayments_FolioId ON dbo.FolioPayments(FolioId);
    CREATE INDEX IX_FolioPayments_CashShiftId ON dbo.FolioPayments(CashShiftId);
END
GO

-- 21. TABLA: REGISTROS DE AUDITORÍA (3 NIVELES: NEGOCIO, TÉCNICO, HTTP)
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        AuditLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        HotelId INT NULL CONSTRAINT FK_AuditLogs_Hotels FOREIGN KEY REFERENCES dbo.Hotels(HotelId),
        UserId INT NULL CONSTRAINT FK_AuditLogs_Users FOREIGN KEY REFERENCES dbo.Users(UserId),
        Level NVARCHAR(20) NOT NULL, -- Negocio, Tecnico, Http
        Action NVARCHAR(100) NOT NULL, -- Insert, Update, Delete, Login, CrossHotelSearch, StateTransition, Anonymization
        EntityName NVARCHAR(100) NULL,
        EntityId NVARCHAR(100) NULL,
        OldValues NVARCHAR(MAX) NULL,
        NewValues NVARCHAR(MAX) NULL,
        Timestamp DATETIME2(7) NOT NULL CONSTRAINT DF_AuditLogs_Timestamp DEFAULT SYSUTCDATETIME(),
        IpAddress NVARCHAR(50) NULL,
        CorrelationId NVARCHAR(100) NULL
    );
    CREATE INDEX IX_AuditLogs_Hotel_Timestamp ON dbo.AuditLogs(HotelId, Timestamp);
    CREATE INDEX IX_AuditLogs_Entity ON dbo.AuditLogs(EntityName, EntityId);
END
GO

-- 22. TABLA: IDEMPOTENCIA DE PETICIONES
IF OBJECT_ID(N'dbo.IdempotencyKeys', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IdempotencyKeys (
        IdempotencyKeyId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IdempotencyKeys PRIMARY KEY,
        [Key] NVARCHAR(100) NOT NULL CONSTRAINT UQ_IdempotencyKeys_Key UNIQUE,
        Endpoint NVARCHAR(250) NOT NULL,
        ResponseCode INT NOT NULL,
        ResponseBody NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_IdempotencyKeys_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_IdempotencyKeys_Key ON dbo.IdempotencyKeys([Key]);
END
GO
