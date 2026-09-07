using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace BillWatch.Web.Infrastructure;

public static class AntiforgeryBoundaryExtensions
{
    public static IApplicationBuilder UseBillWatchAntiforgeryBoundary(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(
            async (context, next) =>
            {
                if (!RequiresAntiforgeryValidation(context))
                {
                    await next();
                    return;
                }

                var antiforgery =
                    context.RequestServices.GetRequiredService<IAntiforgery>();

                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    context.Response.StatusCode =
                        StatusCodes.Status400BadRequest;

                    return;
                }

                await next();
            });
    }

    private static bool RequiresAntiforgeryValidation(
        HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint routeEndpoint)
        {
            return false;
        }

        var routeTemplate =
            routeEndpoint.RoutePattern.RawText;

        if (string.IsNullOrWhiteSpace(routeTemplate) ||
            (!routeTemplate.StartsWith(
                 "/bff",
                 StringComparison.OrdinalIgnoreCase) &&
             !routeTemplate.StartsWith(
                 "/auth",
                 StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var method = context.Request.Method;

        return !HttpMethods.IsGet(method) &&
               !HttpMethods.IsHead(method) &&
               !HttpMethods.IsOptions(method) &&
               !HttpMethods.IsTrace(method);
    }
}
