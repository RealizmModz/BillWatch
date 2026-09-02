using BillWatch.Web.Services;

namespace BillWatch.Web.Infrastructure;

public static class AdminBffEndpointMappings
{
    public static IEndpointRouteBuilder
        MapBillWatchAdminBffEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        var adminBff =
            endpoints.MapGroup(
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
                var safeSkip =
                    Math.Max(
                        skip ?? 0,
                        0);

                var safeTake =
                    Math.Clamp(
                        take ?? 50,
                        1,
                        100);

                return await proxyService
                    .ForwardGetAsync(
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
                var safeSkip =
                    Math.Max(
                        skip ?? 0,
                        0);

                var safeTake =
                    Math.Clamp(
                        take ?? 50,
                        1,
                        100);

                return await proxyService
                    .ForwardGetAsync(
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
                if (targetUserId ==
                    Guid.Empty)
                {
                    return Results.BadRequest();
                }

                var safeSkip =
                    Math.Max(
                        skip ?? 0,
                        0);

                var safeTake =
                    Math.Clamp(
                        take ?? 50,
                        1,
                        100);

                var requestUri =
                    $"/api/admin/audit-log?skip={safeSkip}&take={safeTake}";

                if (targetUserId.HasValue)
                {
                    requestUri +=
                        $"&targetUserId={targetUserId.Value:D}";
                }

                return await proxyService
                    .ForwardGetAsync(
                        context,
                        requestUri,
                        context.RequestAborted);
            });

        return endpoints;
    }
}
