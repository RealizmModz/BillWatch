using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using BillWatch.API.Data;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/subscription")]
[Authorize]
public sealed class SubscriptionController(
    BillWatchDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SubscriptionAccessKeyRedemptionService redemptionService,
    TimeProvider timeProvider)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SubscriptionStatusResponse>> GetStatus(
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userManager.GetUserId(User), out var userId))
        {
            return Unauthorized();
        }

        var entitlements = await dbContext.SubscriptionEntitlements
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .ToListAsync(cancellationToken);

        var effective = SubscriptionEntitlementRules.SelectEffectiveEntitlement(
            entitlements,
            timeProvider.GetUtcNow());

        return Ok(
            effective is null
                ? new SubscriptionStatusResponse(false, null, null, null)
                : new SubscriptionStatusResponse(
                    true,
                    effective.Tier.ToString(),
                    effective.StartsAtUtc,
                    effective.EndsAtUtc));
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
}

public sealed record SubscriptionRedemptionRequest(string AccessKey);

public sealed record SubscriptionRedemptionResponse(
    Guid EntitlementId,
    string Tier,
    DateTimeOffset? EndsAtUtc);

public sealed record SubscriptionStatusResponse(
    bool IsActive,
    string? Tier,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc);
