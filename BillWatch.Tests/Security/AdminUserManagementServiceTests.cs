using BillWatch.API.Authorization;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Security;

public sealed class AdminUserManagementServiceTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignRoleAsync_OwnerCanAssignAdminAndAudit()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, BillWatchRoles.Owner);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).AssignRoleAsync(
            owner.Id,
            target.Id,
            BillWatchRoles.Admin);

        Assert.True(result.Succeeded);
        Assert.True(await HasRole(dbContext, target.Id, BillWatchRoles.Admin));
        Assert.Single(dbContext.AdminAuditLogs);
    }

    [Fact]
    public async Task AssignRoleAsync_NeverAssignsOwnerThroughNormalPath()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, BillWatchRoles.Owner);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).AssignRoleAsync(
            owner.Id,
            target.Id,
            BillWatchRoles.Owner);

        Assert.False(result.Succeeded);
        Assert.False(await HasRole(dbContext, target.Id, BillWatchRoles.Owner));
    }

    [Fact]
    public async Task RemoveRoleAsync_RejectsSelfDemotion()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        await AssignSeededRole(dbContext, owner.Id, BillWatchRoles.Owner);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).RemoveRoleAsync(
            owner.Id,
            owner.Id,
            BillWatchRoles.Owner);

        Assert.False(result.Succeeded);
        Assert.True(await HasRole(dbContext, owner.Id, BillWatchRoles.Owner));
    }

    [Fact]
    public async Task AssignRoleAsync_AdminCannotManagePeer()
    {
        await using var dbContext = CreateDbContext();
        var actor = AddUser(dbContext, "actor@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, actor.Id, BillWatchRoles.Admin);
        await AssignSeededRole(dbContext, target.Id, BillWatchRoles.Admin);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).AssignRoleAsync(
            actor.Id,
            target.Id,
            BillWatchRoles.Moderator);

        Assert.False(result.Succeeded);
        Assert.False(await HasRole(dbContext, target.Id, BillWatchRoles.Moderator));
        Assert.Empty(dbContext.AdminAuditLogs);
    }

    [Fact]
    public async Task GrantAndRevokeEntitlement_AreTargetScopedAndAudited()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, BillWatchRoles.Owner);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var grant = await service.GrantEntitlementAsync(
            owner.Id,
            target.Id,
            BillWatchSubscriptionTier.Standard,
            durationDays: 30,
            grantsLifetimeAccess: false);

        Assert.True(grant.Succeeded);
        Assert.NotNull(grant.ResourceId);
        Assert.True((await service.RevokeEntitlementAsync(
            owner.Id,
            target.Id,
            grant.ResourceId!.Value)).Succeeded);

        var entitlement = await dbContext.SubscriptionEntitlements.SingleAsync();
        Assert.True(entitlement.IsRevoked);
        Assert.Equal(NowUtc, entitlement.RevokedAtUtc);
        Assert.Equal(2, await dbContext.AdminAuditLogs.CountAsync());
    }

    [Fact]
    public async Task SetProgramMembershipAsync_UpdatesMembershipWithoutGrantingStaffRole()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, BillWatchRoles.Owner);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        Assert.True((await service.SetProgramMembershipAsync(
            owner.Id,
            target.Id,
            UserProgramType.BetaTester,
            isActive: true,
            endsAtUtc: NowUtc.AddDays(90))).Succeeded);

        var membership = await dbContext.UserProgramMemberships.SingleAsync();
        Assert.True(membership.IsActive);
        Assert.Equal(UserProgramType.BetaTester, membership.Program);
        Assert.False(await HasRole(dbContext, target.Id, BillWatchRoles.Moderator));
        Assert.Empty(dbContext.SubscriptionEntitlements);
    }

    private static ApplicationUser AddUser(
        BillWatchDbContext dbContext,
        string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };
        dbContext.Users.Add(user);
        return user;
    }

    private static async Task AssignSeededRole(
        BillWatchDbContext dbContext,
        Guid userId,
        string roleName)
    {
        var role = await dbContext.Roles.SingleAsync(
            candidate => candidate.Name == roleName);
        dbContext.UserRoles.Add(
            new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = role.Id
            });
    }

    private static async Task<bool> HasRole(
        BillWatchDbContext dbContext,
        Guid userId,
        string roleName)
    {
        return await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.Name == roleName
            select userRole).AnyAsync();
    }

    private static AdminUserManagementService CreateService(
        BillWatchDbContext dbContext)
    {
        return new AdminUserManagementService(
            dbContext,
            new FixedTimeProvider(NowUtc));
    }

    private static BillWatchDbContext CreateDbContext()
    {
        var dbContext = new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
