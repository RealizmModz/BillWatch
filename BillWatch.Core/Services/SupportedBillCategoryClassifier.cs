using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class SupportedBillCategoryClassifier
{
    public bool TryClassify(
        string? categoryPrimary,
        string? categoryDetailed,
        out BillCategory category)
    {
        category =
            BillCategory.Unknown;

        if (!EqualsCategory(
                categoryPrimary,
                "RENT_AND_UTILITIES"))
        {
            return false;
        }

        if (ContainsCategory(
                categoryDetailed,
                "INTERNET_AND_CABLE"))
        {
            category =
                BillCategory.Internet;

            return true;
        }

        return false;
    }

    private static bool EqualsCategory(
        string? value,
        string expected)
    {
        return string.Equals(
            value,
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCategory(
        string? value,
        string expected)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(
                   expected,
                   StringComparison.OrdinalIgnoreCase);
    }
}