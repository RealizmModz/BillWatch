namespace BillWatch.API.Data.Entities;

public sealed class SubscriptionAccessKeyRedemptionEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid AccessKeyId { get; set; }

    public Guid UserId { get; set; }

    public Guid EntitlementId { get; set; }

    public DateTimeOffset RedeemedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public SubscriptionAccessKeyEntity AccessKey { get; set; } =
        null!;

    public ApplicationUser User { get; set; } =
        null!;

    public SubscriptionEntitlementEntity Entitlement { get; set; } =
        null!;
}
