using System.Text.Json;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BillWatch.Web.Infrastructure;

public static class BffEndpointMappings
{
    private const long StatementFileSizeLimit =
        15L * 1024 * 1024;

    private const long StatementMultipartBodyLimit =
        16L * 1024 * 1024;

    public static IEndpointRouteBuilder MapBillWatchBffEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var bff = endpoints.MapGroup("/bff")
            .RequireAuthorization();

        bff.MapGet(
            "/antiforgery",
            (HttpContext context, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);

                if (string.IsNullOrWhiteSpace(tokens.RequestToken))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                return Results.Ok(
                    new
                    {
                        requestToken = tokens.RequestToken
                    });
            });

        bff.MapGet(
            "/subscription",
            async (HttpContext context, BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/subscription",
                    context.RequestAborted));

        bff.MapGet(
            "/subscription/plans",
            async (HttpContext context, BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/subscription/plans",
                    context.RequestAborted));

        bff.MapPost(
            "/subscription/checkout",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                SubscriptionCheckoutRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Post,
                    "/api/subscription/checkout",
                    request,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/subscription/billing-portal",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                return await proxyService.ForwardPostAsync(
                    context,
                    "/api/subscription/billing-portal",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/subscription/sync",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                return await proxyService.ForwardPostAsync(
                    context,
                    "/api/subscription/sync",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/subscription/access-keys/redeem",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                SubscriptionRedemptionRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Post,
                    "/api/subscription/access-keys/redeem",
                    request,
                    context.RequestAborted);
            });

        bff.MapGet(
            "/bill-streams",
            async (HttpContext context, BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/bill-streams",
                    context.RequestAborted));

        bff.MapGet(
            "/bill-streams/{billStreamId:guid}",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                Guid billStreamId) =>
            {
                if (billStreamId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardGetAsync(
                    context,
                    $"/api/bill-streams/{billStreamId}",
                    context.RequestAborted);
            });

        bff.MapGet(
            "/bank-accounts",
            async (HttpContext context, BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/bank-accounts",
                    context.RequestAborted));

        bff.MapGet(
            "/bank-connections",
            async (HttpContext context, BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/bank-connections",
                    context.RequestAborted));

        bff.MapGet(
            "/bank-transactions",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                int? take) =>
            {
                var safeTake = Math.Clamp(
                    take ?? 100,
                    1,
                    500);

                return await proxyService.ForwardGetAsync(
                    context,
                    $"/api/bank-transactions?take={safeTake}",
                    context.RequestAborted);
            });

        bff.MapGet(
            "/alerts",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                bool? includeDismissed,
                bool? unreadOnly,
                int? take) =>
            {
                var safeTake = Math.Clamp(
                    take ?? 50,
                    1,
                    100);

                var requestUri =
                    "/api/alerts" +
                    $"?includeDismissed={(includeDismissed ?? false).ToString().ToLowerInvariant()}" +
                    $"&unreadOnly={(unreadOnly ?? false).ToString().ToLowerInvariant()}" +
                    $"&take={safeTake}";

                return await proxyService.ForwardGetAsync(
                    context,
                    requestUri,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/alerts/{alertId:guid}/read",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid alertId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (alertId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/alerts/{alertId}/read",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/alerts/{alertId:guid}/dismiss",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid alertId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (alertId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/alerts/{alertId}/dismiss",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        bff.MapGet(
            "/account/export",
            async (HttpContext context, BillWatchBffProxyService proxyService) =>
                await proxyService.ForwardDownloadAsync(
                    context,
                    "/api/account/export",
                    "billwatch-data-export.json",
                    "application/json; charset=utf-8",
                    context.RequestAborted));

        bff.MapDelete(
            "/account",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                DeleteAccountBffRequest? request;

                try
                {
                    request = await context.Request.ReadFromJsonAsync<DeleteAccountBffRequest>(
                        cancellationToken: context.RequestAborted);
                }
                catch (JsonException)
                {
                    return Results.BadRequest();
                }
                catch (BadHttpRequestException)
                {
                    return Results.BadRequest();
                }
                catch (NotSupportedException)
                {
                    return Results.BadRequest();
                }

                if (request is null)
                {
                    return Results.BadRequest();
                }

                if (!string.Equals(
                        request.Confirmation,
                        "DELETE",
                        StringComparison.Ordinal))
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "Type DELETE to confirm permanent account deletion."
                        });
                }

                if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "Enter your current password to confirm permanent account deletion."
                        });
                }

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Delete,
                    "/api/account",
                    request,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/plaid/link-session",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                return await proxyService.ForwardPostAsync(
                    context,
                    "/api/plaid/link-token",
                    includeEmptyJsonBody: true,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/plaid/connections/{connectionId:guid}/update-link-session",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid connectionId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (connectionId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/plaid/connections/{connectionId}/update-link-token",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        bff.MapPost(
            "/plaid/link-session/{sessionId:guid}/complete",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid sessionId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (sessionId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardPostAsync(
                    context,
                    $"/api/plaid/link-session/{sessionId}/complete",
                    includeEmptyJsonBody: false,
                    context.RequestAborted);
            });

        bff.MapDelete(
            "/bank-connections/{connectionId:guid}",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid connectionId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (connectionId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardDeleteAsync(
                    context,
                    $"/api/bank-connections/{connectionId}",
                    context.RequestAborted);
            });

        bff.MapPost(
            "/bill-streams/{billStreamId:guid}/statement-uploads",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                BillWatchBffProxyService proxyService,
                Guid billStreamId) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                if (billStreamId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                if (context.Request.ContentLength is > StatementMultipartBodyLimit)
                {
                    return Results.StatusCode(
                        StatusCodes.Status413PayloadTooLarge);
                }

                IFormCollection form;

                try
                {
                    form = await context.Request.ReadFormAsync(
                        context.RequestAborted);
                }
                catch (InvalidDataException)
                {
                    return Results.StatusCode(
                        StatusCodes.Status413PayloadTooLarge);
                }

                var file = form.Files.GetFile("file");

                if (file is null)
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "Select a bill statement to upload."
                        });
                }

                if (file.Length <= 0)
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "The selected statement is empty."
                        });
                }

                if (file.Length > StatementFileSizeLimit)
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "Bill statements must be 15 MB or smaller."
                        });
                }

                return await proxyService.ForwardMultipartFileAsync(
                    context,
                    $"/api/bill-streams/{billStreamId}/statement-uploads",
                    file,
                    context.RequestAborted);
            });

        bff.MapGet(
            "/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                Guid billStreamId,
                Guid uploadId) =>
            {
                if (billStreamId == Guid.Empty ||
                    uploadId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardGetAsync(
                    context,
                    $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}",
                    context.RequestAborted);
            });

        bff.MapGet(
            "/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file",
            async (
                HttpContext context,
                BillWatchBffProxyService proxyService,
                Guid billStreamId,
                Guid uploadId) =>
            {
                if (billStreamId == Guid.Empty ||
                    uploadId == Guid.Empty)
                {
                    return Results.NotFound();
                }

                return await proxyService.ForwardApiDownloadAsync(
                    context,
                    $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}/file",
                    context.RequestAborted);
            });

        return endpoints;
    }
}

public sealed record SubscriptionCheckoutRequest(
    string BillingInterval);

public sealed record SubscriptionRedemptionRequest(
    string AccessKey);

public sealed record DeleteAccountBffRequest(
    string Confirmation,
    string CurrentPassword,
    string? TwoFactorCode);
