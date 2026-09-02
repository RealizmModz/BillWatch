namespace BillWatch.API.Authorization;

public static class BillWatchPolicies
{
    public const string OwnerOnly =
        "BillWatch.OwnerOnly";

    public const string AdminOrOwner =
        "BillWatch.AdminOrOwner";

    public const string ModeratorOrAbove =
        "BillWatch.ModeratorOrAbove";

    public const string ActiveSubscription =
        "BillWatch.ActiveSubscription";
}
