using BillWatch.API.Authorization;
using BillWatch.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = BillWatchPolicies.AdminOrOwner)]
public sealed class AdminSecurityController(
    BillWatchDbContext dbContext,
    TimeProvider timeProvider)
    : ControllerBase
{
    [HttpGet("access-keys")]
    public async Task<ActionResult<AdminPage<AdminAccessKeySummary>>> ListAccessKeys(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var query = dbContext.SubscriptionAccessKeys
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(skip)
            .Take(take)
            .Select(item => new
            {
                item.Id,
                item.DisplayPrefix,
                item.Label,
                item.Purpose,
                item.Tier,
                item.DurationDays,
                item.GrantsLifetimeAccess,
                item.MaxRedemptions,
                item.RedemptionCount,
                item.ExpiresAtUtc,
                item.IsRevoked,
                item.RevokedAtUtc,
                item.CreatedByUserId,
                item.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var nowUtc = timeProvider.GetUtcNow();
        var items = rows
            .Select(item => new AdminAccessKeySummary(
                item.Id,
                item.DisplayPrefix,
                item.Label,
                item.Purpose.ToString(),
                item.Tier.ToString(),
                item.DurationDays,
                item.GrantsLifetimeAccess,
                item.MaxRedemptions,
                item.RedemptionCount,
                item.ExpiresAtUtc,
                GetAccessKeyStatus(
                    item.IsRevoked,
                    item.ExpiresAtUtc,
                    item.RedemptionCount,
                    item.MaxRedemptions,
                    nowUtc),
                item.RevokedAtUtc,
                item.CreatedByUserId,
                item.CreatedAtUtc))
            .ToArray();

        return new AdminPage<AdminAccessKeySummary>(
            skip,
            take,
            totalCount,
            items);
    }

    [HttpGet("audit-log")]
    public async Task<ActionResult<AdminPage<AdminAuditLogSummary>>> ListAuditLog(
        [FromQuery] Guid? targetUserId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var query = dbContext.AdminAuditLogs
            .AsNoTracking()
            .AsQueryable();

        if (targetUserId.HasValue)
        {
            query = query.Where(
                item => item.TargetUserId == targetUserId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(skip)
            .Take(take)
            .Select(item => new AdminAuditLogSummary(
                item.Id,
                item.ActorUserId,
                item.TargetUserId,
                item.Action,
                item.SubjectType,
                item.SubjectId,
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new AdminPage<AdminAuditLogSummary>(
            skip,
            take,
            totalCount,
            items);
    }

    private static string GetAccessKeyStatus(
        bool isRevoked,
        DateTimeOffset? expiresAtUtc,
        int redemptionCount,
        int maxRedemptions,
        DateTimeOffset nowUtc)
    {
        if (isRevoked)
        {
            return "Revoked";
        }

        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= nowUtc)
        {
            return "Expired";
        }

        if (redemptionCount >= maxRedemptions)
        {
            return "Exhausted";
        }

        return "Active";
    }
}

public sealed record AdminPage<T>(
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<T> Items);

public sealed record AdminAccessKeySummary(
    Guid Id,
    string DisplayPrefix,
    string? Label,
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    int RedemptionCount,
    DateTimeOffset? ExpiresAtUtc,
    string Status,
    DateTimeOffset? RevokedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record AdminAuditLogSummary(
    Guid Id,
    Guid ActorUserId,
    Guid? TargetUserId,
    string Action,
    string SubjectType,
    Guid? SubjectId,
    DateTimeOffset CreatedAtUtc);
