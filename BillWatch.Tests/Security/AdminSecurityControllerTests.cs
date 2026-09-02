using System.Reflection;
using BillWatch.API.Authorization;
using BillWatch.API.Controllers;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Security;

public sealed class AdminSecurityControllerTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Controller_RequiresAdminOrOwnerPolicy()
    {
        var attribute = Assert.Single(
            typeof(AdminSecurityController)
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        Assert.Equal(
            BillWatchPolicies.AdminOrOwner,
            attribute.Policy);
    }

    [Fact]
    public async Task ListAccessKeys_ReturnsOnlySafeAdministrativeMetadata()
    {
        await using var dbContext = CreateDbContext();
        var createdByUserId = Guid.NewGuid();
        var secretHash = "THIS-HASH-MUST-NEVER-BE-RETURNED";

        dbContext.SubscriptionAccessKeys.Add(
            new SubscriptionAccessKeyEntity
            {
                Id = Guid.NewGuid(),
                KeyHash = secretHash,
                DisplayPrefix = "BW-ABCD-EFGH",
                Purpose = SubscriptionAccessKeyPurpose.Beta,
                Tier = BillWatchSubscriptionTier.Beta,
                DurationDays = 30,
                GrantsLifetimeAccess = false,
                MaxRedemptions = 5,
                RedemptionCount = 1,
                ExpiresAtUtc = NowUtc.AddDays(2),
                CreatedByUserId = createdByUserId,
                CreatedAtUtc = NowUtc.AddMinutes(-5)
            });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);
        var result = await controller.ListAccessKeys();
        var page = Assert.IsType<AdminPage<AdminAccessKeySummary>>(result.Value);
        var item = Assert.Single(page.Items);

        Assert.Equal("BW-ABCD-EFGH", item.DisplayPrefix);
        Assert.Equal("Active", item.Status);
        Assert.Equal(createdByUserId, item.CreatedByUserId);
        Assert.Null(typeof(AdminAccessKeySummary).GetProperty("KeyHash"));
        Assert.Null(typeof(AdminAccessKeySummary).GetProperty("PlaintextKey"));
        Assert.DoesNotContain(
            secretHash,
            System.Text.Json.JsonSerializer.Serialize(page));
    }

    [Fact]
    public async Task ListAccessKeys_ReportsDeterministicStatuses()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();

        dbContext.SubscriptionAccessKeys.AddRange(
            CreateKey(actorUserId, NowUtc.AddMinutes(-1)),
            CreateKey(
                actorUserId,
                NowUtc.AddMinutes(-2),
                isRevoked: true),
            CreateKey(
                actorUserId,
                NowUtc.AddMinutes(-3),
                expiresAtUtc: NowUtc),
            CreateKey(
                actorUserId,
                NowUtc.AddMinutes(-4),
                redemptionCount: 2,
                maxRedemptions: 2));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);
        var result = await controller.ListAccessKeys();
        var page = Assert.IsType<AdminPage<AdminAccessKeySummary>>(result.Value);

        Assert.Equal(
            ["Active", "Revoked", "Expired", "Exhausted"],
            page.Items.Select(item => item.Status).ToArray());
    }

    [Fact]
    public async Task ListAuditLog_FiltersByTargetAndBoundsPageSize()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var otherTargetUserId = Guid.NewGuid();

        dbContext.AdminAuditLogs.AddRange(
            new AdminAuditLogEntity
            {
                ActorUserId = actorUserId,
                TargetUserId = targetUserId,
                Action = "StaffRoleAssigned",
                SubjectType = "User",
                SubjectId = targetUserId,
                CreatedAtUtc = NowUtc
            },
            new AdminAuditLogEntity
            {
                ActorUserId = actorUserId,
                TargetUserId = otherTargetUserId,
                Action = "SubscriptionEntitlementGranted",
                SubjectType = "SubscriptionEntitlement",
                SubjectId = Guid.NewGuid(),
                CreatedAtUtc = NowUtc.AddMinutes(-1)
            });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);
        var result = await controller.ListAuditLog(
            targetUserId,
            skip: -5,
            take: 1000);
        var page = Assert.IsType<AdminPage<AdminAuditLogSummary>>(result.Value);
        var item = Assert.Single(page.Items);

        Assert.Equal(0, page.Skip);
        Assert.Equal(100, page.Take);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(targetUserId, item.TargetUserId);
        Assert.Equal("StaffRoleAssigned", item.Action);
    }

    private static AdminSecurityController CreateController(
        BillWatchDbContext dbContext)
    {
        return new AdminSecurityController(
            dbContext,
            new FixedTimeProvider(NowUtc));
    }

    private static SubscriptionAccessKeyEntity CreateKey(
        Guid actorUserId,
        DateTimeOffset createdAtUtc,
        bool isRevoked = false,
        DateTimeOffset? expiresAtUtc = null,
        int redemptionCount = 0,
        int maxRedemptions = 2)
    {
        return new SubscriptionAccessKeyEntity
        {
            KeyHash = Guid.NewGuid().ToString("N"),
            DisplayPrefix = "BW-TEST-KEYS",
            Purpose = SubscriptionAccessKeyPurpose.Complimentary,
            Tier = BillWatchSubscriptionTier.Standard,
            DurationDays = 30,
            MaxRedemptions = maxRedemptions,
            RedemptionCount = redemptionCount,
            ExpiresAtUtc = expiresAtUtc,
            IsRevoked = isRevoked,
            RevokedAtUtc = isRevoked ? NowUtc : null,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = createdAtUtc
        };
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
