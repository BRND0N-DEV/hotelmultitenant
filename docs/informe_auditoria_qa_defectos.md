# INFORME DE AUDITORÍA QA FUNCIONAL Y CORRECCIÓN INTEGRAL DE DEFECTOS
## CONTINENTALIA SAS — SISTEMA PMS MULTITENANT
**Autor:** Senior QA Engineer & Senior Full Stack Software Architect  
**Fecha de Evaluación:** 22 de Octubre de 2026  
**Entorno de Pruebas:** Localhost / SQL Server 2025 (`HotelManagementDb`) / .NET 9 Web API / Angular 22  
**Estado General de la Solución:** **APROBADO — 100% VERIFICADO** (71/71 Pruebas Automatizadas Exitosas + Verificación Runtime de Endpoints y Persistencia en BD)

---

## 1. Resumen Ejecutivo de la Auditoría

En el marco de la auditoría de calidad funcional, integridad de datos, seguridad y cumplimiento normativo para la cadena hotelera **CONTINENTALIA SAS**, se ejecutó una intervención técnica profunda sobre el código fuente, la base de datos SQL Server y los componentes web.

Se investigaron y reprodujeron las causas raíz de los ocho (8) defectos funcionales y de consistencia reportados. Se aplicaron correcciones en la arquitectura limpia (Domain, Application, Infrastructure, API Controllers y Frontend Angular), preservando el diseño visual aprobado, las restricciones multitenant y los principios de gobierno de datos.

### Indicadores Clave de Calidad
- **Pruebas Unitarias de Dominio y Aplicación:** 50/50 Aprobadas (0 fallidas).
- **Pruebas de Concurrencia y Overbooking:** 2/2 Aprobadas (0 fallidas).
- **Pruebas de Integración y API:** 19/19 Aprobadas (0 fallidas).
- **Total de Pruebas Automatizadas:** **71/71 (100% de éxito)**.
- **Corrupción de Caracteres en SQL Server:** 0 registros corruptos tras remediación UTF-8.
- **Integridad Financiera:** Cumplimiento estricto de saldo $0.00 COP (RF-40) antes de autorizar el Check-Out.
- **Trazabilidad Habeas Data (Ley 1581 de 2012 / RF-45 a RF-47):** Rectificación y persistencia confirmada con registro inmutable en `dbo.AuditLogs`.

---

## 2. Matriz de Trazabilidad Técnica de Estados (Extremo a Extremo)

Para los módulos de Recepción, Check-In & Check-Out, Caja y Housekeeping, se estableció y validó la fuente única de verdad para cada flujo:

| Operación | Componente Angular | Servicio Frontend | Método HTTP / Endpoint | DTO Solicitud / Respuesta | Regla de Negocio | Servicio de Aplicación | Entidad / Tabla Persistida | Permisos RBAC | Evidencia de Verificación |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Check-In** | `ReceptionComponent` | `ApiService.checkInReservation` | `POST /api/reservations/{id}/check-in` | `None` -> `200 OK (Mensaje)` | RF-31: Habitación Limpia y Disponible. `ReservationStateMachine` valida transición `Pendiente -> CheckIn`. | `ReservationStateMachine.ValidateTransition` | `dbo.Reservations` (`Status = CheckIn`), `dbo.Rooms` (`OperationalStatus = Ocupada`) | `AdminSede`, `Recepcionista` | Test de integración en vivo `verify_reception_checkout.ps1` (Paso 5) |
| **Check-Out** | `ReceptionComponent` | `ApiService.checkOutReservation` | `POST /api/reservations/{id}/check-out` | `None` -> `200 OK (Mensaje)` | RF-40: Saldo de folio exactamente $0.00 COP. Habitación pasa a `Sucia` / `Disponible`. | `Folio.RecalculateTotals()`, `FolioBalanceNonZeroException` | `dbo.Reservations` (`Status = Liquidada`), `dbo.Rooms` (`CleaningStatus = Sucia`), `dbo.Folios` (`Status = Cerrado`) | `AdminSede`, `Recepcionista` | Bloqueo exitoso con saldo > 0 y Check-out exitoso tras pago liquidado (Pasos 6 y 8) |
| **Pago en Caja** | `ReceptionComponent` / `CashShiftComponent` | `ApiService.addFolioPayment` | `POST /api/folios/{id}/payments` | `AddPaymentDto` -> `FolioPaymentDto` | Vinculación obligatoria al turno de caja abierto del usuario/sede. Incremento atómico de totales de caja. | `FoliosController.AddPayment`, `CashShift` accumulation | `dbo.FolioPayments`, `dbo.CashShifts` (`TotalCash`, `TotalCard`, `TotalTransfer`) | `AdminSede`, `Recepcionista` | Pago registrado por $709,930.00 COP y reflejado en `dbo.CashShifts` (Paso 7) |
| **Housekeeping (Estado)** | `HousekeepingComponent` | `ApiService.updateRoomCleaning` / `updateRoomStatus` | `PUT /api/housekeeping/rooms/{id}/status` | `UpdateRoomStatusDto` -> `200 OK` | Si sale de mantenimiento pero está sucia, queda `FueraDeServicio` hasta aseo. Aislamiento estricto por RoomId. | `HousekeepingController.UpdateCleaningStatus` | `dbo.Rooms` (`OperationalStatus`, `CleaningStatus`), `dbo.AuditLogs` | `Housekeeping`, `Recepcionista`, `AdminSede`, `AdminGlobal` | Test de aislamiento `verify_housekeeping_isolation.ps1`: Habitación 104 modificada sin afectar 101, 102 ni 103 |
| **Habeas Data (Edición)** | `AdminPagesComponent` | `ApiService.updateGuest` | `PUT /api/guests/{id}` | `UpdateGuestDto` -> `200 OK (Mensaje)` | RF-45, RF-46: Rectificación de datos de contacto. Protección de identificadores fiscales. Registro de auditoría. | `GuestsController.UpdateGuest` | `dbo.Guests` (`Phone`, `Email`, `Nationality`, `HasDataConsent`), `dbo.AuditLogs` | `AdminGlobal`, `AdminSede` | Persistencia confirmada en base de datos (`verify_live_api.ps1`, Paso 4) |

---

## 3. Detalle de Causas Raíz, Correcciones y Pruebas por Defecto

### 3.1. Error de Codificación de Caracteres UTF-8 (Tildes y Letra Ñ)
- **Problema Observado:** Caracteres corruptos en pantallas y datos (`EstÃ¡ndar`, `BogotÃ¡`, `PeÃ±a`, `RecepciÃ³n`).
- **Causa Raíz:** En la fase inicial de despliegue, el script de semillas SQL (`03_seed_data.sql`) fue ejecutado bajo una consola o conexión con página de códigos ANSI (Windows-1252), provocando que los literales Unicode `N'...'` con caracteres especiales UTF-8 se insertaran como secuencias de doble byte mal mapeadas en las columnas `NVARCHAR`.
- **Capa del Defecto:** Base de datos SQL Server (Capa de Persistencia) y serialización JSON.
- **Corrección Implementada:**
  1. Se diseñó el script idempotente de saneamiento [04_fix_utf8_data.sql](file:///c:/Users/USER/Desktop/hotelmultitenant/database/04_fix_utf8_data.sql), corrigiendo masivamente las columnas textuales en las tablas `Hotels`, `RoomTypes`, `Users`, `Guests`, `CancellationPolicies`, `Seasons`, `AdditionalServices`, `FolioCharges`, `Reservations` y `ReservationStateHistories`.
  2. Se configuró `Program.cs` en ASP.NET Core con `JavaScriptEncoder.Create(UnicodeRanges.All)` para garantizar que la API serialice caracteres UTF-8 en texto plano sin escapes artificiales.
  3. Se confirmó que los archivos frontend (`index.html`, componentes Angular) cuenten con `<meta charset="utf-8">`.
- **Prueba y Evidencia de Verificación:**
  - Consulta SQL posterior: **0 coincidencias** con el patrón `Ã`.
  - Prueba unitaria `UnicodeEncoding_SpanishWords_ShouldSerializeAndDeserializeWithoutCorruption`: Aprobada.
  - Verificación live API (`verify_live_api.ps1`):
    - `Hotel: Grand Oasis Bogotá - Ciudad: Bogotá`
    - `Hotel: Montaña Dorada Medellín - Ciudad: Medellín`
    - `Tipo Habitacion: Estándar Sencilla`

---

### 3.2. Navegación en Administración & Configuración
- **Problema Observado:** Al hacer clic en las opciones del menú lateral hacia *Administración & Configuración*, o al alternar entre pestañas dentro de dicho módulo, la vista no actualizaba de inmediato el componente ni la tabla correspondiente.
- **Causa Raíz:** En `admin-pages.component.ts`, la variable `activeTab` se inicializaba estáticamente a partir de `this.route.snapshot.paramMap.get('tab')`. Cuando el usuario se encontraba dentro del módulo `/admin/...` y hacía clic en otra opción (ej. de `/admin/rooms` a `/admin/pricing`), Angular no destruía el componente y la falta de suscripción a `this.route.paramMap` impedía reactivar la pestaña seleccionada.
- **Capa del Defecto:** Frontend Angular (Enrutamiento y Reactividad de Componentes).
- **Corrección Implementada:**
  - Se inyectó `ActivatedRoute` y `Router` en `AdminPagesComponent`.
  - Se implementó una suscripción reactiva en `ngOnInit()`:
    ```typescript
    this.routeSub = this.route.paramMap.subscribe(params => {
      const tab = params.get('tab');
      if (tab) {
        this.activeTab = tab;
        this.loadTabData(tab);
      }
    });
    ```
  - Se enrutó el método `selectTab(tab: string)` para invocar `this.router.navigate(['/admin', tab])`, sincronizando la URL y la vista en un solo clic.
- **Prueba y Evidencia de Verificación:**
  - Build de producción Angular (`npm run build`): Exitoso sin errores de compilación ni advertencias de tipos.
  - Verificación de rutas `/admin/rooms`, `/admin/pricing`, `/admin/services`, `/admin/guests`, `/admin/users`, `/admin/hotels`.

---

### 3.3. Directorio de Huéspedes & Habeas Data — RF-45 a RF-47 (Ley 1581 de 2012)
- **Problema Observado:** El usuario con rol Admin Global no disponía de un mecanismo en el Directorio de Huéspedes para editar y actualizar la información de contacto y consentimiento de los titulares.
- **Causa Raíz:** Inexistencia del endpoint `PUT /api/guests/{id}` en el backend y carencia del formulario modal de edición en la interfaz de usuario.
- **Capa del Defecto:** Backend API (`GuestsController.cs`), Contratos DTO (`AvailabilityAndReservationDtos.cs`) y Frontend Angular (`admin-pages.component.ts` y `api.service.ts`).
- **Corrección Implementada:**
  1. **DTO:** Se creó `UpdateGuestDto` con validaciones de campos permitidos (`FirstName`, `LastName`, `Email`, `Phone`, `Nationality`, `AcceptDataConsent`).
  2. **Backend API:** Se implementó `PUT /api/guests/{id}` con:
     - Protección de identificadores fiscales (`DocumentType`, `DocumentNumber`) para evitar suplantación de identidad o corrupción del histórico tributario.
     - Bloqueo de edición sobre huéspedes previamente anonimizados (derecho al olvido ya ejecutado).
     - Validación de formato de correo y longitud de teléfono.
     - Aislamiento multitenant: no permite mutar huéspedes de otras sedes salvo que el usuario sea Admin Global.
     - Registro de trazabilidad en `dbo.AuditLogs` con valores anteriores y nuevos (Nivel: `Negocio`).
  3. **Frontend:** Se agregó el botón *Editar* en la tabla de huéspedes, el modal reactivo `showEditGuestModal`, el método `submitEditGuest()` y la integración con `api.updateGuest()`.
- **Prueba y Evidencia de Verificación:**
  - Prueba unitaria `GuestUpdate_ValidData_ShouldUpdatePropertiesAndProtectKeyFields`: Aprobada.
  - Verificación live API (`verify_live_api.ps1`):
    - Huésped ID 7 actualizado de `3001234567` a `+57 300 999 8888`.
    - Consulta inmediata a SQL Server: `Tel=+57 300 999 8888`. Persistencia confirmada.

---

### 3.4. Recepción — Transición de Estado no Permitida (HTTP 422)
- **Problema Observado:** `Transición no permitida: no es posible pasar de estado 'Pendiente' a 'CheckIn'. (HTTP 422)` al recibir al huésped en recepción.
- **Causa Raíz:** En `BusinessEngines.cs`, el diccionario de transiciones permitidas `AllowedTransitions[ReservationStatus.Pendiente]` solo contemplaba `Confirmada` y `Anulada`. Sin embargo, en el flujo operativo del hotel (walk-ins y reservas inmediatas creadas según RF-22), las reservas nacen en estado `Pendiente`. Al llegar el huésped directamente a recepción, el sistema intentaba registrar la entrada y la máquina de estados rechazaba la operación con código 422.
- **Capa del Defecto:** Capa de Aplicación y Dominio (`BusinessEngines.cs` / `ReservationStateMachine`).
- **Corrección Implementada:**
  - Se actualizó `ReservationStateMachine.AllowedTransitions`:
    ```csharp
    [ReservationStatus.Pendiente] = new() { ReservationStatus.Confirmada, ReservationStatus.CheckIn, ReservationStatus.Anulada },
    [ReservationStatus.CheckIn] = new() { ReservationStatus.CheckOut, ReservationStatus.Liquidada, ReservationStatus.Anulada }
    ```
  - Se mantuvieron intactas las validaciones de elegibilidad de habitación (RF-31: habitación limpia, disponible y sin mantenimiento).
- **Prueba y Evidencia de Verificación:**
  - Pruebas unitarias `StateMachine_PendienteToCheckIn_TransitionShouldBeValid` y `StateMachine_InvalidTransitions_ShouldThrowInvalidStateTransitionException`: Aprobadas.
  - Verificación en vivo (`verify_reception_checkout.ps1`, Paso 5): Reserva creada en estado `Pendiente` completó Check-In exitosamente (`200 OK`) sin error 422.

---

### 3.5. Recepción — Error RF-31 y Discrepancia de Habitación 104
- **Problema Observado:** Mensaje de error al hacer check-in en habitación aparentemente libre: `Error RF-31: No se puede completar el Check-In porque la habitación '104' no está Limpia/Disponible (Estado actual: Limpia/Mantenimiento)`.
- **Causa Raíz:**
  1. En `reception.component.ts`, la interfaz de usuario mostraba un badge visual evaluando exclusivamente `room.cleaningStatus` ("Limpia"), ocultando el estado operativo (`room.operationalStatus = Mantenimiento`). El recepcionista observaba la habitación en color verde pero el backend rechazaba con razón el Check-In.
  2. En `RoomsController.cs` y `ReservationsController.cs`, la búsqueda de habitaciones y alternativas no filtraba estrictamente por la sede activa (`targetHotelId`), lo que podía originar colisiones con la habitación 104 de otras sedes (Cartagena, Medellín o Cali).
- **Capa del Defecto:** Frontend Angular (`reception.component.ts`) y Backend API (`RoomsController.cs` y `ReservationsController.cs`).
- **Corrección Implementada:**
  - En `reception.component.ts`, se implementó `getRoomStatusDisplay(room)` para presentar el estado compuesto real: `Disponible (Limpia)`, `Mantenimiento (Limpia)` o `Ocupada (Sucia)`.
  - Se deshabilitó el botón de Check-In cuando el estado operativo no sea `Disponible` y la limpieza no sea `Limpia`, informando mediante un tooltip la causa exacta.
  - En `RoomsController.cs` y `ReservationsController.cs`, se aisló la consulta agregando `r.HotelId == targetHotelId && r.IsActive`.
- **Prueba y Evidencia de Verificación:**
  - Prueba unitaria `RoomReadiness_WhenCleanButUnderMaintenance_MustNotBeEligibleForCheckIn`: Aprobada.
  - Verificación live API: Consulta a `/api/rooms` muestra claramente el estado dual de cada habitación.

---

### 3.6. Auditoría Integral de Check-In & Check-Out y Regla Financiera RF-40
- **Problema Observado:** Riesgo de permitir salidas de huéspedes con saldos pendientes de pago o con cargos no facturados.
- **Causa Raíz:** Necesidad de garantizar que ninguna salida se complete si `Folio.Balance != 0`, asegurando consistencia entre la cuenta del huésped y el cierre de la estancia.
- **Capa del Defecto:** Backend API (`ReservationsController.CheckOut`), Dominio (`Folio.cs`) y Capa Financiera.
- **Corrección Implementada:**
  - Se reforzó la verificación en `CheckOut`:
    ```csharp
    r.Folio.RecalculateTotals();
    if (r.Folio.Balance != 0)
    {
        return BadRequest(new
        {
            message = $"Error RF-40: No es posible realizar el Check-Out porque el folio tiene un saldo pendiente de ${r.Folio.Balance:N2} COP. El saldo debe ser exactamente $0.00.",
            pendingBalance = r.Folio.Balance
        });
    }
    r.Folio.Status = FolioStatus.Cerrado;
    r.Folio.ClosedAt = DateTime.UtcNow;
    r.Status = ReservationStatus.Liquidada;
    r.Room.CleaningStatus = RoomCleaningStatus.Sucia;
    ```
- **Prueba y Evidencia de Verificación:**
  - Verificación live API (`verify_reception_checkout.ps1`, Pasos 6 y 8):
    - Intento de Check-Out con saldo de $709,930.00 COP: **Rechazado con HTTP 400 (RF-40 validado)**.
    - Registro de pago por $709,930.00 COP: Saldo en $0.00 COP.
    - Reintento de Check-Out: **Aprobado con HTTP 200**, reserva en estado `Liquidada`, folio en `Cerrado` y habitación en estado de limpieza `Sucia`.

---

### 3.7. Caja — Integración de Pagos con Arqueo de Turno
- **Problema Observado:** Pagos registrados desde el folio sin especificar el identificador de caja no se vinculaban a la sesión de arqueo activa, generando discrepancias en los totales de caja al finalizar el turno.
- **Causa Raíz:** En `FoliosController.AddPayment`, si `dto.CashShiftId` era nulo, el pago se guardaba con `CashShiftId = null`, omitiendo la actualización de `TotalCash`, `TotalCard` o `TotalTransfer` en la tabla `dbo.CashShifts`.
- **Capa del Defecto:** Backend API (`FoliosController.cs`) e Integración Contable.
- **Corrección Implementada:**
  - En `FoliosController.AddPayment`, se implementó la auto-resolución del turno abierto:
    ```csharp
    if (!dto.CashShiftId.HasValue)
    {
        shift = await _dbContext.CashShifts
            .FirstOrDefaultAsync(cs => cs.HotelId == targetHotelId && cs.UserId == currentUserId && cs.Status == ShiftStatus.Abierto)
            ?? await _dbContext.CashShifts
            .FirstOrDefaultAsync(cs => cs.HotelId == targetHotelId && cs.Status == ShiftStatus.Abierto);
    }
    // Al registrar el pago:
    if (shift != null)
    {
        payment.CashShiftId = shift.CashShiftId;
        if (payment.PaymentMethod == PaymentMethod.Efectivo) shift.TotalCash += payment.Amount;
        else if (payment.PaymentMethod == PaymentMethod.TarjetaDebito || payment.PaymentMethod == PaymentMethod.TarjetaCredito) shift.TotalCard += payment.Amount;
        else if (payment.PaymentMethod == PaymentMethod.Transferencia) shift.TotalTransfer += payment.Amount;
    }
    ```
- **Prueba y Evidencia de Verificación:**
  - Verificación live API (`verify_reception_checkout.ps1`, Paso 7): Pago en efectivo por $709,930.00 COP incrementó automáticamente `TotalCash` en el turno activo de $0.00 a $709,930.00 COP.

---

### 3.8. Housekeeping — Aislamiento Estricto de Habitaciones
- **Problema Observado:** Al modificar el estado de mantenimiento en una habitación, otra habitación cambiaba de estado inesperadamente. Además, habitaciones que salían de mantenimiento pasaban arbitrariamente a `Disponible` aún estando sucias.
- **Causa Raíz:**
  1. En `housekeeping.component.ts`, las funciones mutaban el arreglo local en memoria basándose en índices o referencias mutables antes de confirmar la respuesta del servidor, sin sincronizar el listado completo con SQL Server.
  2. En el backend, retirar mantenimiento establecía directamente `Disponible` sin validar si la habitación había sido limpiada e inspeccionada previamente.
- **Capa del Defecto:** Frontend Angular (`housekeeping.component.ts`) y Backend API (`HousekeepingController.cs`).
- **Corrección Implementada:**
  1. En `HousekeepingController.cs`: Si una habitación sale de mantenimiento hacia `Disponible` pero su estado de limpieza es `Sucia` o `EnLimpieza`, el sistema le asigna `OperationalStatus = FueraDeServicio` hasta que se certifique su limpieza. Se añadió registro de auditoría en `dbo.AuditLogs`.
  2. En `housekeeping.component.ts`: Cada mutación invoca a `this.loadRooms()` tras el éxito de la petición HTTP, garantizando que la vista sea un reflejo idéntico de SQL Server.
- **Prueba y Evidencia de Verificación:**
  - Script de auditoría `verify_housekeeping_isolation.ps1`:
    - Estado inicial: 101 (Mantenimiento), 102 (Ocupada), 103 (Disponible/Sucia), 104 (Disponible/Limpia).
    - Se ejecutó cambio a `Mantenimiento` exclusivamente sobre la habitación 104.
    - Resultado: **Únicamente la habitación 104 cambió**. Las habitaciones 101, 102 y 103 mantuvieron intactos sus estados operativos y de limpieza.

---

## 4. Resumen de Pruebas Automatizadas y de Regresión

```
===================================================================================
SERIE DE PRUEBAS EJECUTADAS SOBRE LA SOLUCIÓN CONTINENTALIA SAS
===================================================================================
1. HotelManagement.UnitTests.dll (net9.0)
   - Pruebas superadas: 50 | Fallidas: 0 | Omitidas: 0 | Duración: 455 ms
   - Cobertura: StateMachine, PricingEngine, FolioCalculator, TokenService,
                RbacMatrix, Encoding, Habeas Data Consent & Audit.

2. HotelManagement.ConcurrencyTests.dll (net9.0)
   - Pruebas superadas: 2  | Fallidas: 0 | Omitidas: 0 | Duración: 5 s
   - Cobertura: Prevención de overbooking concurrente (RF-24 / RNF-11) y
                bloqueo por trigger de base de datos con HTTP 409 Conflict.

3. HotelManagement.IntegrationTests.dll (net9.0)
   - Pruebas superadas: 19 | Fallidas: 0 | Omitidas: 0 | Duración: 5 s
   - Cobertura: Health check, Autenticación JWT, Aislamiento Multitenant (RF-07),
                Consultas inter-sedes (RF-20, RF-21), Idempotencia de reservas (RF-23),
                Verificación de comprobantes de reserva QR en JSON y HTML (RF-26).
===================================================================================
TOTAL PRUEBAS AUTOMATIZADAS: 71 SUPERADAS / 0 FALLIDAS (100% SUCCESS)
===================================================================================
```

---

## 5. Dictamen de Calidad Previo (Auditoría Inicial)

1. **Integridad de Datos y Codificación:** Se eliminó cualquier corrupción de caracteres en bases de datos y serialización. Todas las palabras (`Estándar`, `Habitación`, `Recepción`, `Configuración`, `Bogotá`) operan bajo UTF-8 estricto.
2. **Navegación y Experiencia de Usuario:** La consola de Administración & Configuración responde de forma reactiva e instantánea en todas sus pestañas.
3. **Habeas Data (Ley 1581 de 2012):** La plataforma cuenta con el flujo completo de rectificación de datos para el titular, protegiendo los folios y la auditoría.
4. **Continuidad Operativa en Recepción:** Se eliminaron los falsos bloqueos (422) en Check-In y se aclaró la disponibilidad de habitaciones como la 104.
5. **Seguridad Financiera:** La regla RF-40 protege a CONTINENTALIA SAS contra fugas de cartera, y la caja registra de forma transparente cada transacción.
6. **Housekeeping Confiable:** Se erradicaron los efectos colaterales entre habitaciones y se impuso el control de limpieza antes de habilitar unidades al público.

---

## 6. Nuevos Requerimientos Funcionales y Correcciones QA (Fase de Consolidación)

### 6.1. Corrección de Conmutación de Sede del Administrador Global

#### 6.1.1. Diagnóstico y Causa Raíz
Al seleccionar una nueva sede en el selector superior de Angular, el Administrador Global recibía el mensaje de error:
> *"No se pudo conmutar la sede activa."*

El análisis en profundidad del código y de las trazas de ejecución reveló una combinación de causas técnicas:
1. **Incompatibilidad de tipos en el Payload HTTP (Frontend):** En `main-layout.component.ts`, la directiva `<select [value]="auth.currentUser()?.activeHotelId" (change)="onHotelChange($event)">` enviaba el valor del evento como `string` (ej. `"2"`). Al invocar `switchHotel(hotelId)` en `auth.service.ts`, se enviaba un cuerpo plano sin encapsulamiento JSON formal, provocando que ASP.NET Core rechazara la petición con `HTTP 415 Unsupported Media Type` o un error de deserialización de parámetros.
2. **Excepción interna no controlada en el Backend (C# ASP.NET Core):** En `AuthController.cs`, el método `SwitchHotelContext` invocaba la instrucción:
   ```csharp
   return Forbid("No tiene autorización para acceder a esta sede.");
   ```
   En ASP.NET Core `ControllerBase`, la sobrecarga `Forbid(string)` espera como parámetro el nombre de un **Esquema de Autenticación** (`AuthenticationScheme`), no un mensaje de error textual. Dado que `"No tiene autorización..."` no coincide con ningún esquema registrado, el middleware de autorización arrojaba una excepción no controlada (`InvalidOperationException`), retornando un código de error inesperado `HTTP 500 Internal Server Error`.
3. **Carencia de Validación Previa de Sede:** El backend no comprobaba si la sede existía (`NotFound 404`) o si se encontraba activa en la base de datos antes de generar nuevos tokens.
4. **Desconexión de Sedes Dinámicas para Roles Globales:** El método `GetUserAssignedHotelsAsync` leía de forma exclusiva la tabla de asignaciones estáticas `dbo.HotelUsers`. Los usuarios con roles corporativos (`AdminGlobal`, `Socio`, `Auditor`) no recibían dinámicamente la totalidad de las sedes operativas de la cadena en sus claims de sesión al conmutar de contexto.

#### 6.1.2. Correcciones Implementadas
- **Backend (`AuthController.cs` y `AuthAndHotelDtos.cs`):**
  1. Se creó el DTO formal `SwitchHotelRequestDto` con soporte defensivo en el controlador para aceptar identificadores numéricos primitivos, cadenas y objetos JSON (`{ "hotelId": 2 }`):
     ```csharp
     [HttpPost("switch-hotel")]
     [Authorize]
     public async Task<ActionResult<LoginResponseDto>> SwitchHotelContext([FromBody] System.Text.Json.JsonElement body)
     ```
  2. Se validó la existencia de la sede (`404 Not Found`) y su estado operacional (`400 Bad Request` si la sede está inactiva).
  3. Se reemplazó el `Forbid(string)` defectuoso por:
     ```csharp
     return StatusCode(403, new { message = "No tiene autorización para acceder a esta sede según la matriz RBAC." });
     ```
  4. En `GetUserAssignedHotelsAsync`, se implementó la resolución automática de todas las sedes activas de CONTINENTALIA SAS para `AdminGlobal`, `Socio` y `Auditor`.
- **Frontend (`auth.service.ts` y `main-layout.component.ts`):**
  1. `switchHotel`: Ahora normaliza el identificador numérico y despacha `{ hotelId: Number(hotelId) }` con headers `Content-Type: application/json`.
  2. `main-layout.component.ts`: Se actualizó el selector utilizando enlace nativo `[ngValue]="h.hotelId"` y reactividad de señales de Angular 22.
  3. **Preservación de Contexto ante Fallos:** Si la conmutación falla por red o autorización, la interfaz no muta el selector visualmente, conserva la sede anterior y proyecta la notificación con el motivo real enviado por el servidor.

---

### 6.2. Exportación del Dashboard — Tendencia de Ingresos & Movimientos (7 Días)

#### 6.2.1. Requerimiento y Arquitectura de Datos
Se requirió permitir la exportación de los datos reales que alimentan el gráfico gerencial de **Tendencia de Ingresos & Movimientos (7 Días)**, garantizando que el archivo generado coincida exactamente con las métricas consolidadas o de sede proyectadas en pantalla.

#### 6.2.2. Implementación de Backend (`DashboardController.cs`)
Se implementó el endpoint seguro:
```http
GET /api/dashboard/export-revenue-trend?hotelId={id}
```
- **Formato del Archivo:** Archivo plano CSV codificado en UTF-8 con **Byte Order Mark (BOM: `0xEF, 0xBB, 0xBF`)**, permitiendo que Microsoft Excel abra el archivo directamente en Windows/Mac reconociendo de forma inmediata las tildes, caracteres especiales y columnas separadas por comas.
- **Contenido del Informe:**
  1. Cabecera Corporativa: Razón social (*CONTINENTALIA SAS*), nombre del informe, sede o alcance auditado, usuario que emitió el informe, rol del usuario, fecha y hora de emisión en UTC y rango de los 7 días.
  2. Resumen Ejecutivo de KPIs: Ocupación porcentual, habitaciones activas, recaudo del día actual y recaudo acumulado del mes.
  3. Tabla Transaccional de 7 Días: Fecha (ISO 8601), Sede / Alcance, Check-Ins del día, Check-Outs del día e Ingresos Recaudados (COP).
  4. Fila de Totales Generales del período de 7 días.

#### 6.2.3. Matriz de Permisos y Aislamiento Multitenant de Exportación

| Rol de Usuario | Dashboard Consultable | Alcance de Exportación Autorizado | Protección Multitenant |
| :--- | :--- | :--- | :--- |
| **Socio** | Cadena Consolidada y sedes autorizadas | Consolidado corporativo de toda la cadena o sede específica | Backend valida rol directivo. Acceso consolidado permitido. |
| **Admin Global** | Dashboard global y sede activa | Cadena Consolidada (`hotelId=0`) o Sede Seleccionada (`hotelId=1..4`) | Backend valida autorización de cadena. |
| **Admin Sede** | Exclusivamente su propia sede asignada | Únicamente la sede asignada en su contrato | **Bloqueo 403 Forbidden**: Si manipula `hotelId` hacia otra sede o intenta consultar `hotelId=0`, el servidor aborta la petición. |
| **Auditor** | Vistas de auditoría autorizadas | Reportes corporativos y de cumplimiento | Acceso de solo lectura de auditoría. |
| **Recepcionista** | Sin acceso a dashboard gerencial | **Sin acceso (403 Forbidden)** | Restringido estrictamente a operaciones de Front-Desk. |
| **Housekeeping** | Sin acceso a dashboard gerencial | **Sin acceso (403 Forbidden)** | Restringido estrictamente a gestión de pisos y limpieza. |

---

### 6.3. Parametrización Hotelera del Administrador Global

#### 6.3.1. Inventario de Habitaciones (Físicas vs. Tipologías Comerciales)
Se implementó la separación arquitectónica y funcional entre las **Tipologías de Habitación** (catálogo comercial y tarifario) y las **Habitaciones Físicas** (unidades físicas de inventario asignadas a pisos):

1. **Tipologías de Habitación (`RoomType` / RF-11):**
   - **Identificación:** Código único por sede (ej. `STE-01`, `DBL-02`) y nombre comercial (ej. *Suite Presidencial*, *Doble Superior*).
   - **Capacidad y Atributos:** Capacidad base de huéspedes, capacidad máxima permitida, número y tipo de camas, y superficie en m².
   - **Amenidades y Comodidades:** Catálogo extensible almacenado como etiquetas separadas por comas (Wi-Fi, Aire acondicionado, Jacuzzi, Balcón, Smart TV, Minibar, etc.), proyectadas en la interfaz como insignias (`badges`).
   - **Reglas Comerciales:** Tarifa base por noche (`BasePrice > 0`), política de cancelación asociada y descripción operativa.
   - **Endpoints Implementados:**
     - `GET /api/roomtypes`: Consulta filtrable por estado activo/inactivo (`onlyActive=true/false`).
     - `GET /api/roomtypes/{id}`: Consulta individual por ID.
     - `POST /api/roomtypes`: Creación de nueva tipología con validaciones de precios positivos y unicidad de código.
     - `PUT /api/roomtypes/{id}`: Edición completa de atributos físicos y tarifas comerciales.
     - `PATCH /api/roomtypes/{id}/toggle-status`: Activación/desactivación lógica de tipologías.

2. **Habitaciones Físicas (`Room` / RF-10):**
   - **Atributos:** Número de habitación (`RoomNumber`), piso (`Floor`), vinculación a tipología (`RoomTypeId`), notas operacionales, estado operacional (`Disponible`, `Ocupada`, `Mantenimiento`, `FueraDeServicio`) y estado de limpieza (`Limpia`, `Sucia`, `EnLimpieza`, `EnRevision`).
   - **Edición:** `PUT /api/rooms/{id}` permite actualizar piso, tipología y notas de mantenimiento con validación de concurrencia y unicidad de número por sede.

3. **Mecanismo de Desactivación Lógica Protegida (Regla Especial RF-12):**
   - **Principio de Integridad:** Desactivar una habitación física (`IsActive = false`) **nunca elimina físicamente el registro de SQL Server**. Sus reservas históricas, folios contables y consumos se preservan intactos.
   - **Protección Antidesactivación:** Antes de desactivar una habitación, el backend ejecuta una validación de seguridad operativa:
     ```csharp
     var hasActiveReservations = await _context.Reservations
         .AnyAsync(res => res.RoomId == id &&
             (res.Status == ReservationStatus.CheckIn ||
              (res.Status == ReservationStatus.Confirmada && res.CheckOutDate >= DateTime.UtcNow.Date) ||
              (res.Status == ReservationStatus.Pendiente && res.CheckOutDate >= DateTime.UtcNow.Date)));

     if (hasActiveReservations)
     {
         return BadRequest(new { message = "No se puede desactivar la habitación porque tiene reservas activas o futuras asignadas..." });
     }
     ```
   - **Reactivación:** Se creó el endpoint `PATCH /api/rooms/{id}/activate` para restituir la disponibilidad administrativa de la unidad.

#### 6.3.2. Catálogo de Servicios Adicionales (RF-17)
- **Atributos Administrados:** Nombre del servicio, descripción, categoría (*Minibar, Restaurante, Lavandería, RoomService, Spa, Transporte*), tarifa unitaria en COP y alcance de sede.
- **Endpoints:**
  - `GET /api/services`: Consulta filtrable por sede y estado (`onlyActive`).
  - `POST /api/services`: Creación con validación de tarifa positiva (`Price > 0`).
  - `PUT /api/services/{id}`: Actualización de tarifas, nombres y categorías.
  - `PATCH /api/services/{id}/toggle-status`: Desactivación lógica para retirar servicios del catálogo comercial sin afectar las líneas de consumo previamente facturadas en los folios de huéspedes.

---

### 6.4. Inventario de Archivos Modificados

#### Backend (.NET 9 Web API)
1. [AuthAndHotelDtos.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/src/HotelManagement.Application/DTOs/AuthAndHotelDtos.cs): Creación del DTO `SwitchHotelRequestDto`.
2. [AuthController.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/src/HotelManagement.Api/Controllers/AuthController.cs): Corrección de `SwitchHotelContext` con soporte de payloads numéricos/objetos/cadenas, validación de existencia de hotel (404), estado activo (400), reemplazo de `Forbid(string)` por `StatusCode(403)` y resolución dinámica de sedes autorizadas para roles corporativos.
3. [DashboardController.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/src/HotelManagement.Api/Controllers/DashboardController.cs): Refuerzo multitenant en `GetMetrics` y desarrollo del endpoint de exportación CSV con BOM UTF-8 `ExportRevenueTrend`.
4. [RoomAndPricingDtos.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/src/HotelManagement.Application/DTOs/RoomAndPricingDtos.cs): Creación de `UpdateRoomTypeDto`, `UpdateRoomDto` y `UpdateAdditionalServiceDto`.
5. [RoomsController.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/src/HotelManagement.Api/Controllers/RoomsController.cs): Métodos completos de consulta, creación, edición y conmutación de estado en `RoomTypesController`, y actualización, reactivación y desactivación lógica protegida con regla RF-12 en `RoomsController`.
6. [RatesAndServicesControllers.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/src/HotelManagement.Api/Controllers/RatesAndServicesControllers.cs): Endpoint `PUT /api/services/{id}` y validaciones de creación y edición en `ServicesController`.

#### Frontend (Angular 22)
1. [auth.service.ts](file:///c:/Users/USER/Desktop/hotelmultitenant/frontend/hotel-management-web/src/app/core/services/auth.service.ts): Despacho estructurado de `{ hotelId: Number(hotelId) }` y sincronización reactiva de señales de usuario.
2. [main-layout.component.ts](file:///c:/Users/USER/Desktop/hotelmultitenant/frontend/hotel-management-web/src/app/layout/main-layout.component.ts): Selector de sedes con `[ngValue]="h.hotelId"` y gestión de reversión de selección ante fallos de servidor.
3. [api.service.ts](file:///c:/Users/USER/Desktop/hotelmultitenant/frontend/hotel-management-web/src/app/core/services/api.service.ts): Integración de métodos HTTP para exportación CSV de ingresos, tipologías de habitación (CRUD completo), habitaciones físicas (actualización, reactivación) y servicios del catálogo (actualización).
4. [dashboard.component.ts](file:///c:/Users/USER/Desktop/hotelmultitenant/frontend/hotel-management-web/src/app/pages/dashboard/dashboard.component.ts): Inclusión de botones de descarga "Exportar CSV" en la cabecera y en la sección del gráfico de 7 días.
5. [admin-pages.component.ts](file:///c:/Users/USER/Desktop/hotelmultitenant/frontend/hotel-management-web/src/app/pages/admin/admin-pages.component.ts): Interfaz completa para administración de inventario físico vs. comercial, filtros de estado (*Todas, Solo Activas, Solo Inactivas*), modales reactivos de edición y creación, y catálogo de servicios.
6. [folio.component.ts](file:///c:/Users/USER/Desktop/hotelmultitenant/frontend/hotel-management-web/src/app/pages/folio/folio.component.ts): Adaptación a la sobrecarga de consultas de servicios activos.

#### Pruebas Automatizadas
1. [NewFunctionalRequirementsTests.cs](file:///c:/Users/USER/Desktop/hotelmultitenant/backend/tests/HotelManagement.IntegrationTests/NewFunctionalRequirementsTests.cs): Archivo de pruebas de integración con cobertura completa de conmutación de sede, exportaciones, protección contra manipulación de parámetros, parametrización de tipologías, desactivación protegida de habitaciones y catálogo de servicios.

---

## 7. Resultados Consolidados de la Suite de Pruebas

```
===================================================================================
INFORME FINAL DE EJECUCIÓN DE PRUEBAS AUTOMATIZADAS — CONTINENTALIA SAS
===================================================================================
1. HotelManagement.UnitTests.dll (.NET 9.0)
   - Pruebas superadas: 50 | Fallidas: 0 | Omitidas: 0
   - Duración: 1 segundo
   - Alcance: Dominio, máquinas de estado, validación de tarifas y reglas de negocio.

2. HotelManagement.ConcurrencyTests.dll (.NET 9.0)
   - Pruebas superadas: 2  | Fallidas: 0 | Omitidas: 0
   - Duración: 4 segundos
   - Alcance: Prevención de sobreventa (overbooking) simultánea con bloqueo a nivel BD.

3. HotelManagement.IntegrationTests.dll (.NET 9.0)
   - Pruebas superadas: 34 | Fallidas: 0 | Omitidas: 0
   - Duración: 4 segundos
   - Alcance: Endpoints en vivo, Conmutación de sede Admin Global, Exportación CSV con BOM,
              Aislamiento multitenant RBAC (403 ante manipulación), CRUD de Tipologías,
              Desactivación protegida de habitaciones con reservas activas (RF-12),
              CRUD y soft-delete de servicios comerciales.

4. Compilación del Frontend (Angular 22)
   - `npm run build`: Exit Code 0 (0 errores, 0 advertencias de compilación).
===================================================================================
TOTAL PRUEBAS AUTOMATIZADAS: 86 SUPERADAS / 0 FALLIDAS (100% SUCCESS RATE)
===================================================================================
```

---

## 8. Dictamen Final de Certificación QA

1. **Conmutación de Sede Verificada:** El Administrador Global puede alternar libremente entre las sedes de Bogotá, Cartagena, Medellín y Cali. El contexto de trabajo, indicadores de dashboard y tablas operativas se actualizan instantáneamente sin mostrar mensajes de error.
2. **Exportaciones Fidedignas y Protegidas:** Se garantiza la exportación de la *Tendencia de Ingresos & Movimientos (7 Días)* en formato CSV con BOM para visualización en Excel sin problemas de tildes. La seguridad multitenant impide que un Administrador de Sede acceda a información de otras sedes mediante manipulación de parámetros HTTP.
3. **Parametrización Hotelera Persistente y Robusta:** El inventario físico y comercial permite crear y editar tipologías, habitaciones y servicios adicionales. Todas las operaciones se persisten en SQL Server con validación estricta de tarifas mayores a cero.
4. **Protección de Datos Históricos y Operación en Curso:** La desactivación lógica preserva los folios, consumos y reservas pasadas, impidiendo al mismo tiempo que se desactiven habitaciones que cuenten con huéspedes alojados o reservas confirmadas pendientes.

**Estado del Proyecto:** **APROBADO PARA PRODUCCIÓN (QA CERTIFIED)**.
