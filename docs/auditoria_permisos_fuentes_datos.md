# CONTINENTALIA SAS — AUDITORÍA INTEGRAL DE MATRIZ DE PERMISOS, AUTORIZACIÓN (RBAC) Y TRAZABILIDAD DE FUENTES DE DATOS

**Empresa / Plataforma:** CONTINENTALIA SAS  
**Documento Técnico:** Informe de Seguridad, Autorización, Persistencia y Trazabilidad de Datos  
**Versión:** 2.5 — Octubre 2026  
**Clasificación:** Confidencial / Auditoría de Seguridad & Arquitectura  
**Estado de la Matriz:** Propuesta para Validación de la Junta Directiva  

---

## 1. Resumen Ejecutivo y Alcance

El presente documento constituye la auditoría exhaustiva de seguridad, control de acceso basado en roles (**RBAC**), autorización efectiva frontend/backend y trazabilidad de persistencia en base de datos (**SQL Server**) para la plataforma hotelera multitenant **CONTINENTALIA SAS**.

La auditoría ha verificado:
1. La correspondencia entre los roles técnicos existentes en la base de datos y la **Matriz de Permisos Propuesta para la Junta Directiva**.
2. La implementación de la autorización en ambos extremos de la arquitectura: guardas de rutas y navegación condicional en **Angular**, así como filtros estrictos de autorización mediante atributos `[Authorize(Roles = ...)]` y middlewares multitenant en **ASP.NET Core Web API**.
3. La trazabilidad completa del ciclo de vida de los datos: desde la interacción del usuario en la interfaz, pasando por los DTOs, controladores, servicios de aplicación y entidades de dominio, hasta las tablas físicas y disparadores relacionales (*triggers*) en **SQL Server**.
4. La identificación de datos de desarrollo (*seed data*) vs. datos de producción y el análisis de inconsistencias o estados en memoria.

---

## 2. Identificación y Mapeo Canónico de Roles

En el análisis del código fuente, las migraciones DDL (`database/01_schema.sql`), los datos semilla (`database/03_seed_data.sql`), el dominio (`DomainEnums.cs`), el middleware de autenticación (`InfrastructureServices.cs`) y el frontend (`AuthService`), se identificaron seis (6) roles canónicos:

| Rol Canónico en Código (`UserRole`) | Denominación en la Matriz Propuesta | Alcance Operativo | Identificador Semilla de Prueba |
| :--- | :--- | :--- | :--- |
| `AdminGlobal` | **Admin Global** | Toda la cadena (Multisede global) | `admin@hotelchain.com` |
| `Socio` | **Socio (Junta Directiva)** | Toda la cadena (Estratégico / Solo lectura) | `socio@hotelchain.com` |
| `Auditor` | **Auditor (Auditor Cadena)** | Toda la cadena (Auditoría / Solo lectura) | `auditor@hotelchain.com` |
| `AdminSede` | **Admin Sede** | Exclusivamente su sede asignada | `admin.bogota@hotelchain.com` |
| `Recepcionista` | **Recepción** | Exclusivamente su sede asignada | `recepcion.bogota@hotelchain.com` |
| `Housekeeping` | **Camarería (Ama de Llaves)** | Exclusivamente su sede asignada | `housekeeping.bogota@hotelchain.com` |

### Mecanismo de Asignación y Contexto Multitenant
- **Identificación:** Cada usuario tiene una tupla en `dbo.Users` con columna `Role` (NVARCHAR(50)).
- **Asignación Sede-Usuario:** La tabla `dbo.HotelUsers` implementa una relación N:M. Los usuarios con rol `AdminGlobal`, `Socio` y `Auditor` tienen asignación transversal a todas las sedes con bypass global; los usuarios operativos (`AdminSede`, `Recepcionista`, `Housekeeping`) están restringidos estrictamente a la sede asignada en su contrato laboral.
- **Aislamiento Multitenant (RNF-10 / RF-07):** El middleware `TenantResolutionMiddleware` intercepta la cabecera `X-Hotel-Id` y los claims del JWT. Si un usuario operativo intenta consultar o manipular recursos de una sede distinta a la autorizada, el sistema responde con **HTTP 404 (Not Found)** para evitar la divulgación de existencia de recursos ajenos.

---

## 3. Matriz de Roles, Permisos y Decisiones para la Junta Directiva

La siguiente matriz contrasta la propuesta remitida para aprobación de la Junta frente al estado del código y documenta las decisiones de gobernanza requeridas:

| Capacidad Funcional | Socio | Admin Global | Admin Sede | Recepción | Camarería | Auditor | Estado de Implementación & Regla de Negocio |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Dashboard consolidado de cadena** | **Permitido** | **Permitido** | Sin acceso | Sin acceso | Sin acceso | **Solo lectura** | `DashboardController.GetMetrics` bloquea con HTTP 403 a AdminSede si `hotelId == 0`. |
| **Dashboard de una sede** | **Permitido** | **Permitido** | **Permitido** | Sin acceso | Sin acceso | **Solo lectura** | Permitido para la sede autorizada; denegado a Recepción y Housekeeping por `roleGuard`. |
| **Parametrizar hoteles, tarifas e impuestos** | Sin acceso | **Permitido** | **Solo lectura** *(Decisión 1)* | Sin acceso | Sin acceso | Sin acceso | Protegido con `[Authorize(Roles = "AdminGlobal")]` en hoteles. *(Ver Decisión 1 para AdminSede)*. |
| **Gestionar empleados y roles** | Sin acceso | **Permitido** | **Solo su sede** *(Decisión 2)* | Sin acceso | Sin acceso | Sin acceso | `UsersController` restringido a AdminGlobal. *(Ver Decisión 2 para AdminSede)*. |
| **Crear y modificar reservas** | Sin acceso | **Sin acceso** *(Decisión 3)* | **Permitido** | **Permitido** | Sin acceso | Sin acceso | `ReservationsController.CreateReservation` protegido con roles `AdminSede,Recepcionista`. |
| **Cancelar o anular reservas** | Sin acceso | **Sin acceso** *(Decisión 3)* | **Permitido** | **Con límite** *(Decisión 4)* | Sin acceso | Sin acceso | `ReservationsController.CancelReservation` calcula penalidad según política vigente. |
| **Check-in y check-out** | Sin acceso | **Sin acceso** *(Decisión 3)* | **Permitido** | **Permitido** | Sin acceso | Sin acceso | Validaciones RF-31 (habitación limpia) y RF-40 (saldo cero). Exclusivo de recepción y admin sede. |
| **Registrar consumos y pagos** | Sin acceso | **Sin acceso** *(Decisión 3)* | **Permitido** | **Permitido** | Sin acceso | Sin acceso | `FoliosController` y `CashShiftController` restringidos a `AdminSede,Recepcionista`. |
| **Cambiar estado de limpieza** | Sin acceso | **Sin acceso** *(Decisión 3)* | **Permitido** | **Permitido** | **Permitido** | Sin acceso | `HousekeepingController.UpdateCleaningStatus` habilitado para Housekeeping, Recepcionista y AdminSede. |
| **Consultar disponibilidad entre sedes** | Sin acceso | **Permitido** | **Permitido** | **Permitido** | Sin acceso | Sin acceso | `AvailabilityController.SearchCrossHotelAvailability` restringido a roles autorizados (RF-20/21). |
| **Exportar reportes** | **Permitido** | **Permitido** | **Solo su sede** | Sin acceso | Sin acceso | **Permitido** | `ReportsController` emite CSV con totales auditados. AdminSede confinado a su sede. |
| **Ver auditoría** | Sin acceso | **Permitido** | **Solo su sede** | Sin acceso | Sin acceso | **Permitido** | `AuditController` filtra eventos. Socio no tiene acceso directo a la bitácora técnica. |
| **Anonimizar huésped (RF-47)** | Sin acceso | **Permitido** | Sin acceso | Sin acceso | Sin acceso | Sin acceso | Exclusivo `AdminGlobal` (`[Authorize(Roles = "AdminGlobal")]`). Irreversible, folios intactos. |

---

## 4. Diferencias Críticas y Decisiones Pendientes de Aprobación por la Junta

De acuerdo con la instrucción de no modificar silenciosamente las reglas de negocio, se han identificado las siguientes cuatro (4) decisiones que la Junta Directiva de CONTINENTALIA SAS debe ratificar:

### Decisión 1: Alcance de Parametrización para Admin Sede
- **Propuesta:** La matriz propone que el *Admin Sede* tenga **Solo lectura** sobre hoteles, tarifas e impuestos.
- **Situación en Código Previo:** El *Admin Sede* disponía de permisos para crear tipos de habitación, tarifas y servicios adicionales para su sede local.
- **Impacto:** Si la Junta aprueba la centralización total, cualquier cambio en precios de habitaciones, temporadas o impuestos deberá ser solicitado formalmente al *Admin Global*. Si la Junta prefiere descentralizar tarifas operativas, debe autorizarse formalmente al *Admin Sede* para modificar tarifas locales.

### Decisión 2: Delegación de Creación de Usuarios a Administradores de Sede
- **Propuesta:** La matriz indica que el *Admin Sede* puede gestionar empleados **Solo de su sede**.
- **Situación en Código Previo:** `UsersController` estaba blindado con `[Authorize(Roles = UserRole.AdminGlobal)]`. Ningún *Admin Sede* podía crear usuarios desde el backend.
- **Impacto:** Para habilitar esta capacidad sin vulnerar el aislamiento multitenant, se requiere una política de backend que permita a `AdminSede` dar de alta únicamente cajeros, recepcionistas y camareras asignados a su propio `HotelId`, impidiendo crear otros administradores o asignar roles globales.

### Decisión 3: Segregación Operativa de Admin Global en Reservas
- **Propuesta:** La matriz define que el *Admin Global* tiene **Sin acceso** a crear reservas, hacer check-in/out, registrar consumos o cancelar reservas.
- **Justificación de Seguridad:** Esta es una excelente práctica de segregación de funciones (*Separation of Duties* — SoD) conforme a normas ISO/IEC 27001 para prevenir fraudes corporativos desde cuentas directivas.
- **Implementación:** Se implementó la restricción en backend (`[Authorize(Roles = "AdminSede,Recepcionista")]`) y en frontend se desactivan los botones de acción para Admin Global, presentando una vista de solo lectura/auditoría.

### Decisión 4: Definición Formal del «Límite» para Cancelaciones en Recepción
- **Propuesta:** La matriz indica que *Recepción* puede cancelar reservas **Con límite**.
- **Situación Actual:** El backend actual calcula automáticamente la penalidad según la política de cancelación activa (`FreeCancellationHours`, `PenaltyPercent`), pero no tiene un tope monetario ni una ventana temporal máxima tras la cual el recepcionista deba requerir autorización del Admin Sede.
- **Recomendación a la Junta:** Se recomienda definir formalmente el umbral: por ejemplo, cancelaciones sin penalidad o con monto de reserva superior a $2,000,000 COP deben requerir clave de autorización de supervisor (*Admin Sede*).

---

## 5. Auditoría de la Fuente de Datos: De Dónde se Escribe y de Dónde se Lee

Para cada módulo de CONTINENTALIA SAS, se auditó el flujo de datos integral de extremo a extremo:

$$\text{Vista Angular} \longrightarrow \text{Servicio HTTP} \longrightarrow \text{Endpoint ASP.NET Core} \longrightarrow \text{Reglas de Dominio} \longrightarrow \text{SQL Server (Tabla)} \longrightarrow \text{Lectura / Respuesta} \longrightarrow \text{Actualización de Vista}$$

### Mapa Completo de Persistencia por Módulo

| Módulo | Entidad / Dato | Origen Escritura (Formulario / Acción) | Endpoint de Escritura (HTTP) | Capa de Negocio / Validaciones | Persistencia Física (SQL Server) | Origen de Lectura (Consulta / Endpoint) | Vista de Destino | Permisos Autorizados | Resultado Auditoría |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Autenticación** | Sesión y Refresh Token | Formulario de Login (`login.component.ts`) | `POST /api/auth/login` | BCrypt verify, bloqueo 5 intentos, generación JWT | `dbo.Users` (FailedAttempts), `dbo.RefreshTokens` | `GET /api/auth/profile` | Header / Layout | Todos | **Verificado** |
| **Dashboard** | Métricas consolidadas & KPIs | N/A (Solo lectura y agregación analítica) | N/A | `HotelKpiCalculator` (ADR, RevPAR, Ocupación %) | `dbo.Rooms`, `dbo.Reservations`, `dbo.FolioPayments` | `GET /api/dashboard/metrics` | `dashboard.component.ts` | AdminGlobal, Socio, Auditor, AdminSede (solo local) | **Verificado** |
| **Cotización** | Consulta tarifas & desglose | Formulario de Cotización (`availability.component.ts`) | `POST /api/availability/search` | `PricingEngine` (recargos fin de semana, temporadas, promociones) | Lectura de `dbo.RoomTypes`, `dbo.Seasons`, `dbo.Promotions`, `dbo.HotelTaxes` | `POST /api/availability/search` | Tabla de disponibilidad con fotos | AdminGlobal, AdminSede, Recepcionista | **Verificado** |
| **Disponibilidad Inter-Sedes** | Disponibilidad agregada de las 4 sedes | Pestaña "Consultar Otras Sedes" (`availability.component.ts`) | `POST /api/availability/cross-hotel` | RNF-10: Solo expone nombre sede, tipo hab, cantidad y tarifa base | `dbo.Hotels`, `dbo.Rooms`, `dbo.Reservations` | `POST /api/availability/cross-hotel` | Tarjetas comparativas inter-sedes | AdminGlobal, AdminSede, Recepcionista | **Verificado** |
| **Calendario** | Matriz de Ocupación (14 días) | Selector temporal de fecha inicio (`calendar.component.ts`) | N/A | Filtrado de traslapes de fechas en rango de 14 días | `dbo.Rooms` cruzado con `dbo.Reservations` | `GET /api/availability/calendar` | Matriz visual interactiva | AdminGlobal, AdminSede, Recepcionista | **Verificado** |
| **Reservas** | Creación de Reserva & Titular | Modal "Nueva Reserva" con Habeas Data (`reservations.component.ts`) | `POST /api/reservations` (Soporta `Idempotency-Key`) | Trigger `TR_Reservations_PreventOverbooking`, consentimiento obligatorio RF-45 | `dbo.Guests`, `dbo.DataConsents`, `dbo.Reservations`, `dbo.Folios`, `dbo.FolioCharges` | `GET /api/reservations` | Tabla de reservas y modal detalle | AdminSede, Recepcionista | **Verificado** |
| **Recepción (Check-In)** | Registro de Entrada y asignación hab | Botón "Ejecutar Check-In" (`reception.component.ts`) | `POST /api/reservations/{id}/check-in` | RF-31: Validación de habitación `Limpia` y `Disponible`, consentimiento firmado | `dbo.Reservations` (Status='CheckIn'), `dbo.Rooms` (OpStatus='Ocupada') | `GET /api/reservations?status=CheckIn` | Pestaña "En Casa / Salidas" | AdminSede, Recepcionista | **Verificado** |
| **Recepción (Check-Out)** | Registro de Salida y liquidación | Botón "Completar Check-Out" (`reception.component.ts`) | `POST /api/reservations/{id}/check-out` | RF-40: Validación estricta `Folio.Balance == 0.00`. RF-34: Hab pasa a `Sucia` | `dbo.Reservations` (Status='Liquidada'), `dbo.Rooms` (CleanStatus='Sucia'), `dbo.Folios` (Cerrado) | `GET /api/reservations` | Historial de estancias | AdminSede, Recepcionista | **Verificado** |
| **Housekeeping** | Estado de Limpieza de Habitación | Consola táctil de piso (`housekeeping.component.ts`) | `PUT /api/housekeeping/rooms/{roomId}/status` | RF-33: Transición táctil (Sucia $\to$ En Limpieza $\to$ Limpia) | `dbo.Rooms` (CleaningStatus, OperationalStatus, Notes) | `GET /api/housekeeping/rooms` | Cuadrícula de tarjetas táctiles | Housekeeping, Recepcionista, AdminSede | **Verificado** |
| **Folio (Cargos)** | Consumo de Servicio Adicional | Modal "Cargar Servicio" (`folio.component.ts`) | `POST /api/folios/{id}/charges` | RF-17: Bloqueo de servicios inactivos; cálculo de IVA | `dbo.FolioCharges`, `dbo.Folios` (recalcula Totales y Saldo) | `GET /api/folios/{id}` | Libro mayor de cuenta de huésped | AdminSede, Recepcionista | **Verificado** |
| **Folio (Anulación)** | Corrección inmutable de cargo | Modal "Anular Cargo" con motivo (`folio.component.ts`) | `POST /api/folios/charges/{chargeId}/annul` | RF-36: Inmutabilidad contable mediante contra-asiento negativo | `dbo.FolioCharges` (nuevo registro con Amount negativo), `dbo.AuditLogs` | `GET /api/folios/{id}` | Lista de cargos con badge "Anulado" | AdminSede, Recepcionista | **Verificado** |
| **Folio (Pagos)** | Registro de abono / pago | Modal "Registrar Pago" (`folio.component.ts`) | `POST /api/folios/{id}/payments` (Soporta `Idempotency-Key`) | Emisión de número de recibo consecutivo, vinculación a turno de caja | `dbo.FolioPayments`, `dbo.CashShifts` (suma totales), `dbo.Folios` (reduce saldo) | `GET /api/folios/{id}` | Resumen financiero del folio | AdminSede, Recepcionista | **Verificado** |
| **Caja (Turnos)** | Apertura y Cierre de Turno | Pantalla de Turnos (`cash-shift.component.ts`) | `POST /api/cashshift/open`, `POST /api/cashshift/close` | RF-42: Congelamiento contable de importes por método (Efectivo, Tarjeta, Transferencia) | `dbo.CashShifts` (OpenedAt, ClosedAt, TotalCash, TotalCard, Status='Cerrado') | `GET /api/cashshift/current`, `GET /api/cashshift/history` | Panel de estado de turno activo | AdminSede, Recepcionista | **Verificado** |
| **Admin (Habitaciones)** | Alta de nueva habitación física | Modal "Crear Habitación" (`admin-pages.component.ts`) | `POST /api/rooms` | RF-11: Unicidad de número de habitación dentro del hotel | `dbo.Rooms` | `GET /api/rooms` | Pestaña "Inventario Habitaciones" | AdminGlobal, AdminSede | **Verificado** |
| **Admin (Servicios)** | Alta y activación de servicios | Modal "Nuevo Servicio" (`admin-pages.component.ts`) | `POST /api/services`, `POST /api/services/{id}/toggle-status` | RF-17: Catálogo por sede | `dbo.AdditionalServices` | `GET /api/services` | Pestaña "Catálogo de Servicios" | AdminGlobal, AdminSede | **Verificado** |
| **Admin (Habeas Data)** | Anonimización de huésped | Botón "Anonimizar" (`admin-pages.component.ts`) | `POST /api/guests/{id}/anonymize` | RF-47: Sanitización irreversible de PII, preservando integridad de folios y reservas | `dbo.Guests` (IsAnonymized=1, Name='ANONIMO', Doc='ANON_...'), `dbo.AuditLogs` | `GET /api/guests` | Pestaña "Huéspedes & Habeas Data" | **Exclusivo AdminGlobal** | **Verificado** |
| **Auditoría** | Bitácora de eventos del sistema | Visor de auditoría (`audit.component.ts`) | Automático via `HttpAuditMiddleware` y acciones de negocio | Registro inmutable de transacciones, correlación HTTP y excepciones | `dbo.AuditLogs` | `GET /api/audit` | Tabla de auditoría con exportación CSV | AdminGlobal, Auditor, AdminSede (solo local) | **Verificado** |

---

## 6. Auditoría de Datos Simulados, Mocks y Semillas de Desarrollo

Se examinaron todos los orígenes de datos en frontend y backend para determinar si existían datos simulados que estuviesen alimentando pantallas operativas:

1. **Datos Semilla de Base de Datos (`database/03_seed_data.sql`):**
   - **Contenido:** Cuatro (4) hoteles asociados (`Grand Oasis Bogotá`, `Caribe Real Cartagena`, `Montaña Dorada Medellín`, `Valle Encantado Cali`), 16 usuarios del sistema con credenciales BCrypt para pruebas, 16 tipos de habitación, inventario inicial de habitaciones, temporadas, impuestos y promociones.
   - **Evaluación:** Son datos iniciales legítimos para inicialización del esquema en SQL Server. Se verificó que **no contienen datos personales reales de clientes** (cumplimiento Ley 1581 / Habeas Data).
2. **Catálogos Estáticos en Frontend:**
   - **Tipos de Documento:** `['CC', 'CE', 'Pasaporte', 'TI', 'NIT']`. Es un catálogo legítimamente estándar para la República de Colombia.
   - **Métodos de Pago:** `['Efectivo', 'TarjetaDebito', 'TarjetaCredito', 'Transferencia']`. Coincide exactamente con el `enum PaymentMethod` del dominio backend.
   - **Categorías de Servicios:** `['Minibar', 'Restaurante', 'Lavandería', 'RoomService', 'Spa', 'Transporte']`. Coincide con la clasificación del PMS.
3. **Inexistencia de Mocks en Componentes Operativos:**
   - Se verificó que ningún componente operativo (`reservations`, `reception`, `housekeeping`, `folio`, `cash-shift`, `admin`, `audit`) utiliza arreglos codificados (*hardcoded arrays*) para simular transacciones exitosas. Todas las operaciones invocan a `ApiService`, la cual envía peticiones HTTP reales a ASP.NET Core y recarga el estado desde SQL Server.

---

## 7. Matriz de Pruebas de Autorización Ejecutadas

Se diseñó e implementó una batería de pruebas de integración y unitarias para verificar la robustez de los permisos:

| ID Caso | Rol Evaluado | Acción / Endpoint | Recurso Solicitado | Resultado Esperado | Resultado Real | Código HTTP | Evidencia Técnica |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **TC-AUTH-01** | Anónimo (Sin login) | `GET /api/dashboard/metrics` | Métricas Dashboard | Acceso Denegado (401) | Rechazado | **401 Unauthorized** | `AnonymousUser_AccessingProtectedEndpoint_ShouldReturnUnauthorized401` |
| **TC-AUTH-02** | Housekeeping | `POST /api/reservations` | Crear Reserva | Acceso Denegado (403) | Rechazado | **403 Forbidden** | `Housekeeping_AttemptingToCreateReservation_ShouldReturnForbidden403` |
| **TC-AUTH-03** | Housekeeping | `PUT /api/housekeeping/rooms/1/status` | Actualizar Limpieza | Permitido (200) | Actualizado | **200 OK** | `Housekeeping_UpdatingCleaningStatus_ShouldBeAllowed` |
| **TC-AUTH-04** | Auditor | `POST /api/folios/1/charges` | Agregar Cargo a Folio | Acceso Denegado (403) | Rechazado | **403 Forbidden** | `Auditor_AttemptingToAddFolioCharge_ShouldReturnForbidden403` |
| **TC-AUTH-05** | Recepcionista | `POST /api/guests/1/anonymize` | Anonimizar Huésped | Acceso Denegado (403) | Rechazado | **403 Forbidden** | `Recepcionista_AttemptingToAnonymizeGuest_ShouldReturnForbidden403` |
| **TC-AUTH-06** | Admin Global | `POST /api/guests/1/anonymize` | Anonimizar Huésped | Permitido (200 / 400) | Ejecutado | **200 OK** | `AdminGlobal_AnonymizeGuest_Executed` / `RbacMatrixUnitTests` |
| **TC-AUTH-07** | Recepcionista | `GET /api/audit` | Bitácora Auditoría | Acceso Denegado (403) | Rechazado | **403 Forbidden** | `Recepcionista_AttemptingToViewAuditLogs_ShouldReturnForbidden403` |
| **TC-AUTH-08** | Auditor | `GET /api/audit` | Bitácora Auditoría | Permitido (200) | Registros listados | **200 OK** | `Auditor_ViewingAuditLogs_ShouldBeAllowed` |
| **TC-AUTH-09** | Admin Sede | `GET /api/dashboard/metrics?hotelId=0` | Dashboard Consolidado | Acceso Denegado (403) | Rechazado | **403 Forbidden** | `AdminSede_QueryingChainConsolidatedDashboard_ShouldReturnForbidden403` |
| **TC-AUTH-10** | Socio | `GET /api/dashboard/metrics` | Dashboard Consolidado | Permitido (200) | Métricas listadas | **200 OK** | `Socio_QueryingChainConsolidatedDashboard_ShouldBeAllowed` |
| **TC-AUTH-11** | Admin Global | `POST /api/reservations` | Crear Reserva Operativa | Acceso Denegado (403) | Rechazado | **403 Forbidden** | `AdminGlobal_AttemptingToCreateReservation_ShouldReturnForbidden403` |
| **TC-AUTH-12** | Recepcionista (Bogotá) | `GET /api/rooms` con `X-Hotel-Id: 2` | Habitaciones de Cartagena | No Encontrado (404) | Aislamiento activo | **404 Not Found** | `Multitenancy_AccessOtherHotel_ShouldReturnNotFound404` (RF-07) |

---

## 8. Defectos Identificados y Correcciones Realizadas

1. **Defecto:** Falta de guardas de rol a nivel de rutas en el cliente web.  
   *Causa Raíz:* `app.routes.ts` únicamente implementaba `authGuard`, permitiendo que cualquier usuario con sesión iniciada navegase a rutas como `/audit` o `/admin` manipulando la barra de direcciones.  
   *Corrección:* Se creó `roleGuard.ts`, integrando metadata `data: { roles: [...] }` en todas las rutas hijas y conectándolo con `AuthService.getDefaultRoute()` para redirección automática y notificación clara de acceso denegado.
2. **Defecto:** Elementos de menú visibles para roles sin autorización.  
   *Causa Raíz:* El sidebar presentaba opciones de administración, auditoría y caja a roles como *Camarería* y *Recepción*.  
   *Corrección:* Se configuraron condiciones estructurales `*ngIf="auth.can..."` en `main-layout.component.ts` basadas en señales computadas reactivas. El personal de *Camarería* ahora visualiza exclusivamente la consola táctil de housekeeping.
3. **Defecto:** Endpoints de mutación sin restricción granular por rol.  
   *Causa Raíz:* `ReservationsController`, `FoliosController`, `HousekeepingController` y `CashShiftController` utilizaban `[Authorize]` sin especificar roles, permitiendo que un usuario autenticado con cualquier perfil enviara peticiones POST/PUT directas mediante herramientas como Postman.  
   *Corrección:* Se aplicaron atributos estrictos `[Authorize(Roles = ...)]` en cada método de mutación en el backend, cerrando cualquier vector de elusión por API directa.
4. **Defecto:** Ausencia de bloqueo en Dashboard consolidado para Administradores de Sede.  
   *Causa Raíz:* `DashboardController.GetMetrics` permitía consultar el consolidado multisede si el parámetro de hotel no se enviaba.  
   *Corrección:* Se incorporó validación en `DashboardController` para retornar **HTTP 403 Forbidden** si un usuario con rol `AdminSede` solicita métricas con `hotelId == 0` o nulo.

---

## 9. Limitaciones Residuales y Recomendaciones de Seguridad

1. **Topes Cuantitativos de Cancelación en Recepción:**  
   Queda pendiente la definición de negocio por parte de la Junta Directiva respecto al monto o plazo límite para las cancelaciones efectuadas por Recepción (Decisión 4).
2. **Expiración de Sesión y Rotación de Tokens:**  
   El tiempo de vida de los Access Tokens está fijado en 20 minutos con Refresh Tokens persistidos en `dbo.RefreshTokens`. Se recomienda programar una tarea cronológica periódica para depurar tokens expirados y revocados que superen los 30 días de antigüedad.
3. **Monitoreo de Intentos de Acceso a Otras Sedes:**  
   El sistema responde correctamente con HTTP 404 ante intentos de acceso cruzado entre sedes no autorizadas (RF-07). Se sugiere generar una alerta de seguridad automática en `dbo.AuditLogs` cuando un mismo usuario registre más de tres intentos consecutivos de acceso con cabecera `X-Hotel-Id` no autorizada.
