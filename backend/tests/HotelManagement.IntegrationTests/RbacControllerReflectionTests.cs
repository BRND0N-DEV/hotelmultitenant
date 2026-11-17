using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using HotelManagement.Api.Controllers;
using HotelManagement.Api.Services;
using HotelManagement.Application.Interfaces;
using HotelManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace HotelManagement.IntegrationTests;

/// <summary>
/// Verifica mediante introspección (Reflection) que todos los controladores y acciones de la API
/// tengan controles explícitos de autenticación/autorización alineados a la matriz RBAC,
/// y valida directamente el comportamiento de TenantAuthorizationHelper.
/// </summary>
public class RbacControllerReflectionTests
{
    private sealed class StubTenantContext : ITenantContext
    {
        public int? CurrentHotelId { get; private set; }
        public bool IsGlobalBypass { get; set; }

        public StubTenantContext(int? hotelId = null, bool isGlobalBypass = false)
        {
            CurrentHotelId = hotelId;
            IsGlobalBypass = isGlobalBypass;
        }

        public void SetHotelContext(int hotelId) => CurrentHotelId = hotelId;
        public void ClearHotelContext() { CurrentHotelId = null; IsGlobalBypass = false; }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; init; } = 1;
        public string? Email { get; init; } = "qa@hotelchain.com";
        public string? Role { get; init; }
        public string? FullName { get; init; } = "QA User";
        public string? IpAddress { get; init; } = "127.0.0.1";
        public bool IsAuthenticated => true;
    }

    private static IEnumerable<(Type Controller, MethodInfo Action, HttpMethodAttribute HttpAttr, bool IsAllowAnonymous, string? EffectiveRoles)> GetAllControllerActions()
    {
        var apiAssembly = typeof(AuthController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name);

        foreach (var controller in controllerTypes)
        {
            var classAuthorize = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var action in actions)
            {
                var httpAttrs = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
                if (httpAttrs.Count == 0) continue;

                var allowAnon = action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null
                                || controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null;
                var methodAuthorize = action.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
                var effectiveRoles = methodAuthorize?.Roles ?? classAuthorize?.Roles;

                foreach (var httpAttr in httpAttrs)
                {
                    yield return (controller, action, httpAttr, allowAnon, effectiveRoles);
                }
            }
        }
    }

    private static HashSet<string> ParseRoles(string? rolesCsv) =>
        string.IsNullOrWhiteSpace(rolesCsv)
            ? new HashSet<string>(StringComparer.Ordinal)
            : rolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void AllControllerEndpoints_ShouldHaveExplicitAuthorizationOrAllowedAnonymousWhitelist()
    {
        var allActions = GetAllControllerActions().ToList();
        allActions.Should().HaveCount(66, "la API expone exactamente 66 acciones en 16 controladores");

        var expectedPublicEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AuthController.Login",
            "AuthController.RefreshToken",
            "ReservationsController.VerifyVoucher"
        };

        var authenticatedWithoutRoleRestriction = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AuthController.GetCurrentUser",
            "AuthController.SwitchHotelContext"
        };

        foreach (var item in allActions)
        {
            var key = $"{item.Controller.Name}.{item.Action.Name}";
            bool hasClassOrMethodAuthorize =
                item.Action.GetCustomAttribute<AuthorizeAttribute>(true) != null ||
                item.Controller.GetCustomAttribute<AuthorizeAttribute>(true) != null;

            if (expectedPublicEndpoints.Contains(key))
            {
                bool isPublic = item.IsAllowAnonymous || !hasClassOrMethodAuthorize;
                isPublic.Should().BeTrue($"{key} es un endpoint público controlado");
                continue;
            }

            item.IsAllowAnonymous.Should().BeFalse($"{key} no debe exponerse como [AllowAnonymous]");
            hasClassOrMethodAuthorize.Should().BeTrue($"{key} debe tener [Authorize]");

            if (!authenticatedWithoutRoleRestriction.Contains(key))
            {
                item.EffectiveRoles.Should().NotBeNullOrWhiteSpace(
                    $"{key} debe restringirse explícitamente mediante Roles para evitar acceso de cualquier rol autenticado");
            }
        }
    }

    [Fact]
    public void AuditorRole_MetadataInvariant_ShouldNeverAppearOnAnyMutatingHttpEndpoint()
    {
        var mutatingMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

        foreach (var item in GetAllControllerActions())
        {
            bool isMutating = item.HttpAttr.HttpMethods.Any(m => mutatingMethods.Contains(m));
            if (!isMutating) continue;

            var roles = ParseRoles(item.EffectiveRoles);
            roles.Should().NotContain(
                UserRole.Auditor,
                $"Auditor es estrictamente solo lectura y no debe figurar en {item.Controller.Name}.{item.Action.Name}");
        }
    }

    [Fact]
    public void SocioRole_MetadataInvariant_ShouldNeverAppearOnAnyMutatingHttpEndpoint()
    {
        var mutatingMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

        foreach (var item in GetAllControllerActions())
        {
            bool isMutating = item.HttpAttr.HttpMethods.Any(m => mutatingMethods.Contains(m));
            if (!isMutating) continue;

            var roles = ParseRoles(item.EffectiveRoles);
            roles.Should().NotContain(
                UserRole.Socio,
                $"Socio solo consulta reportes/tableros/catálogos estratégicos y no debe figurar en {item.Controller.Name}.{item.Action.Name}");
        }
    }

    [Fact]
    public void HousekeepingRole_MetadataInvariant_ShouldBeStrictlyScopedToCleaningTasks()
    {
        var allowedForHousekeeping = new HashSet<string>(StringComparer.Ordinal)
        {
            "HousekeepingController.GetHousekeepingRooms",
            "HousekeepingController.UpdateCleaningStatus"
        };

        foreach (var item in GetAllControllerActions())
        {
            var roles = ParseRoles(item.EffectiveRoles);
            if (!roles.Contains(UserRole.Housekeeping)) continue;

            var key = $"{item.Controller.Name}.{item.Action.Name}";
            allowedForHousekeeping.Should().Contain(
                key,
                $"Housekeeping solo debe acceder al módulo de camarería; {key} violaría mínimo privilegio");
        }
    }

    [Fact]
    public void SeparationOfDuties_AdminGlobal_ShouldNotBePermittedOnOperationalStayMutations()
    {
        var operationalMutations = new HashSet<string>(StringComparer.Ordinal)
        {
            "ReservationsController.CreateReservation",
            "ReservationsController.CheckIn",
            "ReservationsController.CheckOut",
            "ReservationsController.ChangeState",
            "ReservationsController.CancelReservation",
            "FoliosController.AddCharge",
            "FoliosController.AnnulCharge",
            "FoliosController.AddPayment",
            "CashShiftController.OpenShift",
            "CashShiftController.CloseShift",
            "HousekeepingController.GetHousekeepingRooms",
            "HousekeepingController.UpdateCleaningStatus",
            "RoomsController.UpdateRoomStatus"
        };

        foreach (var item in GetAllControllerActions())
        {
            var key = $"{item.Controller.Name}.{item.Action.Name}";
            if (!operationalMutations.Contains(key)) continue;

            var roles = ParseRoles(item.EffectiveRoles);
            roles.Should().NotContain(
                UserRole.AdminGlobal,
                $"por segregación de funciones (SoD), AdminGlobal no debe ejecutar ni operar directamente {key}");
        }
    }

    [Fact]
    public void TenantAuthorizationHelper_AdminSede_TamperingHotelId_ShouldReturnForbidden403()
    {
        var tenant = new StubTenantContext(hotelId: 1);
        var currentUser = new StubCurrentUserService { Role = UserRole.AdminSede };

        var error = TenantAuthorizationHelper.ResolveAuthorizedHotelId(tenant, currentUser, requestedHotelId: 2, out _);

        var objectResult = error.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void TenantAuthorizationHelper_AdminSede_MissingHotelContext_ShouldReturnForbidden403()
    {
        var tenant = new StubTenantContext(hotelId: null);
        var currentUser = new StubCurrentUserService { Role = UserRole.AdminSede };

        var error = TenantAuthorizationHelper.ResolveAuthorizedHotelId(tenant, currentUser, requestedHotelId: null, out _);

        var objectResult = error.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void TenantAuthorizationHelper_GlobalRole_WithConsolidatedZero_ShouldEnableGlobalBypassOnlyWhenAllowed()
    {
        var tenantAllowed = new StubTenantContext(hotelId: 1);
        var globalUser = new StubCurrentUserService { Role = UserRole.Socio };

        var noError = TenantAuthorizationHelper.ResolveAuthorizedHotelId(tenantAllowed, globalUser, requestedHotelId: 0, out int targetHotelId, allowConsolidatedZero: true);

        noError.Should().BeNull();
        targetHotelId.Should().Be(0);
        tenantAllowed.IsGlobalBypass.Should().BeTrue();

        var tenantDisallowed = new StubTenantContext(hotelId: 1);
        var badRequest = TenantAuthorizationHelper.ResolveAuthorizedHotelId(tenantDisallowed, globalUser, requestedHotelId: 0, out _, allowConsolidatedZero: false);
        badRequest.Should().BeOfType<BadRequestObjectResult>();
    }
}
