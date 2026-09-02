using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class SubscriptionAccessKeyRedemptionServiceTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RedeemAsync_CreatesEntitlementRedemptionAndAudit()
    {
        await using var dbContext = CreateDbContext();
        var generator = new SubscriptionAccessKeyGenerator();
        var generated = generator.Generate();
        var key = CreateKey(generated.Hash, durationDays: 30);
        dbContext.SubscriptionAccessKeys.Add(key);
        await dbContext.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var service = CreateService(dbContext, generator);

        var result = await service.RedeemAsync(userId, generated.PlaintextKey);

        Assert.True(result.Succeeded);
        Assert.Equal(NowUtc.AddDays(30), result.EndsAtUtc);
        Assert.Equal(1, key.RedemptionCount);
        Assert.Single(dbContext.SubscriptionEntitlements);
        Assert.Single(dbContext.SubscriptionAccessKeyRedemptions);
        Assert.Single(dbContext.AdminAuditLogs);
        Assert.Equal(
            SubscriptionEntitlementSource.AccessKey,
            dbContext.SubscriptionEntitlements.Single().Source);
    }

    [Fact]
    public async Task RedeemAsync_RejectsRevokedKeyWithoutWriting()
    {
        await using var dbContext = CreateDbContext();
        var generator = new SubscriptionAccessKeyGenerator();
        var generated = generator.Generate();
        var key = CreateKey(generated.Hash, durationDays: 30);
        key.IsRevoked = true;
        key.RevokedAtUtc = NowUtc.AddMinutes(-1);
        dbContext.SubscriptionAccessKeys.Add(key);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, generator)
            .RedeemAsync(Guid.NewGuid(), generated.PlaintextKey);

        Assert.False(result.Succeeded);
        Assert.Empty(dbContext.SubscriptionEntitlements);
        Assert.Empty(dbContext.SubscriptionAccessKeyRedemptions);
        Assert.Empty(dbContext.AdminAuditLogs);
    }

    [Fact]
    public async Task RedeemAsync_RejectsSecondRedemptionBySameUser()
    {
        await using var dbContext = CreateDbContext();
        var generator = new SubscriptionAccessKeyGenerator();
        var generated = generator.Generate();
        var key = CreateKey(generated.Hash, durationDays: 30);
        key.MaxRedemptions = 2;
        dbContext.SubscriptionAccessKeys.Add(key);
        await dbContext.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var service = CreateService(dbContext, generator);

        Assert.True((await service.RedeemAsync(userId, generated.PlaintextKey)).Succeeded);
        Assert.False((await service.RedeemAsync(userId, generated.PlaintextKey)).Succeeded);
        Assert.Equal(1, key.RedemptionCount);
        Assert.Single(dbContext.SubscriptionAccessKeyRedemptions);
    }

    private static SubscriptionAccessKeyEntity CreateKey(
        string hash,
        int durationDays)
    {
        return new SubscriptionAccessKeyEntity
        {
            KeyHash = hash,
            DisplayPrefix = "BW-TEST",
            DurationDays = durationDays,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = NowUtc
        };
    }

    private static SubscriptionAccessKeyRedemptionService CreateService(
        BillWatchDbContext dbContext,
        SubscriptionAccessKeyGenerator generator)
    {
        return new SubscriptionAccessKeyRedemptionService(
            dbContext,
            generator,
            new FixedTimeProvider(NowUtc));
    }

    private static BillWatchDbContext CreateDbContext()
    {
        return new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
