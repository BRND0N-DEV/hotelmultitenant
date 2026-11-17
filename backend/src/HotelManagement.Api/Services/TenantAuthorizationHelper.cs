using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Enums;

namespace HotelManagement.Api.Services;

/// <summary>
/// Centraliza la validación de contexto multitenant y prevención de manipulación de parámetros hotelId (RF-07 / OWASP BOLA).
/// </summary>
public static class TenantAuthorizationHelper
{
    public static bool IsGlobalRole(string? role) =>
        role is UserRole.AdminGlobal or UserRole.Socio or UserRole.Auditor;

    public static ActionResult? ResolveAuthorizedHotelId(
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        int? requestedHotelId,
        out int targetHotelId,
        bool allowConsolidatedZero = false)
    {
        if (IsGlobalRole(currentUser.Role))
        {
            if (requestedHotelId.HasValue)
            {
                if (requestedHotelId.Value <= 0)
                {
                    if (allowConsolidatedZero)
                    {
                        tenantContext.IsGlobalBypass = true;
                        targetHotelId = 0;
                        return null;
                    }

                    targetHotelId = 0;
                    return new BadRequestObjectResult(new { message = "Identificador de sede inválido." });
                }

                tenantContext.SetHotelContext(requestedHotelId.Value);
                targetHotelId = requestedHotelId.Value;
                return null;
            }

            targetHotelId = tenantContext.CurrentHotelId ?? 1;
            return null;
        }

        if (!tenantContext.CurrentHotelId.HasValue || tenantContext.CurrentHotelId.Value <= 0)
        {
            targetHotelId = 0;
            return new ObjectResult(new { message = "Acceso denegado: No cuenta con una sede asignada en el contexto actual." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        if (requestedHotelId.HasValue && requestedHotelId.Value != tenantContext.CurrentHotelId.Value)
        {
            targetHotelId = 0;
            return new ObjectResult(new { message = "Acceso denegado: No tiene autorización para consultar o modificar recursos de otra sede." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        targetHotelId = tenantContext.CurrentHotelId.Value;
        return null;
    }

    public static bool HasTenantAccess(ITenantContext tenantContext, int entityHotelId) =>
        tenantContext.IsGlobalBypass || (tenantContext.CurrentHotelId.HasValue && entityHotelId == tenantContext.CurrentHotelId.Value);
}
