using BillWatch.API.Authorization;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/admin/subscription")]
[Authorize(Policy = BillWatchPolicies.AdminOrOwner)]
public sealed class AdminSubscriptionController(
    UserManager<ApplicationUser> userManager,
    AdminSubscriptionAccessKeyService accessKeyService)
    : ControllerBase
{
    [HttpPost("access-keys")]
    public async Task<ActionResult<CreatedAccessKeyResponse>> CreateAccessKey(
        CreateAccessKeyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<SubscriptionAccessKeyPurpose>(
                request.Purpose,
                ignoreCase: true,
                out var purpose) ||
            !Enum.IsDefined(purpose) ||
            !Enum.TryParse<BillWatchSubscriptionTier>(
                request.Tier,
                ignoreCase: true,
                out var tier) ||
            !Enum.IsDefined(tier))
        {
            return ValidationProblem(
                "Purpose or tier is invalid.");
        }

        try
        {
            var created = await accessKeyService.CreateAsync(
                actorUserId,
                purpose,
                tier,
                request.DurationDays,
                request.GrantsLifetimeAccess,
                request.MaxRedemptions,
                request.ExpiresAtUtc,
                cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new CreatedAccessKeyResponse(
                    created.Id,
                    created.PlaintextKey,
                    created.DisplayPrefix,
                    created.Purpose.ToString(),
                    created.Tier.ToString(),
                    created.DurationDays,
                    created.GrantsLifetimeAccess,
                    created.MaxRedemptions,
                    created.ExpiresAtUtc,
                    created.CreatedAtUtc));
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    [HttpPost("access-keys/{accessKeyId:guid}/revoke")]
    public async Task<IActionResult> RevokeAccessKey(
        Guid accessKeyId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        return await accessKeyService.RevokeAsync(
            actorUserId,
            accessKeyId,
            cancellationToken)
                ? NoContent()
                : NotFound();
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(
            userManager.GetUserId(User),
            out actorUserId);
    }
}

public sealed record CreateAccessKeyRequest(
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    DateTimeOffset? ExpiresAtUtc);

public sealed record CreatedAccessKeyResponse(
    Guid Id,
    string PlaintextKey,
    string DisplayPrefix,
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);
