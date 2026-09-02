namespace BillWatch.API.Data.Entities;

public enum SubscriptionAccessKeyPurpose
{
    Complimentary,
    Beta
}

public sealed class SubscriptionAccessKeyEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string KeyHash { get; set; } =
        string.Empty;

    public string DisplayPrefix { get; set; } =
        string.Empty;

    public SubscriptionAccessKeyPurpose Purpose { get; set; } =
        SubscriptionAccessKeyPurpose.Complimentary;

    public BillWatchSubscriptionTier Tier { get; set; } =
        BillWatchSubscriptionTier.Standard;

    public int? DurationDays { get; set; }

    public bool GrantsLifetimeAccess { get; set; }

    public int MaxRedemptions { get; set; } =
        1;

    public int RedemptionCount { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsRevoked { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser CreatedByUser { get; set; } =
        null!;
}
