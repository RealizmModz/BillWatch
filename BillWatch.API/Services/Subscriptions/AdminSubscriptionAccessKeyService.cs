using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Subscriptions;

public sealed class AdminSubscriptionAccessKeyService(
    BillWatchDbContext dbContext,
    SubscriptionAccessKeyGenerator keyGenerator,
    TimeProvider timeProvider)
{
    public async Task<CreatedSubscriptionAccessKey> CreateAsync(
        Guid actorUserId,
        SubscriptionAccessKeyPurpose purpose,
        BillWatchSubscriptionTier tier,
        int? durationDays,
        bool grantsLifetimeAccess,
        int maxRedemptions,
        DateTimeOffset? expiresAtUtc,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = timeProvider.GetUtcNow();

        if (!Enum.IsDefined(purpose) ||
            !Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(purpose),
                "Purpose and tier must be defined values.");
        }

        var normalizedLabel = NormalizeLabel(
            label,
            grantsLifetimeAccess);

        ValidateGrant(
            durationDays,
            grantsLifetimeAccess,
            maxRedemptions,
            expiresAtUtc,
            nowUtc);

        var generated = keyGenerator.Generate();
        var accessKey = new SubscriptionAccessKeyEntity
        {
            KeyHash = generated.Hash,
            DisplayPrefix = generated.DisplayPrefix,
            Label = normalizedLabel,
            Purpose = purpose,
            Tier = tier,
            DurationDays = durationDays,
            GrantsLifetimeAccess = grantsLifetimeAccess,
            MaxRedemptions = maxRedemptions,
            ExpiresAtUtc = expiresAtUtc,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = nowUtc
        };

        dbContext.SubscriptionAccessKeys.Add(accessKey);
        dbContext.AdminAuditLogs.Add(
            new AdminAuditLogEntity
            {
                ActorUserId = actorUserId,
                Action = "SubscriptionAccessKeyCreated",
                SubjectType = nameof(SubscriptionAccessKeyEntity),
                SubjectId = accessKey.Id,
                CreatedAtUtc = nowUtc
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreatedSubscriptionAccessKey(
            accessKey.Id,
            generated.PlaintextKey,
            accessKey.DisplayPrefix,
            accessKey.Label,
            accessKey.Purpose,
            accessKey.Tier,
            accessKey.DurationDays,
            accessKey.GrantsLifetimeAccess,
            accessKey.MaxRedemptions,
            accessKey.ExpiresAtUtc,
            accessKey.CreatedAtUtc);
    }

    public async Task<bool> RevokeAsync(
        Guid actorUserId,
        Guid accessKeyId,
        CancellationToken cancellationToken = default)
    {
        var accessKey = await dbContext.SubscriptionAccessKeys
            .SingleOrDefaultAsync(
                candidate => candidate.Id == accessKeyId,
                cancellationToken);

        if (accessKey is null)
        {
            return false;
        }

        if (accessKey.IsRevoked)
        {
            return true;
        }

        var nowUtc = timeProvider.GetUtcNow();
        accessKey.IsRevoked = true;
        accessKey.RevokedAtUtc = nowUtc;

        dbContext.AdminAuditLogs.Add(
            new AdminAuditLogEntity
            {
                ActorUserId = actorUserId,
                Action = "SubscriptionAccessKeyRevoked",
                SubjectType = nameof(SubscriptionAccessKeyEntity),
                SubjectId = accessKey.Id,
                CreatedAtUtc = nowUtc
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? NormalizeLabel(
        string? label,
        bool grantsLifetimeAccess)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        if (!grantsLifetimeAccess)
        {
            throw new ArgumentException(
                "Labels are available only for lifetime access keys.",
                nameof(label));
        }

        var normalized = label.Trim();

        if (normalized.Length > 120)
        {
            throw new ArgumentException(
                "Access key labels must be 120 characters or fewer.",
                nameof(label));
        }

        return normalized;
    }

    private static void ValidateGrant(
        int? durationDays,
        bool grantsLifetimeAccess,
        int maxRedemptions,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        if (grantsLifetimeAccess == durationDays.HasValue)
        {
            throw new ArgumentException(
                "Specify either lifetime access or a duration, but not both.");
        }

        if (durationDays is <= 0 or > 3650)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationDays),
                "Duration must be between 1 and 3650 days.");
        }

        if (maxRedemptions is <= 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRedemptions),
                "Maximum redemptions must be between 1 and 10000.");
        }

        if (expiresAtUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "Key expiration must be in the future.");
        }
    }
}

public sealed record CreatedSubscriptionAccessKey(
    Guid Id,
    string PlaintextKey,
    string DisplayPrefix,
    string? Label,
    SubscriptionAccessKeyPurpose Purpose,
    BillWatchSubscriptionTier Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);
