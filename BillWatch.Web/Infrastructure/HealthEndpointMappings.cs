namespace BillWatch.Web.Infrastructure;

public static class HealthEndpointMappings
{
    public static IEndpointRouteBuilder
        MapBillWatchHealthEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        endpoints.MapGet(
                "/health/live",
                () =>
                    Results.Ok(
                        new
                        {
                            status =
                                "live"
                        }))
            .AllowAnonymous();

        endpoints.MapGet(
                "/health/ready",
                async (
                    IHttpClientFactory httpClientFactory,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        using var timeout =
                            CancellationTokenSource
                                .CreateLinkedTokenSource(
                                    cancellationToken);

                        timeout.CancelAfter(
                            TimeSpan.FromSeconds(5));

                        var client =
                            httpClientFactory
                                .CreateClient(
                                    "BillWatchApi");

                        using var response =
                            await client.GetAsync(
                                "/health/ready",
                                HttpCompletionOption
                                    .ResponseHeadersRead,
                                timeout.Token);

                        if (!response.IsSuccessStatusCode)
                        {
                            return Results.StatusCode(
                                StatusCodes
                                    .Status503ServiceUnavailable);
                        }

                        var body =
                            await response.Content
                                .ReadAsStringAsync(
                                    timeout.Token);

                        var normalized =
                            string.Concat(
                                body.Where(
                                    character =>
                                        !char.IsWhiteSpace(
                                            character)));

                        if (!string.Equals(
                                normalized,
                                "{\"status\":\"ready\"}",
                                StringComparison.Ordinal))
                        {
                            return Results.StatusCode(
                                StatusCodes
                                    .Status503ServiceUnavailable);
                        }

                        return Results.Ok(
                            new
                            {
                                status =
                                    "ready"
                            });
                    }
                    catch (OperationCanceledException)
                    {
                        return Results.StatusCode(
                            StatusCodes
                                .Status503ServiceUnavailable);
                    }
                    catch (HttpRequestException)
                    {
                        return Results.StatusCode(
                            StatusCodes
                                .Status503ServiceUnavailable);
                    }
                })
            .AllowAnonymous();

        return endpoints;
    }
}