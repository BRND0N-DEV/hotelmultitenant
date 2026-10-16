# ANÁLISIS DE REQUISITOS Y ARQUITECTURA DEL SISTEMA
## Sistema de Gestión Hotelera Integral Multitenant (PMS)
**Basado en:** `Requisitos_Sistema_Gestion_Hotelera.tex`

---

## 1. Resumen Ejecutivo y Alcance
El sistema es una plataforma multitenant para una cadena hotelera compuesta por **cuatro hoteles asociados**. Su propósito es sustituir el control manual en hojas de cálculo por una solución centralizada, consistente y segura que garantice:
- Aislamiento lógico estricto por sede (multitenancy).
- Control de acceso basado en roles con permisos granulares.
- Gestión de inventario físico y tarifario (habitaciones, temporadas, fines de semana, promociones, impuestos, políticas de cancelación y servicios adicionales).
- Motor de disponibilidad y cotización en tiempo real.
- Prevención estricta de sobreventa (overbooking) en base de datos y control de concurrencia optimista en aplicación.
- Máquina de estados de reservas con auditoría completa de transiciones.
- Operación de recepción: check-in (validando limpieza y consentimiento de datos Habeas Data), consumos inmutables en folio con contra-asientos auditados, pagos con idempotencia, check-out (saldo cero obligatorio) y facturación/liquidación con PDF.
- Interfaz de housekeeping adaptable a tabletas para el personal de camarería.
- Cierre de turnos de caja con congelamiento de totales por método de pago.
- Privacidad de datos (Habeas Data) y derecho al olvido (anonimización irreversible contable).
- Dashboard consolidado multisede con métricas hoteleras clave (Ocupación, ADR, RevPAR, Ingresos, Cancelaciones).
- Exportación de reportes a Excel (.xlsx) y PDF, y auditoría en tres niveles (Negocio, Técnico y HTTP).

---

## 2. Actores y Matriz de Responsabilidades
| Actor | Rol en el Sistema | Alcance y Permisos |
|---|---|---|
| **Socio** | `Socio` | Consulta de solo lectura de indicadores consolidados de la cadena y de cada hotel en el Dashboard. |
| **Administrador Global** | `AdminGlobal` | Configuración de hoteles, parámetros globales, gestión de empleados y roles, anonimización irreversible de datos y auditoría global. |
| **Administrador de Sede** | `AdminSede` | Administración de la sede activa: habitaciones, empleados locales, catálogo de servicios adicionales, cierres de caja y auditoría local. |
| **Recepcionista** | `Recepcionista` | Cotización, disponibilidad, reservas con idempotencia, check-in, check-out, folios, consumos, contra-asientos, pagos y consulta de disponibilidad inter-sedes. |
| **Camarería / Supervisor** | `Housekeeping` | Consulta de habitaciones asignadas y actualización táctil de estados de limpieza (Limpia, Sucia, En Limpieza, Mantenimiento). |
| **Auditor** | `Auditor` | Consulta de registros de auditoría (negocio, técnico y HTTP) y exportación de reportes a Excel/PDF en modo solo lectura. |
| **Huésped** | `Huesped` (Sin login en v1) | Titular o acompañante en reservas; otorga consentimiento de datos y recibe notificaciones/vouchers/comprobantes con código QR. |

---

## 3. Catálogo de Requisitos Funcionales (RF-01 a RF-55)

### Acceso, Usuarios y Permisos
- **RF-01:** Inicio de sesión con correo y contraseña.
- **RF-02:** Sesiones con Access Token de 15 a 30 min y Refresh Token rotativo, con revocación de familia ante detección de reutilización.
- **RF-03:** Bloqueo temporal tras 5 intentos fallidos consecutivos con registro de auditoría.
- **RF-04:** Administrador Global gestiona empleados (roles, departamentos, turnos, hoteles asignados).
- **RF-05:** Selección y conmutación de contexto activo únicamente entre hoteles autorizados para el usuario.
- **RF-06:** Permisos granulares por rol y acción. Operación no autorizada dentro de la sede retorna HTTP 403.
- **RF-07:** Aislamiento total: consultar o modificar recursos de otra sede no autorizada retorna HTTP 404 (No Encontrado) para evitar divulgación de existencia.

### Parametrización de Hoteles y Servicios
- **RF-08:** Creación y configuración de hoteles (nombre, ciudad, dirección, zona horaria, moneda base COP, colores/logo).
- **RF-09:** Bloqueo de desactivación de hoteles con reservas futuras activas, informando la cantidad.
- **RF-10:** Definición de tipos de habitación, capacidad base y máxima, comodidades y habitaciones físicas por sede.
- **RF-11:** Unicidad del número de habitación por hotel (permitiendo duplicidad en hoteles distintos).
- **RF-12:** Bloqueo de desactivación de habitaciones con reservas futuras activas.
- **RF-13:** Configuración de tarifas base, temporadas, recargos de fin de semana y promociones con vigencia.
- **RF-14:** Validación para rechazar temporadas con fechas traslapadas en el mismo hotel.
- **RF-15:** Aplicación automática de promociones vigentes con visualización del descuento desglosado.
- **RF-16:** Configuración de impuestos y políticas de cancelación por sede (utilizando la política vigente al momento de reservar).
- **RF-17:** Catálogo de servicios adicionales por hotel (precio, categoría, activo/inactivo). Los servicios inactivos no pueden cargarse a folios.

### Disponibilidad y Reservas
- **RF-18:** Consulta de disponibilidad por fechas, tipo de habitación y huéspedes con desglose por noche y total.
- **RF-19:** Exclusión de habitaciones ocupadas por reservas activas que traslapen el rango (CheckIn < RangoFin Y CheckOut > RangoInicio).
- **RF-20:** Consulta inter-sedes autorizada que retorna únicamente: sede, tipo de habitación, cantidad disponible y tarifa referencial.
- **RF-21:** Consulta inter-sedes protegida: no expone reservas, huéspedes, números de habitación ni folios de otras sedes.
- **RF-22:** Creación de reserva en flujo unificado en estado `Pendiente`.
- **RF-23:** Soporte de cabecera `Idempotency-Key` en creación de reservas para evitar duplicados.
- **RF-24:** Prevención estricta de overbooking mediante garantía de integridad en base de datos SQL Server.
- **RF-25:** Detección de colisiones y ediciones concurrentes mediante RowVersion / ConcurrencyToken con HTTP 409.
- **RF-26:** Máquina de estados con transiciones permitidas: `Pendiente`, `Confirmada`, `CheckIn`, `CheckOut`, `Liquidada`, `Anulada`, `NoShow`. Transiciones inválidas retornan HTTP 422.
- **RF-27:** Registro histórico obligatorio de cada cambio de estado (estado anterior, nuevo, usuario, fecha, motivo).
- **RF-28:** Cancelación y No-Show aplicando política vigente al reservar, cálculo de penalidad y liberación de habitación.
- **RF-29:** Calendario de ocupación interactivo de hasta 14 días con diferenciación visual por color y texto.

### Recepción, Estancia, Folio y Pagos
- **RF-30:** Registro de titular y acompañantes con documento, nombres, contacto y nacionalidad.
- **RF-31:** Check-in bloqueado si la habitación no está `Limpia/Disponible` o si falta el consentimiento Habeas Data.
- **RF-32:** Transición de Check-in: reserva a `CheckIn`, habitación a `Ocupada`.
- **RF-33:** Housekeeping táctil para camarería y supervisores para consultar y actualizar estado de limpieza.
- **RF-34:** Transición de Check-out: habitación pasa automáticamente a estado `Sucia`.
- **RF-35:** Registro de consumos en el folio (servicio, cantidad, precio unitario, usuario, fecha/hora).
- **RF-36:** Inmutabilidad de cargos: correcciones exclusivas mediante contra-asiento de anulación auditado con motivo.
- **RF-37:** Vista de folio con detalle de consumos, impuestos, abonos/pagos y saldo neto pendiente.
- **RF-38:** Registro de abonos y pagos con método (Efectivo, Débito, Crédito, Transferencia) y referencia, emitiendo recibo.
- **RF-39:** Soporte de clave de idempotencia en pagos.
- **RF-40:** Check-out bloqueado si el saldo es distinto de cero.
- **RF-41:** Cierre de estancia con saldo cero, generación de factura/comprobante PDF y paso a `Liquidada`.
- **RF-42:** Cierre de caja por turno con congelamiento de importes por método de pago.

### Privacidad, Notificaciones y Auditoría
- **RF-43:** Notificaciones por correo con voucher, QR y comprobante con la identidad visual del hotel.
- **RF-44:** Envío asíncrono y desacoplado de correos con política de reintentos (hasta 3 veces) sin abortar transacciones.
- **RF-45:** Captura de consentimiento Habeas Data (fecha, hora, IP, versión de política).
- **RF-46:** Bloqueo de confirmación y check-in sin consentimiento firmado.
- **RF-47:** Anonimización irreversible de datos personales de huéspedes preservando folios y transacciones contables.
- **RF-48:** Portal de autogestión de huéspedes considerado fuera de alcance inicial (fase posterior HU-24).
- **RF-49:** Dashboard consolidado multihotel con filtro por sede.
- **RF-50:** Indicadores clave: Ingresos diarios/semanales/mensuales, Ocupación %, ADR, RevPAR, Cancelaciones.
- **RF-51:** Actualización automática del dashboard sin recarga completa.
- **RF-52:** Exportación de transacciones y huéspedes a Excel (.xlsx) y PDF.
- **RF-53:** Encabezados y metadatos profesionales en archivos exportados.
- **RF-54:** Consulta de auditoría con filtros por usuario, entidad, fecha y valores anterior/nuevo.
- **RF-55:** Auditoría en 3 niveles: Negocio, Técnico (cambios EF Core) y HTTP (solicitudes y respuestas).

---

## 4. Requisitos No Funcionales (RNF-01 a RNF-16)
- **RNF-01 (Rendimiento):** Consulta de disponibilidad p95 < 800 ms con índices en `(HotelId, RoomTypeId, RoomId)` y fechas.
- **RNF-02 (Concurrencia):** 50 usuarios concurrentes sin fallos de integridad ni sobreventa.
- **RNF-03 (Disponibilidad):** 99% de disponibilidad operativa con health checks en `/health`.
- **RNF-04 (Respaldo y Recuperación):** Procedimiento documentado y script de respaldo/restauración de base de datos.
- **RNF-05 (Seguridad OWASP):** Prevención de inyección SQL (consultas parametrizadas EF Core), XSS, CSRF, IDOR (verificación de HotelId) y control de autenticación.
- **RNF-06 (Accesibilidad):** Cumplimiento WCAG 2.1 AA en navegación por teclado, contrastes y aria-labels.
- **RNF-07 (Compatibilidad):** Navegadores modernos (Chrome, Edge, Firefox) y dispositivos móviles/tabletas.
- **RNF-08 (Mantenibilidad):** Clean Architecture con separación estricta, alta cohesión y pruebas automatizadas.
- **RNF-09 (Observabilidad):** Logs estructurados con Serilog y CorrelationId (`X-Correlation-Id`).
- **RNF-10 (Aislamiento Multitenant):** Filtrado obligatorio por HotelId en entidades operativas + verificación en capa de aplicación.
- **RNF-11 (Integridad de Reservas):** Constraint / Trigger de exclusión transaccional en SQL Server que impida reservas activas solapadas en la misma habitación.
- **RNF-12 (Seguridad de Sesión):** Tokens JWT de 15 min, Refresh tokens almacenados de forma segura con rotación y detección de reutilización.
- **RNF-13 (Protección de Contraseñas):** BCrypt / Argon2id con salt criptográfico; nunca en texto plano ni en logs.
- **RNF-14 (Interfaz Adaptable):** Responsive 1920x1080, 1366x768 y tabletas táctiles (10").
- **RNF-15 (Usabilidad y Validación):** Mensajes reactivos claros, validaciones inline y debounce de 300 ms en búsquedas.
- **RNF-16 (Trazabilidad):** Borrado lógico (`IsDeleted`), auditoría de cambios y estampas temporales UTC.

---

## 5. Diseño del Modelo de Base de Datos Relacional (SQL Server)
```
Hotels (HotelId, Code, Name, City, Address, TimeZone, Currency, PrimaryColor, SecondaryColor, LogoUrl, IsActive, CreatedAt)
├── HotelUsers (HotelUserId, UserId, HotelId, IsDefault)
├── RoomTypes (RoomTypeId, HotelId, Name, Code, BaseCapacity, MaxCapacity, BasePrice, Description, Amenities, IsActive)
├── Rooms (RoomId, HotelId, RoomTypeId, RoomNumber, Floor, OperationalStatus, CleaningStatus, RowVersion, IsActive)
├── Seasons (SeasonId, HotelId, Name, StartDate, EndDate, Multiplier, IsActive)
├── Promotions (PromotionId, HotelId, Code, Name, DiscountPercent, StartDate, EndDate, IsActive)
├── HotelTaxes (TaxId, HotelId, Name, Percentage, IsActive)
├── CancellationPolicies (PolicyId, HotelId, Name, FreeCancellationHours, PenaltyPercent, Description, IsActive)
├── AdditionalServices (ServiceId, HotelId, Name, Category, Price, IsActive)
├── CashShifts (CashShiftId, HotelId, UserId, OpenedAt, ClosedAt, InitialAmount, TotalCash, TotalCard, TotalTransfer, Status)
├── Guests (GuestId, HotelId, DocumentType, DocumentNumber, FirstName, LastName, Email, Phone, Nationality, IsAnonymized, CreatedAt)
│   └── DataConsents (ConsentId, GuestId, Accepted, AcceptedAt, IpAddress, PolicyVersion)
├── Reservations (ReservationId, HotelId, RoomId, GuestId, Code, CheckInDate, CheckOutDate, AdultCount, ChildCount, Status,
│                 BaseRate, DiscountAmount, TaxAmount, TotalAmount, CancellationPolicyId, RowVersion, CreatedAt)
│   ├── ReservationCompanions (CompanionId, ReservationId, DocumentType, DocumentNumber, FullName, Relationship)
│   ├── ReservationStateHistories (HistoryId, ReservationId, PreviousStatus, NewStatus, ChangedByUserId, ChangedAt, Reason)
│   └── Folios (FolioId, ReservationId, HotelId, TotalCharges, TotalTaxes, TotalPayments, Balance, Status, ClosedAt)
│       ├── FolioCharges (ChargeId, FolioId, ServiceId, Description, Quantity, UnitPrice, Amount, TaxAmount, IsAnnulled,
│                         AnnulledByChargeId, AnnulmentReason, CreatedByUserId, CreatedAt)
│       └── FolioPayments (PaymentId, FolioId, CashShiftId, Amount, PaymentMethod, Reference, ReceiptNumber, CreatedByUserId, CreatedAt)
├── Users (UserId, Email, PasswordHash, FullName, Role, Department, Shift, FailedLoginAttempts, LockoutEnd, IsActive, RowVersion)
│   └── RefreshTokens (RefreshTokenId, UserId, Token, ExpiresAt, CreatedAt, RevokedAt, ReplacedByToken)
├── AuditLogs (AuditLogId, HotelId, UserId, Level, Action, EntityName, EntityId, OldValues, NewValues, Timestamp, IpAddress, CorrelationId)
└── IdempotencyKeys (IdempotencyKeyId, Key, Endpoint, ResponseCode, ResponseBody, CreatedAt)
```
