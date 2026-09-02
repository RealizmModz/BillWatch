namespace BillWatch.API.Data.Entities;

public enum BillWatchSubscriptionTier
{
    Beta,
    Standard
}

public enum SubscriptionEntitlementSource
{
    Paid,
    Complimentary,
    BetaProgram,
    AccessKey,
    Internal
}

public sealed class SubscriptionEntitlementEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    public BillWatchSubscriptionTier Tier { get; set; } =
        BillWatchSubscriptionTier.Standard;

    public SubscriptionEntitlementSource Source { get; set; } =
        SubscriptionEntitlementSource.Complimentary;

    public DateTimeOffset StartsAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset? EndsAtUtc { get; set; }

    public bool IsRevoked { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? GrantedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;

    public ApplicationUser? GrantedByUser { get; set; }
}
