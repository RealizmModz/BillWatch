using System.Data;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillWatch.API.Services.Subscriptions;

public sealed class SubscriptionAccessKeyRedemptionService(
    BillWatchDbContext dbContext,
    SubscriptionAccessKeyGenerator keyGenerator,
    TimeProvider timeProvider)
{
    public async Task<SubscriptionAccessKeyRedemptionResult> RedeemAsync(
        Guid userId,
        string plaintextKey,
        CancellationToken cancellationToken = default)
    {
        string keyHash;

        try
        {
            keyHash = keyGenerator.ComputeHash(plaintextKey);
        }
        catch (ArgumentException)
        {
            return SubscriptionAccessKeyRedemptionResult.Invalid;
        }

        IDbContextTransaction? transaction = null;

        try
        {
            if (dbContext.Database.IsRelational())
            {
                transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var accessKey = dbContext.Database.IsRelational()
                ? await dbContext.SubscriptionAccessKeys
                    .FromSqlInterpolated(
                        $"SELECT * FROM \"SubscriptionAccessKeys\" WHERE \"KeyHash\" = {keyHash} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken)
                : await dbContext.SubscriptionAccessKeys
                    .SingleOrDefaultAsync(
                        candidate => candidate.KeyHash == keyHash,
                        cancellationToken);

            var nowUtc = timeProvider.GetUtcNow();

            if (accessKey is null ||
                accessKey.IsRevoked ||
                accessKey.ExpiresAtUtc <= nowUtc ||
                accessKey.RedemptionCount >= accessKey.MaxRedemptions)
            {
                return SubscriptionAccessKeyRedemptionResult.Invalid;
            }

            if (await dbContext.SubscriptionAccessKeyRedemptions.AnyAsync(
                    redemption => redemption.AccessKeyId == accessKey.Id &&
                        redemption.UserId == userId,
                    cancellationToken))
            {
                return SubscriptionAccessKeyRedemptionResult.AlreadyRedeemed;
            }

            DateTimeOffset? endsAtUtc = accessKey.GrantsLifetimeAccess
                ? null
                : nowUtc.AddDays(accessKey.DurationDays!.Value);

            var entitlement = new SubscriptionEntitlementEntity
            {
                UserId = userId,
                Tier = accessKey.Tier,
                Source = SubscriptionEntitlementSource.AccessKey,
                StartsAtUtc = nowUtc,
                EndsAtUtc = endsAtUtc,
                GrantedByUserId = accessKey.CreatedByUserId,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            accessKey.RedemptionCount++;
            dbContext.SubscriptionEntitlements.Add(entitlement);
            dbContext.SubscriptionAccessKeyRedemptions.Add(
                new SubscriptionAccessKeyRedemptionEntity
                {
                    AccessKeyId = accessKey.Id,
                    UserId = userId,
                    EntitlementId = entitlement.Id,
                    RedeemedAtUtc = nowUtc
                });
            dbContext.AdminAuditLogs.Add(
                new AdminAuditLogEntity
                {
                    ActorUserId = userId,
                    TargetUserId = userId,
                    Action = "SubscriptionAccessKeyRedeemed",
                    SubjectType = nameof(SubscriptionAccessKeyEntity),
                    SubjectId = accessKey.Id,
                    CreatedAtUtc = nowUtc
                });

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new SubscriptionAccessKeyRedemptionResult(
                true,
                entitlement.Id,
                entitlement.Tier,
                entitlement.EndsAtUtc);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}

public sealed record SubscriptionAccessKeyRedemptionResult(
    bool Succeeded,
    Guid? EntitlementId,
    BillWatchSubscriptionTier? Tier,
    DateTimeOffset? EndsAtUtc)
{
    public static SubscriptionAccessKeyRedemptionResult Invalid { get; } =
        new(false, null, null, null);

    public static SubscriptionAccessKeyRedemptionResult AlreadyRedeemed { get; } =
        Invalid;
}
