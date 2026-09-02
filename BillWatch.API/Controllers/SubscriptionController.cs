using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/subscription")]
[Authorize]
public sealed class SubscriptionController(
    UserManager<ApplicationUser> userManager,
    SubscriptionAccessKeyRedemptionService redemptionService)
    : ControllerBase
{
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
