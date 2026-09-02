using BillWatch.API.Authorization;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Admin;
using BillWatch.API.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = BillWatchPolicies.AdminOrOwner)]
public sealed class AdminUsersController(
    BillWatchDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    AdminUserManagementService managementService,
    TimeProvider timeProvider)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminUserSummary>>> ListUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip(skip)
            .Take(take)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.IsActive,
                user.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        var userIds = users.Select(user => user.Id).ToArray();

        var roles = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where userIds.Contains(userRole.UserId)
            select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        var nowUtc = timeProvider.GetUtcNow();
        var entitlements = await dbContext.SubscriptionEntitlements
            .AsNoTracking()
            .Where(item => userIds.Contains(item.UserId) &&
                !item.IsRevoked &&
                item.StartsAtUtc <= nowUtc &&
                (item.EndsAtUtc == null || item.EndsAtUtc > nowUtc))
            .ToListAsync(cancellationToken);
        var memberships = await dbContext.UserProgramMemberships
            .AsNoTracking()
            .Where(item => userIds.Contains(item.UserId) &&
                item.IsActive &&
                (item.EndsAtUtc == null || item.EndsAtUtc > nowUtc))
            .ToListAsync(cancellationToken);

        return Ok(users.Select(user =>
        {
            var effective = SubscriptionEntitlementRules.SelectEffectiveEntitlement(
                entitlements.Where(item => item.UserId == user.Id),
                nowUtc);
            return new AdminUserSummary(
                user.Id,
                user.Email,
                user.IsActive,
                user.CreatedAtUtc,
                roles.Where(item => item.UserId == user.Id)
                    .Select(item => item.Name!)
                    .OrderByDescending(BillWatchRoleHierarchy.GetRank)
                    .ToArray(),
                memberships.Where(item => item.UserId == user.Id)
                    .Select(item => item.Program.ToString())
                    .OrderBy(name => name)
                    .ToArray(),
                effective?.Tier.ToString(),
                effective?.EndsAtUtc);
        }).ToList());
    }

    [HttpPost("{targetUserId:guid}/roles/{roleName}")]
    public async Task<IActionResult> AssignRole(
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        var result = await managementService.AssignRoleAsync(
            actorUserId,
            targetUserId,
            roleName,
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("{targetUserId:guid}/roles/{roleName}")]
    public async Task<IActionResult> RemoveRole(
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        var result = await managementService.RemoveRoleAsync(
            actorUserId,
            targetUserId,
            roleName,
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{targetUserId:guid}/entitlements")]
    public async Task<IActionResult> GrantEntitlement(
        Guid targetUserId,
        GrantEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<BillWatchSubscriptionTier>(
                request.Tier,
                ignoreCase: true,
                out var tier) ||
            !Enum.IsDefined(tier))
        {
            return ValidationProblem("Tier is invalid.");
        }

        var result = await managementService.GrantEntitlementAsync(
            actorUserId,
            targetUserId,
            tier,
            request.DurationDays,
            request.GrantsLifetimeAccess,
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new { entitlementId = result.ResourceId });
    }

    [HttpPost("{targetUserId:guid}/entitlements/{entitlementId:guid}/revoke")]
    public async Task<IActionResult> RevokeEntitlement(
        Guid targetUserId,
        Guid entitlementId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        var result = await managementService.RevokeEntitlementAsync(
            actorUserId,
            targetUserId,
            entitlementId,
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{targetUserId:guid}/programs/{programName}")]
    public async Task<IActionResult> SetProgramMembership(
        Guid targetUserId,
        string programName,
        SetProgramMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<UserProgramType>(
                programName,
                ignoreCase: true,
                out var program) ||
            !Enum.IsDefined(program))
        {
            return ValidationProblem("Program is invalid.");
        }

        var result = await managementService.SetProgramMembershipAsync(
            actorUserId,
            targetUserId,
            program,
            request.IsActive,
            request.EndsAtUtc,
            cancellationToken);

        return ToActionResult(result);
    }

    private IActionResult ToActionResult(AdminUserMutationResult result)
    {
        return result.Code switch
        {
            "success" => NoContent(),
            "not_found" => NotFound(),
            "invalid" => ValidationProblem("The requested change is invalid."),
            _ => Forbid()
        };
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(
            userManager.GetUserId(User),
            out actorUserId);
    }
}

public sealed record GrantEntitlementRequest(
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess);

public sealed record SetProgramMembershipRequest(
    bool IsActive,
    DateTimeOffset? EndsAtUtc);

public sealed record AdminUserSummary(
    Guid Id,
    string? Email,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Programs,
    string? SubscriptionTier,
    DateTimeOffset? SubscriptionEndsAtUtc);
