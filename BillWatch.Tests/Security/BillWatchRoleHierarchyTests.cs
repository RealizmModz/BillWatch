using BillWatch.API.Authorization;

namespace BillWatch.Tests.Security;

public sealed class BillWatchRoleHierarchyTests
{
    [Theory]
    [InlineData(BillWatchRoles.Owner, 300)]
    [InlineData(BillWatchRoles.Admin, 200)]
    [InlineData(BillWatchRoles.Moderator, 100)]
    [InlineData("User", 0)]
    [InlineData(null, 0)]
    public void GetRank_ReturnsExpectedHierarchy(
        string? roleName,
        int expectedRank)
    {
        Assert.Equal(
            expectedRank,
            BillWatchRoleHierarchy.GetRank(
                roleName));
    }

    [Theory]
    [InlineData(BillWatchRoles.Owner, BillWatchRoles.Admin, true)]
    [InlineData(BillWatchRoles.Owner, BillWatchRoles.Moderator, true)]
    [InlineData(BillWatchRoles.Admin, BillWatchRoles.Moderator, true)]
    [InlineData(BillWatchRoles.Admin, BillWatchRoles.Admin, false)]
    [InlineData(BillWatchRoles.Moderator, BillWatchRoles.Admin, false)]
    [InlineData(BillWatchRoles.Moderator, BillWatchRoles.Moderator, false)]
    [InlineData(BillWatchRoles.Owner, BillWatchRoles.Owner, false)]
    [InlineData(BillWatchRoles.Admin, BillWatchRoles.Owner, false)]
    [InlineData("User", BillWatchRoles.Moderator, false)]
    public void CanAssignRole_EnforcesStaffHierarchy(
        string actorRole,
        string targetRole,
        bool expected)
    {
        Assert.Equal(
            expected,
            BillWatchRoleHierarchy.CanAssignRole(
                actorRole,
                targetRole));
    }

    [Theory]
    [InlineData(BillWatchRoles.Owner, BillWatchRoles.Admin, true)]
    [InlineData(BillWatchRoles.Owner, BillWatchRoles.Moderator, true)]
    [InlineData(BillWatchRoles.Owner, null, true)]
    [InlineData(BillWatchRoles.Admin, BillWatchRoles.Moderator, true)]
    [InlineData(BillWatchRoles.Admin, null, true)]
    [InlineData(BillWatchRoles.Admin, BillWatchRoles.Owner, false)]
    [InlineData(BillWatchRoles.Admin, BillWatchRoles.Admin, false)]
    [InlineData(BillWatchRoles.Moderator, null, true)]
    [InlineData(BillWatchRoles.Moderator, BillWatchRoles.Moderator, false)]
    [InlineData("User", null, false)]
    public void CanManageUser_EnforcesStaffHierarchy(
        string actorRole,
        string? targetHighestRole,
        bool expected)
    {
        Assert.Equal(
            expected,
            BillWatchRoleHierarchy.CanManageUser(
                actorRole,
                targetHighestRole));
    }
}
