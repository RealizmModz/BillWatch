using System.Security.Claims;
using BillWatch.API.Authorization;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Security;

public sealed class ActiveSubscriptionAuthorizationHandlerTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 3, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_SucceedsForActiveEntitlement()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();

        dbContext.SubscriptionEntitlements.Add(
            new SubscriptionEntitlementEntity
            {
                UserId = userId,
                StartsAtUtc = NowUtc.AddDays(-1),
                EndsAtUtc = NowUtc.AddDays(1)
            });
        await dbContext.SaveChangesAsync();

        var context = CreateAuthorizationContext(userId);
        var handler = new ActiveSubscriptionAuthorizationHandler(
            dbContext,
            new FixedTimeProvider(NowUtc));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_DoesNotSucceedForExpiredEntitlement()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();

        dbContext.SubscriptionEntitlements.Add(
            new SubscriptionEntitlementEntity
            {
                UserId = userId,
                StartsAtUtc = NowUtc.AddDays(-2),
                EndsAtUtc = NowUtc
            });
        await dbContext.SaveChangesAsync();

        var context = CreateAuthorizationContext(userId);
        var handler = new ActiveSubscriptionAuthorizationHandler(
            dbContext,
            new FixedTimeProvider(NowUtc));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_SucceedsForExplicitlyExemptEndpoint()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var httpContext = new DefaultHttpContext();

        httpContext.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(
                    new SubscriptionAccessExemptAttribute()),
                "subscription-exempt-test"));

        var context = CreateAuthorizationContext(
            userId,
            httpContext);
        var handler = new ActiveSubscriptionAuthorizationHandler(
            dbContext,
            new FixedTimeProvider(NowUtc));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_DoesNotTreatUnmarkedEndpointAsExempt()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var httpContext = new DefaultHttpContext();

        httpContext.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(),
                "subscription-protected-test"));

        var context = CreateAuthorizationContext(
            userId,
            httpContext);
        var handler = new ActiveSubscriptionAuthorizationHandler(
            dbContext,
            new FixedTimeProvider(NowUtc));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static BillWatchDbContext CreateDbContext()
    {
        return new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
    }

    private static AuthorizationHandlerContext CreateAuthorizationContext(
        Guid userId,
        object? resource = null)
    {
        var requirement = new ActiveSubscriptionRequirement();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "test"));

        return new AuthorizationHandlerContext(
            [requirement],
            principal,
            resource);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
