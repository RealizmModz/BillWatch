namespace BillWatch.API.Authorization;

public static class BillWatchRoles
{
    public const string Owner = "Owner";

    public const string Admin = "Admin";

    public const string Moderator = "Moderator";

    public static readonly IReadOnlySet<string> StaffRoles =
        new HashSet<string>(
            [
                Owner,
                Admin,
                Moderator
            ],
            StringComparer.Ordinal);

    public static bool IsStaffRole(
        string? roleName)
    {
        return !string.IsNullOrWhiteSpace(
                roleName) &&
            StaffRoles.Contains(
                roleName.Trim());
    }
}
