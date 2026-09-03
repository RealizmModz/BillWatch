using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class AdminSubscriptionAccessKeyServiceTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_PersistsOnlyHashAndReturnsPlaintextOnce()
    {
        await using var dbContext = CreateDbContext();
        var generator = new SubscriptionAccessKeyGenerator();
        var service = CreateService(dbContext, generator);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateAsync(
            actorUserId,
            SubscriptionAccessKeyPurpose.Complimentary,
            BillWatchSubscriptionTier.Standard,
            durationDays: 30,
            grantsLifetimeAccess: false,
            maxRedemptions: 2,
            expiresAtUtc: NowUtc.AddDays(7));

        var stored = Assert.Single(dbContext.SubscriptionAccessKeys);
        Assert.Equal(generator.ComputeHash(result.PlaintextKey), stored.KeyHash);
        Assert.DoesNotContain(result.PlaintextKey, stored.KeyHash);
        Assert.Equal(30, stored.DurationDays);
        Assert.Equal(2, stored.MaxRedemptions);
        Assert.Single(dbContext.AdminAuditLogs);
    }

    [Fact]
    public async Task CreateAsync_TrimsAndPersistsLifetimeLabel()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new SubscriptionAccessKeyGenerator());

        var result = await service.CreateAsync(
            Guid.NewGuid(),
            SubscriptionAccessKeyPurpose.Beta,
            BillWatchSubscriptionTier.Beta,
            durationDays: null,
            grantsLifetimeAccess: true,
            maxRedemptions: 1,
            expiresAtUtc: null,
            label: "  Founding beta tester  ");

        var stored = Assert.Single(dbContext.SubscriptionAccessKeys);
        Assert.Equal("Founding beta tester", stored.Label);
        Assert.Equal("Founding beta tester", result.Label);
    }

    [Fact]
    public async Task CreateAsync_RejectsLabelForNonLifetimeKey()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new SubscriptionAccessKeyGenerator());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                Guid.NewGuid(),
                SubscriptionAccessKeyPurpose.Complimentary,
                BillWatchSubscriptionTier.Standard,
                durationDays: 30,
                grantsLifetimeAccess: false,
                maxRedemptions: 1,
                expiresAtUtc: null,
                label: "Not allowed"));

        Assert.Empty(dbContext.SubscriptionAccessKeys);
    }

    [Fact]
    public async Task CreateAsync_RejectsLifetimeLabelLongerThan120Characters()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new SubscriptionAccessKeyGenerator());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                Guid.NewGuid(),
                SubscriptionAccessKeyPurpose.Beta,
                BillWatchSubscriptionTier.Beta,
                durationDays: null,
                grantsLifetimeAccess: true,
                maxRedemptions: 1,
                expiresAtUtc: null,
                label: new string('x', 121)));

        Assert.Empty(dbContext.SubscriptionAccessKeys);
    }

    [Fact]
    public async Task CreateAsync_RejectsAmbiguousLifetimeGrant()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new SubscriptionAccessKeyGenerator());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                Guid.NewGuid(),
                SubscriptionAccessKeyPurpose.Complimentary,
                BillWatchSubscriptionTier.Standard,
                durationDays: 30,
                grantsLifetimeAccess: true,
                maxRedemptions: 1,
                expiresAtUtc: null));

        Assert.Empty(dbContext.SubscriptionAccessKeys);
    }

    [Fact]
    public async Task RevokeAsync_IsIdempotentAndAuditsOnce()
    {
        await using var dbContext = CreateDbContext();
        var generator = new SubscriptionAccessKeyGenerator();
        var service = CreateService(dbContext, generator);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateAsync(
            actorUserId,
            SubscriptionAccessKeyPurpose.Beta,
            BillWatchSubscriptionTier.Beta,
            durationDays: null,
            grantsLifetimeAccess: true,
            maxRedemptions: 10,
            expiresAtUtc: null);

        Assert.True(await service.RevokeAsync(actorUserId, created.Id));
        Assert.True(await service.RevokeAsync(actorUserId, created.Id));

        var stored = await dbContext.SubscriptionAccessKeys.SingleAsync();
        Assert.True(stored.IsRevoked);
        Assert.Equal(NowUtc, stored.RevokedAtUtc);
        Assert.Equal(2, await dbContext.AdminAuditLogs.CountAsync());
    }

    private static AdminSubscriptionAccessKeyService CreateService(
        BillWatchDbContext dbContext,
        SubscriptionAccessKeyGenerator generator)
    {
        return new AdminSubscriptionAccessKeyService(
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
