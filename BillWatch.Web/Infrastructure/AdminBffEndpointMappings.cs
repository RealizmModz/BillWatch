using BillWatch.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BillWatch.Web.Infrastructure;

public static class AdminBffEndpointMappings
{
    public static IEndpointRouteBuilder
        MapBillWatchAdminBffEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var adminBff = endpoints.MapGroup(
                "/bff/admin")
            .RequireAuthorization();

        adminBff.MapGet(
            "/users",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                int? skip,
                int? take) =>
            {
                var safeSkip = Math.Max(skip ?? 0, 0);
                var safeTake = Math.Clamp(take ?? 50, 1, 100);

                return await proxyService.ForwardGetAsync(
                    context,
                    $"/api/admin/users?skip={safeSkip}&take={safeTake}",
                    context.RequestAborted);
            });

        adminBff.MapGet(
            "/access-keys",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                int? skip,
                int? take) =>
            {
                var safeSkip = Math.Max(skip ?? 0, 0);
                var safeTake = Math.Clamp(take ?? 50, 1, 100);

                return await proxyService.ForwardGetAsync(
                    context,
                    $"/api/admin/access-keys?skip={safeSkip}&take={safeTake}",
                    context.RequestAborted);
            });

        adminBff.MapGet(
            "/audit-log",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                Guid? targetUserId,
                int? skip,
                int? take) =>
            {
                if (targetUserId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                var safeSkip = Math.Max(skip ?? 0, 0);
                var safeTake = Math.Clamp(take ?? 50, 1, 100);
                var requestUri =
                    $"/api/admin/audit-log?skip={safeSkip}&take={safeTake}";

                if (targetUserId.HasValue)
                {
                    requestUri +=
                        $"&targetUserId={targetUserId.Value:D}";
                }

                return await proxyService.ForwardGetAsync(
                    context,
                    requestUri,
                    context.RequestAborted);
            });

        adminBff.MapPost(
            "/users/{targetUserId:guid}/roles/{roleName}",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid targetUserId,
                string roleName) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (targetUserId == Guid.Empty ||
                    !TryNormalizeMutableStaffRole(
                        roleName,
                        out var normalizedRole))
                {
                    return Results.BadRequest();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/admin/users/{targetUserId:D}/roles/{normalizedRole}",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        adminBff.MapDelete(
            "/users/{targetUserId:guid}/roles/{roleName}",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid targetUserId,
                string roleName) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (targetUserId == Guid.Empty ||
                    !TryNormalizeMutableStaffRole(
                        roleName,
                        out var normalizedRole))
                {
                    return Results.BadRequest();
                }

                return await proxyService.ForwardDeleteAsync(
                    context,
                    $"/api/admin/users/{targetUserId:D}/roles/{normalizedRole}",
                    context.RequestAborted);
            });

        adminBff.MapPost(
            "/users/{targetUserId:guid}/entitlements",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                Guid targetUserId,
                AdminGrantEntitlementRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (targetUserId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Post,
                    $"/api/admin/users/{targetUserId:D}/entitlements",
                    request,
                    context.RequestAborted);
            });

        adminBff.MapPost(
            "/users/{targetUserId:guid}/entitlements/{entitlementId:guid}/revoke",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid targetUserId,
                Guid entitlementId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (targetUserId == Guid.Empty ||
                    entitlementId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/admin/users/{targetUserId:D}/entitlements/{entitlementId:D}/revoke",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        adminBff.MapPut(
            "/users/{targetUserId:guid}/programs/{programName}",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                Guid targetUserId,
                string programName,
                AdminProgramMembershipRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (targetUserId == Guid.Empty ||
                    !TryNormalizeProgram(
                        programName,
                        out var normalizedProgram))
                {
                    return Results.BadRequest();
                }

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Put,
                    $"/api/admin/users/{targetUserId:D}/programs/{normalizedProgram}",
                    request,
                    context.RequestAborted);
            });

        adminBff.MapPost(
            "/access-keys",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                AdminCreateAccessKeyRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Post,
                    "/api/admin/subscription/access-keys",
                    request,
                    context.RequestAborted);
            });

        adminBff.MapPost(
            "/access-keys/{accessKeyId:guid}/revoke",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid accessKeyId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (accessKeyId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/admin/subscription/access-keys/{accessKeyId:D}/revoke",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        return endpoints;
    }

    private static bool TryNormalizeMutableStaffRole(
        string roleName,
        out string normalizedRole)
    {
        if (string.Equals(
                roleName,
                "Admin",
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedRole = "Admin";
            return true;
        }

        if (string.Equals(
                roleName,
                "Moderator",
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedRole = "Moderator";
            return true;
        }

        normalizedRole = string.Empty;
        return false;
    }

    private static bool TryNormalizeProgram(
        string programName,
        out string normalizedProgram)
    {
        if (string.Equals(
                programName,
                "BetaTester",
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedProgram = "BetaTester";
            return true;
        }

        if (string.Equals(
                programName,
                "InternalTester",
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedProgram = "InternalTester";
            return true;
        }

        normalizedProgram = string.Empty;
        return false;
    }
}

public sealed record AdminGrantEntitlementRequest(
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess);

public sealed record AdminProgramMembershipRequest(
    bool IsActive,
    DateTimeOffset? EndsAtUtc);

public sealed record AdminCreateAccessKeyRequest(
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    DateTimeOffset? ExpiresAtUtc);
