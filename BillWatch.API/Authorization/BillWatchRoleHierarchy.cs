namespace BillWatch.API.Authorization;

public static class BillWatchRoleHierarchy
{
    public static int GetRank(
        string? roleName)
    {
        return roleName switch
        {
            BillWatchRoles.Owner => 300,
            BillWatchRoles.Admin => 200,
            BillWatchRoles.Moderator => 100,
            _ => 0
        };
    }

    public static bool CanAssignRole(
        string actorRole,
        string targetRole)
    {
        if (!BillWatchRoles.IsStaffRole(
                actorRole) ||
            !BillWatchRoles.IsStaffRole(
                targetRole))
        {
            return false;
        }

        /*
         * Owner is a bootstrap/recovery role, not a role that the normal
         * admin console may mint. This prevents a compromised staff account
         * from creating another top-level principal through ordinary UI/API
         * flows. Owner changes require the explicit owner-recovery path.
         */
        if (string.Equals(
                targetRole,
                BillWatchRoles.Owner,
                StringComparison.Ordinal))
        {
            return false;
        }

        return GetRank(actorRole) >
            GetRank(targetRole);
    }

    public static bool CanManageUser(
        string actorRole,
        string? targetHighestRole)
    {
        if (!BillWatchRoles.IsStaffRole(
                actorRole))
        {
            return false;
        }

        return GetRank(actorRole) >
            GetRank(targetHighestRole);
    }
}
