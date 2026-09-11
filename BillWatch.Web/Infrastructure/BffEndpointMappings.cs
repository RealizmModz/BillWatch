using System.Text.Json;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BillWatch.Web.Infrastructure;

public static class BffEndpointMappings
{
    private const long StatementFileSizeLimit = 15L * 1024 * 1024;
    private const long StatementMultipartBodyLimit = 16L * 1024 * 1024;

    public static IEndpointRouteBuilder MapBillWatchBffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var bff = endpoints.MapGroup("/bff").RequireAuthorization();

        bff.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return string.IsNullOrWhiteSpace(tokens.RequestToken)
                ? Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                : Results.Ok(new { requestToken = tokens.RequestToken });
        });

        bff.MapGet("/subscription", async (HttpContext context, BillWatchBffProxyService proxy) =>
            await proxy.ForwardGetAsync(context, "/api/subscription", context.RequestAborted));
        bff.MapGet("/subscription/plans", async (HttpContext context, BillWatchBffProxyService proxy) =>
            await proxy.ForwardGetAsync(context, "/api/subscription/plans", context.RequestAborted));
        bff.MapPost("/subscription/checkout", async (HttpContext context, IAntiforgery antiforgery, AdminBffWriteProxyService proxy, SubscriptionCheckoutRequest request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return await proxy.ForwardJsonAsync(context, HttpMethod.Post, "/api/subscription/checkout", request, context.RequestAborted);
        });
        bff.MapPost("/subscription/billing-portal", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return await proxy.ForwardPostAsync(context, "/api/subscription/billing-portal", false, context.RequestAborted);
        });
        bff.MapPost("/subscription/sync", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return await proxy.ForwardPostAsync(context, "/api/subscription/sync", false, context.RequestAborted);
        });
        bff.MapPost("/subscription/access-keys/redeem", async (HttpContext context, IAntiforgery antiforgery, AdminBffWriteProxyService proxy, SubscriptionRedemptionRequest request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return await proxy.ForwardJsonAsync(context, HttpMethod.Post, "/api/subscription/access-keys/redeem", request, context.RequestAborted);
        });

        bff.MapGet("/bill-streams", async (HttpContext context, BillWatchBffProxyService proxy) =>
            await proxy.ForwardGetAsync(context, "/api/bill-streams", context.RequestAborted));
        bff.MapGet("/bill-streams/{billStreamId:guid}", async (HttpContext context, BillWatchBffProxyService proxy, Guid billStreamId) =>
            billStreamId == Guid.Empty
                ? Results.NotFound()
                : await proxy.ForwardGetAsync(context, $"/api/bill-streams/{billStreamId}", context.RequestAborted));
        bff.MapGet("/bank-accounts", async (HttpContext context, BillWatchBffProxyService proxy) =>
            await proxy.ForwardGetAsync(context, "/api/bank-accounts", context.RequestAborted));
        bff.MapGet("/bank-connections", async (HttpContext context, BillWatchBffProxyService proxy) =>
            await proxy.ForwardGetAsync(context, "/api/bank-connections", context.RequestAborted));
        bff.MapGet("/bank-transactions", async (HttpContext context, BillWatchBffProxyService proxy, int? take) =>
            await proxy.ForwardGetAsync(context, $"/api/bank-transactions?take={Math.Clamp(take ?? 100, 1, 500)}", context.RequestAborted));
        bff.MapGet("/alerts", async (HttpContext context, BillWatchBffProxyService proxy, bool? includeDismissed, bool? unreadOnly, int? take) =>
        {
            var requestUri = "/api/alerts" +
                $"?includeDismissed={(includeDismissed ?? false).ToString().ToLowerInvariant()}" +
                $"&unreadOnly={(unreadOnly ?? false).ToString().ToLowerInvariant()}" +
                $"&take={Math.Clamp(take ?? 50, 1, 100)}";
            return await proxy.ForwardGetAsync(context, requestUri, context.RequestAborted);
        });
        bff.MapPost("/alerts/{alertId:guid}/read", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy, Guid alertId) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return alertId == Guid.Empty ? Results.NotFound() : await proxy.ForwardPostAsync(context, $"/api/alerts/{alertId}/read", false, context.RequestAborted);
        });
        bff.MapPost("/alerts/{alertId:guid}/dismiss", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy, Guid alertId) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return alertId == Guid.Empty ? Results.NotFound() : await proxy.ForwardPostAsync(context, $"/api/alerts/{alertId}/dismiss", false, context.RequestAborted);
        });

        bff.MapGet("/account/export", async (HttpContext context, BillWatchBffProxyService proxy) =>
            await proxy.ForwardDownloadAsync(context, "/api/account/export", "billwatch-data-export.json", "application/json; charset=utf-8", context.RequestAborted));
        bff.MapDelete("/account", async (HttpContext context, IAntiforgery antiforgery, AdminBffWriteProxyService proxy) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            DeleteAccountBffRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<DeleteAccountBffRequest>(cancellationToken: context.RequestAborted);
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (BadHttpRequestException) { return Results.BadRequest(); }
            catch (NotSupportedException) { return Results.BadRequest(); }
            if (request is null) return Results.BadRequest();
            if (!string.Equals(request.Confirmation, "DELETE", StringComparison.Ordinal))
                return Results.BadRequest(new { message = "Type DELETE to confirm permanent account deletion." });
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                return Results.BadRequest(new { message = "Enter your current password to confirm permanent account deletion." });
            return await proxy.ForwardJsonAsync(context, HttpMethod.Delete, "/api/account", request, context.RequestAborted);
        });

        bff.MapPost("/plaid/link-session", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return await proxy.ForwardPostAsync(context, "/api/plaid/link-token", true, context.RequestAborted);
        });
        bff.MapPost("/plaid/connections/{connectionId:guid}/update-link-session", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy, Guid connectionId) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return connectionId == Guid.Empty ? Results.NotFound() : await proxy.ForwardPostAsync(context, $"/api/plaid/connections/{connectionId}/update-link-token", false, context.RequestAborted);
        });
        bff.MapPost("/plaid/link-session/{sessionId:guid}/complete", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy, Guid sessionId) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return sessionId == Guid.Empty ? Results.NotFound() : await proxy.ForwardPostAsync(context, $"/api/plaid/link-session/{sessionId}/complete", false, context.RequestAborted);
        });
        bff.MapDelete("/bank-connections/{connectionId:guid}", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy, Guid connectionId) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return connectionId == Guid.Empty ? Results.NotFound() : await proxy.ForwardDeleteAsync(context, $"/api/bank-connections/{connectionId}", context.RequestAborted);
        });

        bff.MapPost("/bill-streams/{billStreamId:guid}/statement-uploads", async (HttpContext context, IAntiforgery antiforgery, BillWatchBffProxyService proxy, Guid billStreamId) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            if (billStreamId == Guid.Empty) return Results.NotFound();
            if (context.Request.ContentLength is > StatementMultipartBodyLimit)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(context.RequestAborted);
            }
            catch (InvalidDataException)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            catch (BadHttpRequestException)
            {
                return Results.BadRequest(new { message = "The statement upload request is malformed." });
            }

            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "Select a bill statement to upload." });
            if (file.Length <= 0) return Results.BadRequest(new { message = "The selected statement is empty." });
            if (file.Length > StatementFileSizeLimit) return Results.BadRequest(new { message = "Bill statements must be 15 MB or smaller." });
            return await proxy.ForwardMultipartFileAsync(context, $"/api/bill-streams/{billStreamId}/statement-uploads", file, context.RequestAborted);
        });

        bff.MapGet("/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}", async (HttpContext context, BillWatchBffProxyService proxy, Guid billStreamId, Guid uploadId) =>
            billStreamId == Guid.Empty || uploadId == Guid.Empty
                ? Results.NotFound()
                : await proxy.ForwardGetAsync(context, $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}", context.RequestAborted));
        bff.MapGet("/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file", async (HttpContext context, BillWatchBffProxyService proxy, Guid billStreamId, Guid uploadId) =>
            billStreamId == Guid.Empty || uploadId == Guid.Empty
                ? Results.NotFound()
                : await proxy.ForwardApiDownloadAsync(context, $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}/file", context.RequestAborted));

        return endpoints;
    }
}

public sealed record SubscriptionCheckoutRequest(string BillingInterval);
public sealed record SubscriptionRedemptionRequest(string AccessKey);
public sealed record DeleteAccountBffRequest(string Confirmation, string CurrentPassword, string? TwoFactorCode);
