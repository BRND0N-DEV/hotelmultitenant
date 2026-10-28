# INFORME DE ESTRATEGIA DE CALIDAD DE SOFTWARE (ISO/IEC 25010) Y SHIFT-LEFT
## Sistema de Gestión Hotelera Integral Multitenant (PMS)

---

## 1. Marco de Calidad ISO/IEC 25010

El desarrollo y evaluación del sistema se estructura en torno a las 8 características de calidad de producto de software definidas por la norma ISO/IEC 25010:

### 1.1. Adecuación Funcional
- **Completitud Funcional:** Implementación del 100% de los requisitos funcionales vigentes (RF-01 a RF-55) y de las 26 historias de usuario principales (HU-01 a HU-23, HU-25 a HU-27). La HU-24 (Portal del huésped) queda documentada para fase posterior según alcance pactado.
- **Corrección Funcional:** Precisión en los cálculos financieros (tarifas, temporadas, promociones, recargos, impuestos, contra-asientos y saldos de folio).
- **Pertinencia Funcional:** Alineación de los flujos de recepción, housekeeping y gerencia con la operación real de la cadena de cuatro hoteles.

### 1.2. Eficiencia del Desempeño
- **Comportamiento Temporal:**
  - Consulta de disponibilidad: p95 < 800 ms (RNF-01).
  - Peticiones REST estándar: p95 < 300 ms.
- **Utilización de Recursos:** Optimización de consultas con índices filtrados en SQL Server; streaming de reportes en Excel/PDF para evitar consumo excesivo de memoria RAM (HU-26).
- **Capacidad:** Soporte demostrado para al menos 50 usuarios concurrentes sin degradación ni pérdida de integridad (RNF-02).

### 1.3. Compatibilidad
- **Coexistencia:** Operación multi-instancia en el mismo servidor de base de datos sin colisión entre sedes.
- **Interoperabilidad:** API RESTful estandarizada basada en contratos OpenAPI (Swagger) y formatos JSON estandarizados con RFC 7807 (ProblemDetails).

### 1.4. Capacidad de Interacción (Usabilidad)
- **Reconocibilidad y Aprendizaje:** Interfaz intuitiva, menú lateral con íconos claros, estados de habitaciones y reservas visualmente diferenciados por color y texto.
- **Operabilidad y Accesibilidad:** Navegación fluida por teclado, contrastes WCAG 2.1 AA en pantallas críticas (RNF-06), debounce de 300 ms en búsquedas (RNF-15).
- **Protección contra Errores:** Bloqueo de acciones no permitidas en la interfaz (ej. check-out con saldo pendiente, check-in con habitación sucia), confirmaciones en acciones críticas y validaciones reactivas en formularios.

### 1.5. Fiabilidad
- **Madurez y Tolerancia a Fallos:** Envío asíncrono de correos desacoplado mediante background worker y política de reintentos (Polly) de hasta 3 intentos; los fallos de terceros no comprometen la transacción de reserva (RF-44).
- **Consistencia Transaccional:** Prevención absoluta de overbooking mediante trigger atómico en SQL Server (RNF-11) y transacciones serializables en operaciones de reserva.
- **Recuperabilidad:** Script y procedimiento probado para respaldo y recuperación diaria de base de datos (RNF-04).

### 1.6. Seguridad
- **Confidencialidad:** Hashing de contraseñas con BCrypt (costo 11) o Argon2id; nunca se almacenan ni registran contraseñas en texto plano (RNF-13). Tokens JWT con vigencia de 15 a 30 minutos y Refresh Tokens rotativos con detección y revocación de replay (RNF-12).
- **Integridad:** Aislamiento multitenant reforzado: un usuario no puede consultar ni modificar datos de otra sede; intentos indebidos responden HTTP 404 (RF-07).
- **No Repudio y Trazabilidad:** Auditoría en 3 niveles (Negocio, Técnico con cambios de entidades, y HTTP para consultas inter-sedes y autenticación) (RF-54, RF-55, RNF-16).
- **Privacidad (Habeas Data):** Captura obligatoria de consentimiento y anonimización irreversible respetando el historial contable (RF-45, RF-47).

### 1.7. Mantenibilidad
- **Modularidad:** Clean Architecture con aislamiento de reglas de negocio en la capa Domain, libre de dependencias de infraestructura.
- **Reusabilidad:** Componentes y directivas de Angular reutilizables; servicios transversales desacoplados.
- **Capacidad de Ser Probado (Testabilidad):** Inyección de dependencias en todas las capas, facilitando la cobertura de pruebas unitarias superior al 80% en Domain y Application (RNF-08).

### 1.8. Flexibilidad (Portabilidad)
- **Adaptabilidad:** Frontend responsive compatible con escritorios (1920x1080, 1366x768) y tabletas táctiles de 10" para housekeeping (RNF-14).
- **Facilidad de Instalación:** Scripts reproducibles en SQL Server, migraciones de EF Core, configuración modular mediante `appsettings.json` y variables de entorno documentadas en `.env.example`.

---

## 2. Enfoque Shift-Left de Pruebas

El enfoque **Shift-Left** incorpora la verificación de calidad desde la fase de diseño hasta la entrega continua:

```
[Diseño / Requisitos] ──► [Pruebas Unitarias de Dominio] ──► [Pruebas de Integración y API] ──► [Pruebas de Concurrencia] ──► [Pruebas E2E Frontend]
   • RTM y Reglas              • PricingEngine                   • Multitenancy (404)           • Overbooking simulado        • Flujo Check-In / Out
   • Validación contratos      • StateMachine                    • Transiciones (422)           • RowVersion (409)            • Housekeeping táctil
                               • FolioCalculator                 • Idempotencia                 • Reintentos concurrentes      • Exportación reportes
```

1. **Pruebas Unitarias (Domain & Application):**
   - Verificación de la máquina de estados de reservas (transiciones válidas e inválidas 422).
   - Cálculo de tarifas con temporadas traslapadas, fines de semana y promociones.
   - Cálculo del folio, impuestos, contra-asientos de corrección y saldos netos.
   - Algoritmo de bloqueo por intentos fallidos y rotación de tokens.
2. **Pruebas de Integración y Aislamiento:**
   - Verificación de Global Query Filters en EF Core: intento de consultar reservas de Hotel B desde contexto de Hotel A retorna 404.
   - Consulta inter-sedes: verificación de que solo expone cantidades y tarifas sin datos de huéspedes.
   - Verificación del comportamiento del header `Idempotency-Key`.
3. **Pruebas de Concurrencia y Consistencia:**
   - Dos solicitudes simultáneas intentando reservar la misma habitación para el mismo período: una se aprueba, la segunda es rechazada con HTTP 409 o falla por trigger de overbooking.
4. **Pruebas de Frontend:**
   - Validaciones reactivas en formularios.
   - Interceptor de autenticación y manejo de errores.
   - Flujo responsive de recepción y housekeeping táctil.

---

## 3. Módulo de Matriz de Permisos (RBAC), Autorización y Trazabilidad de Datos

En cumplimiento de los requerimientos de auditoría de seguridad y gobernanza de datos para CONTINENTALIA SAS, se ha incorporado el módulo integral de verificación de control de acceso y persistencia.

El detalle técnico exhaustivo, las matrices de pruebas, la trazabilidad de persistencia de extremo a extremo (Frontend -> API -> SQL Server), el inventario de datos semilla y las cuatro (4) decisiones de negocio para la Junta Directiva se encuentran documentados formalmente en:
- [Auditoría Integral de Permisos y Trazabilidad de Datos](file:///c:/Users/USER/Desktop/hotelmultitenant/docs/auditoria_permisos_fuentes_datos.md)

---

## 4. Auditoría Integral de Defectos y Verificación QA Funcional

Para consultar el diagnóstico de causa raíz, las modificaciones efectuadas en cada capa arquitectónica, las 71 pruebas automatizadas y las evidencias de verificación en caliente de los 8 defectos funcionales corregidos, remitirse a:
- [Informe de Auditoría QA Funcional y Corrección Integral de Defectos](file:///c:/Users/USER/Desktop/hotelmultitenant/docs/informe_auditoria_qa_defectos.md)


