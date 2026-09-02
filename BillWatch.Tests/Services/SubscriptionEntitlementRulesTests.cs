using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;

namespace BillWatch.Tests.Services;

public sealed class SubscriptionEntitlementRulesTests
{
    private static readonly DateTimeOffset NowUtc =
        new(
            2026,
            9,
            2,
            3,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public void IsActive_AcceptsCurrentLifetimeEntitlement()
    {
        var entitlement =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-1),
                endsAtUtc:
                    null);

        Assert.True(
            SubscriptionEntitlementRules.IsActive(
                entitlement,
                NowUtc));
    }

    [Fact]
    public void IsActive_RejectsRevokedEntitlement()
    {
        var entitlement =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-1),
                endsAtUtc:
                    NowUtc.AddDays(30));

        entitlement.IsRevoked =
            true;

        entitlement.RevokedAtUtc =
            NowUtc.AddMinutes(-1);

        Assert.False(
            SubscriptionEntitlementRules.IsActive(
                entitlement,
                NowUtc));
    }

    [Fact]
    public void IsActive_RejectsFutureEntitlement()
    {
        var entitlement =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddMinutes(1),
                endsAtUtc:
                    NowUtc.AddDays(30));

        Assert.False(
            SubscriptionEntitlementRules.IsActive(
                entitlement,
                NowUtc));
    }

    [Fact]
    public void IsActive_ExpiresAtExactEndInstant()
    {
        var entitlement =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-30),
                endsAtUtc:
                    NowUtc);

        Assert.False(
            SubscriptionEntitlementRules.IsActive(
                entitlement,
                NowUtc));
    }

    [Fact]
    public void SelectEffectiveEntitlement_PrefersStandardOverBeta()
    {
        var beta =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-10),
                endsAtUtc:
                    null,
                tier:
                    BillWatchSubscriptionTier.Beta);

        var standard =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-1),
                endsAtUtc:
                    NowUtc.AddDays(30),
                tier:
                    BillWatchSubscriptionTier.Standard);

        var selected =
            SubscriptionEntitlementRules
                .SelectEffectiveEntitlement(
                    [beta, standard],
                    NowUtc);

        Assert.Same(
            standard,
            selected);
    }

    [Fact]
    public void SelectEffectiveEntitlement_IgnoresInactiveEntries()
    {
        var expired =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-30),
                endsAtUtc:
                    NowUtc.AddDays(-1));

        var revoked =
            CreateEntitlement(
                startsAtUtc:
                    NowUtc.AddDays(-1),
                endsAtUtc:
                    null);

        revoked.IsRevoked =
            true;

        Assert.Null(
            SubscriptionEntitlementRules
                .SelectEffectiveEntitlement(
                    [expired, revoked],
                    NowUtc));
    }

    private static SubscriptionEntitlementEntity
        CreateEntitlement(
            DateTimeOffset startsAtUtc,
            DateTimeOffset? endsAtUtc,
            BillWatchSubscriptionTier tier =
                BillWatchSubscriptionTier.Standard)
    {
        return new SubscriptionEntitlementEntity
        {
            UserId =
                Guid.NewGuid(),
            Tier =
                tier,
            Source =
                SubscriptionEntitlementSource.Complimentary,
            StartsAtUtc =
                startsAtUtc,
            EndsAtUtc =
                endsAtUtc,
            CreatedAtUtc =
                startsAtUtc,
            UpdatedAtUtc =
                startsAtUtc
        };
    }
}
