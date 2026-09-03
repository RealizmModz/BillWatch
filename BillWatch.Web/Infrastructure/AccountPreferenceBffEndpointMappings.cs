using BillWatch.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BillWatch.Web.Infrastructure;

public static class AccountPreferenceBffEndpointMappings
{
    public static IEndpointRouteBuilder MapBillWatchAccountPreferenceBffEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var bff = endpoints.MapGroup("/bff/account/preferences")
            .RequireAuthorization();

        bff.MapGet(
            string.Empty,
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/account/preferences",
                    context.RequestAborted));

        bff.MapPut(
            string.Empty,
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                AccountPreferenceUpdateRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Put,
                    "/api/account/preferences",
                    request,
                    context.RequestAborted);
            });

        return endpoints;
    }
}

public sealed record AccountPreferenceUpdateRequest(
    string TimestampDisplayMode);
