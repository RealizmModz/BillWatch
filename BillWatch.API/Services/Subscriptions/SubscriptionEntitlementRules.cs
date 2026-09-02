using BillWatch.API.Data.Entities;

namespace BillWatch.API.Services.Subscriptions;

public static class SubscriptionEntitlementRules
{
    public static bool IsActive(
        SubscriptionEntitlementEntity entitlement,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(
            entitlement);

        if (entitlement.IsRevoked ||
            entitlement.StartsAtUtc > nowUtc)
        {
            return false;
        }

        return entitlement.EndsAtUtc is null ||
            entitlement.EndsAtUtc > nowUtc;
    }

    public static SubscriptionEntitlementEntity?
        SelectEffectiveEntitlement(
            IEnumerable<SubscriptionEntitlementEntity> entitlements,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(
            entitlements);

        return entitlements
            .Where(
                entitlement =>
                    IsActive(
                        entitlement,
                        nowUtc))
            .OrderByDescending(
                entitlement =>
                    GetTierRank(
                        entitlement.Tier))
            .ThenByDescending(
                entitlement =>
                    entitlement.EndsAtUtc is null)
            .ThenByDescending(
                entitlement =>
                    entitlement.EndsAtUtc)
            .ThenByDescending(
                entitlement =>
                    entitlement.StartsAtUtc)
            .FirstOrDefault();
    }

    private static int GetTierRank(
        BillWatchSubscriptionTier tier)
    {
        return tier switch
        {
            BillWatchSubscriptionTier.Standard => 200,
            BillWatchSubscriptionTier.Beta => 100,
            _ => 0
        };
    }
}
