using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Entities;
using HotelManagement.Domain.Enums;
using HotelManagement.Domain.Exceptions;

namespace HotelManagement.Api.Middlewares;

// 1. CORRELATION ID MIDDLEWARE (RNF-09)
public class CorrelationIdMiddleware
{
    private const string CorrelationHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(CorrelationHeader, out var correlationId) || string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers[CorrelationHeader] = correlationId;
        }

        context.Response.Headers[CorrelationHeader] = correlationId;
        context.TraceIdentifier = correlationId.ToString();

        await _next(context);
    }
}

// 2. TENANT RESOLUTION & MULTITENANCY ENFORCEMENT MIDDLEWARE (RF-05, RF-07, RNF-10)
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, IHotelDbContext dbContext)
    {
        // 1. Verificar si el endpoint es público o administrativo global
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Rutas que no requieren aislamiento por sede o son de autenticación
        if (path.StartsWith("/api/auth") || path.StartsWith("/swagger") || path.StartsWith("/health"))
        {
            await _next(context);
            return;
        }

        // 2. Extraer HotelId: primero del encabezado X-Hotel-Id, luego del Claim del Token JWT
        int? resolvedHotelId = null;

        if (context.Request.Headers.TryGetValue("X-Hotel-Id", out var hotelHeader) && int.TryParse(hotelHeader, out var hId))
        {
            resolvedHotelId = hId;
        }
        else
        {
            var claim = context.User.FindFirst("HotelId")?.Value;
            if (int.TryParse(claim, out var cId))
            {
                resolvedHotelId = cId;
            }
        }

        var userRole = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // Socio y AdminGlobal pueden omitir el filtro de tenant para reportes consolidados si no se especifica hotel
        if ((userRole == UserRole.AdminGlobal || userRole == UserRole.Socio || userRole == UserRole.Auditor) && resolvedHotelId == null)
        {
            tenantContext.IsGlobalBypass = true;
        }
        else if (resolvedHotelId.HasValue)
        {
            // Validar que el usuario tenga asignado este hotel o sea AdminGlobal/Socio/Auditor
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdClaim, out var userId) && userRole != UserRole.AdminGlobal && userRole != UserRole.Socio && userRole != UserRole.Auditor)
                {
                    bool isAssigned = await dbContext.HotelUsers
                        .AnyAsync(hu => hu.UserId == userId && hu.HotelId == resolvedHotelId.Value);

                    if (!isAssigned)
                    {
                        // RF-07: Intento de acceder a recursos de otra sede responde 404 (No Encontrado)
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        context.Response.ContentType = "application/problem+json";
                        var problem = new ProblemDetails
                        {
                            Status = (int)HttpStatusCode.NotFound,
                            Title = "Recurso no encontrado",
                            Detail = "El recurso solicitado no existe o no se encuentra disponible."
                        };
                        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                        return;
                    }
                }
            }

            tenantContext.SetHotelContext(resolvedHotelId.Value);
        }

        await _next(context);
    }
}

// 3. EXCEPTION HANDLING MIDDLEWARE (RFC 7807 ProblemDetails)
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado procesando la solicitud: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = HttpStatusCode.InternalServerError;
        var title = "Error interno del servidor";
        var detail = exception.Message;

        if (exception.ToString().Contains("CONFLICTO DE OVERBOOKING") ||
            exception.GetBaseException().Message.Contains("CONFLICTO DE OVERBOOKING") ||
            exception.ToString().Contains("La habitación seleccionada ya tiene una reserva activa") ||
            exception.ToString().Contains("transient failure"))
        {
            statusCode = HttpStatusCode.Conflict; // 409
            title = "Conflicto de Overbooking detectado en base de datos";
            detail = "La habitación ya cuenta con una reserva activa en las fechas seleccionadas. Operación bloqueada por el motor de base de datos (RF-24 / RNF-11).";
        }
        else
        {
            switch (exception)
            {
            case InvalidStateTransitionException stateEx:
                statusCode = HttpStatusCode.UnprocessableEntity; // 422
                title = "Transición de estado inválida";
                detail = stateEx.Message;
                break;

            case OverbookingException overbookEx:
                statusCode = HttpStatusCode.Conflict; // 409
                title = "Conflicto de disponibilidad (Overbooking prevenido)";
                detail = overbookEx.Message;
                break;

            case DbUpdateException dbUpdateEx when dbUpdateEx.GetBaseException().Message.Contains("CONFLICTO DE OVERBOOKING") || dbUpdateEx.ToString().Contains("CONFLICTO DE OVERBOOKING"):
                statusCode = HttpStatusCode.Conflict; // 409
                title = "Conflicto de Overbooking detectado en base de datos";
                detail = "La habitación ya cuenta con una reserva activa en las fechas seleccionadas. Operación bloqueada por el motor de base de datos (RF-24 / RNF-11).";
                break;

            case DbUpdateConcurrencyException:
                statusCode = HttpStatusCode.Conflict; // 409
                title = "Conflicto de concurrencia";
                detail = "El recurso fue modificado por otro usuario de forma concurrente. Por favor recargue y reintente la operación.";
                break;

            case FolioBalanceNonZeroException folioEx:
                statusCode = HttpStatusCode.BadRequest; // 400
                title = "Saldo pendiente en el folio";
                detail = folioEx.Message;
                break;

            case TenantAccessViolationException:
            case EntityNotFoundException:
                statusCode = HttpStatusCode.NotFound; // 404
                title = "Recurso no encontrado";
                detail = "El recurso solicitado no existe en la sede actual.";
                break;

            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Forbidden; // 403
                title = "Acceso denegado";
                detail = "No tiene permisos suficientes para ejecutar esta operación dentro de la sede.";
                break;

            case DomainException domEx:
                statusCode = HttpStatusCode.BadRequest; // 400
                title = "Error en reglas de negocio";
                detail = domEx.Message;
                break;

            default:
                break;
        }
    }

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        if (context.Response.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
        {
            problemDetails.Extensions["correlationId"] = correlationId.ToString();
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }
}

// 4. HTTP AUDIT MIDDLEWARE (RF-54, RF-55)
public class HttpAuditMiddleware
{
    private readonly RequestDelegate _next;

    public HttpAuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Registrar consultas sensibles, inter-sedes y operaciones de mutación
        bool shouldAudit = path.Contains("cross-hotel") ||
                           path.Contains("anonymize") ||
                           context.Request.Method == "POST" ||
                           context.Request.Method == "PUT" ||
                           context.Request.Method == "DELETE";

        await _next(context);

        if (shouldAudit && context.Response.StatusCode < 400)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IHotelDbContext>();
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();

                var audit = new AuditLog
                {
                    HotelId = tenantContext.CurrentHotelId,
                    UserId = currentUserService.UserId,
                    Level = AuditLevel.Http,
                    Action = $"{context.Request.Method} {path}",
                    EntityName = "HttpRequest",
                    EntityId = context.Response.StatusCode.ToString(),
                    Timestamp = DateTime.UtcNow,
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    CorrelationId = context.TraceIdentifier
                };

                dbContext.AuditLogs.Add(audit);
                await dbContext.SaveChangesAsync();
            }
            catch
            {
                // La auditoría no debe detener el flujo HTTP
            }
        }
    }
}
