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

        if (EqualsCategory(
                categoryPrimary,
                "RENT_AND_UTILITIES"))
        {
            return TryClassifyRentAndUtilities(
                categoryDetailed,
                out category);
        }

        if (EqualsCategory(
                categoryPrimary,
                "LOAN_PAYMENTS"))
        {
            return TryClassifyLoanPayment(
                categoryDetailed,
                out category);
        }

        if (EqualsCategory(
                categoryPrimary,
                "ENTERTAINMENT"))
        {
            return TryClassifyEntertainment(
                categoryDetailed,
                out category);
        }

        if (EqualsCategory(
                categoryPrimary,
                "GENERAL_SERVICES") &&
            (
                ContainsCategory(
                    categoryDetailed,
                    "INSURANCE") ||
                ContainsCategory(
                    categoryDetailed,
                    "STORAGE")
            ))
        {
            category =
                BillCategory.Other;

            return true;
        }

        return false;
    }

    private static bool TryClassifyRentAndUtilities(
        string? categoryDetailed,
        out BillCategory category)
    {
        category =
            BillCategory.Unknown;

        if (ContainsCategory(
                categoryDetailed,
                "INTERNET_AND_CABLE"))
        {
            category =
                BillCategory.Internet;

            return true;
        }

        if (ContainsCategory(
                categoryDetailed,
                "TELEPHONE"))
        {
            category =
                BillCategory.MobilePhone;

            return true;
        }

        if (ContainsCategory(
                categoryDetailed,
                "GAS_AND_ELECTRICITY") ||
            ContainsCategory(
                categoryDetailed,
                "WATER") ||
            ContainsCategory(
                categoryDetailed,
                "SEWAGE_AND_WASTE") ||
            ContainsCategory(
                categoryDetailed,
                "OTHER_UTILITIES"))
        {
            category =
                BillCategory.Utility;

            return true;
        }

        if (ContainsCategory(
                categoryDetailed,
                "RENT"))
        {
            category =
                BillCategory.Other;

            return true;
        }

        return false;
    }

    private static bool TryClassifyLoanPayment(
        string? categoryDetailed,
        out BillCategory category)
    {
        category =
            BillCategory.Unknown;

        if (string.IsNullOrWhiteSpace(
                categoryDetailed))
        {
            return false;
        }

        /*
         * Cash advances and earned-wage advances are borrowing events,
         * not recurring bills BillWatch should promote into Bill Streams.
         */
        if (ContainsCategory(
                categoryDetailed,
                "CASH_ADVANCES") ||
            ContainsCategory(
                categoryDetailed,
                "EWA"))
        {
            return false;
        }

        /*
         * Credit-card, personal, auto, mortgage, student, BNPL, and other
         * true loan repayments are valid bill candidates. The recurring
         * detector still has to prove cadence before a Bill Stream exists.
         */
        category =
            BillCategory.Other;

        return true;
    }

    private static bool TryClassifyEntertainment(
        string? categoryDetailed,
        out BillCategory category)
    {
        category =
            BillCategory.Unknown;

        /*
         * These detailed categories commonly contain streaming/media
         * subscriptions. Broad entertainment and video-game purchases stay
         * excluded so ordinary discretionary spending is not promoted merely
         * because it happens to repeat.
         */
        if (!ContainsCategory(
                categoryDetailed,
                "TV_AND_MOVIES") &&
            !ContainsCategory(
                categoryDetailed,
                "MUSIC_AND_AUDIO"))
        {
            return false;
        }

        category =
            BillCategory.Other;

        return true;
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