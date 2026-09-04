using System.Text;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/subscription")]
[Authorize]
public sealed class SubscriptionController(
    BillWatchDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SubscriptionAccessKeyRedemptionService redemptionService,
    TimeProvider timeProvider,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
    : ControllerBase
{
    private const int StripeWebhookMaxPayloadBytes = 256 * 1024;

    [HttpGet]
    public async Task<ActionResult<SubscriptionStatusResponse>> GetStatus(
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        var entitlements = await dbContext.SubscriptionEntitlements
            .AsNoTracking()
            .Where(candidate => candidate.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var effective = SubscriptionEntitlementRules.SelectEffectiveEntitlement(
            entitlements,
            timeProvider.GetUtcNow());

        var billing = CreateBillingService();
        StripeSubscriptionState? providerState = null;

        if (billing.IsConfigured)
        {
            try
            {
                providerState = await billing.GetCurrentSubscriptionAsync(
                    user.Id,
                    user.Email,
                    cancellationToken);
            }
            catch (StripeBillingException)
            {
                // Local entitlement state remains authoritative for access if
                // Stripe is temporarily unavailable. Provider details are
                // presentation-only on this response.
            }
        }

        return Ok(
            new SubscriptionStatusResponse(
                effective is not null,
                effective?.Tier.ToString(),
                effective?.StartsAtUtc,
                effective?.EndsAtUtc,
                effective?.Source.ToString(),
                billing.IsConfigured,
                effective?.Source == SubscriptionEntitlementSource.Paid,
                providerState?.BillingInterval,
                providerState?.CancelAtPeriodEnd ?? false,
                providerState?.Status));
    }

    [HttpGet("plans")]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanResponse>>> GetPlans(
        CancellationToken cancellationToken)
    {
        var billing = CreateBillingService();

        if (!billing.IsConfigured)
        {
            return Ok(Array.Empty<SubscriptionPlanResponse>());
        }

        try
        {
            var plans = await billing.GetPlansAsync(cancellationToken);

            return Ok(
                plans.Select(plan =>
                    new SubscriptionPlanResponse(
                        plan.BillingInterval,
                        plan.UnitAmount,
                        plan.Currency)));
        }
        catch (StripeBillingException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "BillWatch could not load paid subscription plans right now.");
        }
    }

    [HttpPost("checkout")]
    [EnableRateLimiting("subscription-redemption")]
    public async Task<ActionResult<SubscriptionRedirectResponse>> CreateCheckout(
        SubscriptionCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return BadRequest(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Verify your BillWatch email before starting a paid subscription."
                });
        }

        var billing = CreateBillingService();

        if (!billing.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Paid subscriptions are not available in this BillWatch environment yet.");
        }

        try
        {
            var url = await billing.CreateCheckoutUrlAsync(
                user.Id,
                user.Email,
                request.BillingInterval,
                cancellationToken);

            return Ok(new SubscriptionRedirectResponse(url));
        }
        catch (StripeBillingException exception)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = exception.Message
                });
        }
    }

    [HttpPost("billing-portal")]
    [EnableRateLimiting("subscription-redemption")]
    public async Task<ActionResult<SubscriptionRedirectResponse>> CreateBillingPortal(
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        var billing = CreateBillingService();

        if (!billing.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Paid subscription management is not available in this BillWatch environment yet.");
        }

        try
        {
            var url = await billing.CreatePortalUrlAsync(
                user.Id,
                user.Email,
                cancellationToken);

            return Ok(new SubscriptionRedirectResponse(url));
        }
        catch (StripeBillingException exception)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = exception.Message
                });
        }
    }

    [HttpPost("sync")]
    [EnableRateLimiting("subscription-redemption")]
    public async Task<ActionResult<SubscriptionSyncResponse>> SyncPaidSubscription(
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        var billing = CreateBillingService();

        if (!billing.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Paid subscriptions are not configured in this BillWatch environment yet.");
        }

        try
        {
            await billing.SyncCurrentSubscriptionAsync(
                user.Id,
                user.Email,
                cancellationToken);

            return Ok(new SubscriptionSyncResponse(true));
        }
        catch (StripeBillingException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "BillWatch could not refresh the paid subscription right now.");
        }
    }

    [HttpPost("webhooks/stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> StripeWebhook(
        CancellationToken cancellationToken)
    {
        var billing = CreateBillingService();

        if (!billing.IsConfigured)
        {
            return NotFound();
        }

        if (Request.ContentLength is > StripeWebhookMaxPayloadBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var payloadRead = await ReadWebhookPayloadAsync(
            Request.Body,
            cancellationToken);

        if (payloadRead.TooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await billing.HandleWebhookAsync(
                payloadRead.Payload,
                signature,
                cancellationToken);

            return Ok();
        }
        catch (StripeWebhookSignatureException)
        {
            return Unauthorized();
        }
        catch (StripeBillingException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("access-keys/redeem")]
    [EnableRateLimiting("subscription-redemption")]
    public async Task<ActionResult<SubscriptionRedemptionResponse>> Redeem(
        SubscriptionRedemptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userManager.GetUserId(User), out var userId))
        {
            return Unauthorized();
        }

        var result = await redemptionService.RedeemAsync(
            userId,
            request.AccessKey,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "The access key could not be redeemed."
                });
        }

        return Ok(
            new SubscriptionRedemptionResponse(
                result.EntitlementId!.Value,
                result.Tier!.Value.ToString(),
                result.EndsAtUtc));
    }

    private StripeBillingService CreateBillingService() =>
        new(
            httpClientFactory.CreateClient(),
            StripeBillingOptions.FromConfiguration(configuration),
            dbContext,
            timeProvider);

    private static async Task<StripeWebhookPayloadReadResult> ReadWebhookPayloadAsync(
        Stream requestBody,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        using var payload = new MemoryStream(
            capacity: StripeWebhookMaxPayloadBytes);

        while (true)
        {
            var remaining =
                StripeWebhookMaxPayloadBytes + 1 - (int)payload.Length;

            if (remaining <= 0)
            {
                return new StripeWebhookPayloadReadResult(
                    TooLarge: true,
                    Payload: string.Empty);
            }

            var read = await requestBody.ReadAsync(
                buffer.AsMemory(
                    0,
                    Math.Min(buffer.Length, remaining)),
                cancellationToken);

            if (read == 0)
            {
                break;
            }

            await payload.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);

            if (payload.Length > StripeWebhookMaxPayloadBytes)
            {
                return new StripeWebhookPayloadReadResult(
                    TooLarge: true,
                    Payload: string.Empty);
            }
        }

        return new StripeWebhookPayloadReadResult(
            TooLarge: false,
            Payload: Encoding.UTF8.GetString(payload.ToArray()));
    }

    private sealed record StripeWebhookPayloadReadResult(
        bool TooLarge,
        string Payload);
}

public sealed record SubscriptionCheckoutRequest(string BillingInterval);

public sealed record SubscriptionRedirectResponse(string Url);

public sealed record SubscriptionPlanResponse(
    string BillingInterval,
    long UnitAmount,
    string Currency);

public sealed record SubscriptionSyncResponse(bool Succeeded);

public sealed record SubscriptionRedemptionRequest(string AccessKey);

public sealed record SubscriptionRedemptionResponse(
    Guid EntitlementId,
    string Tier,
    DateTimeOffset? EndsAtUtc);

public sealed record SubscriptionStatusResponse(
    bool IsActive,
    string? Tier,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    string? Source,
    bool BillingAvailable,
    bool IsPaid,
    string? BillingInterval,
    bool CancelAtPeriodEnd,
    string? ProviderStatus);
