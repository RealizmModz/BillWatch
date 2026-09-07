using BillWatch.Web.Services;

namespace BillWatch.Web.Infrastructure;

public static class ExternalIdentityManagementEndpointMappings
{
    public static IEndpointRouteBuilder MapBillWatchExternalIdentityManagementEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/bff/account/external",
                async (
                    HttpContext context,
                    BillWatchBffProxyService proxyService) =>
                    await proxyService.ForwardGetAsync(
                        context,
                        "/api/auth/external",
                        context.RequestAborted))
            .RequireAuthorization();

        return endpoints;
    }
}
