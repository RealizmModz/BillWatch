using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class BillExplanationService
{
    public BillExplanation CreateExplanation(
        BillStatementComparisonResult comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var meaningfulChanges = comparison.LineItemChanges
            .Where(change =>
                change.ChangeType != BillLineItemChangeType.Unchanged)
            .OrderByDescending(change =>
                Math.Abs(change.Difference))
            .Select(CreateLineItemExplanation)
            .ToList()
            .AsReadOnly();

        string summary = CreateSummary(comparison);

        return new BillExplanation(
            ProviderName: comparison.ProviderName,
            Summary: summary,
            MonthlyChange: comparison.TotalComparison.MonthlyChange,
            AnnualChange: comparison.TotalComparison.AnnualChange,
            Confidence: comparison.Confidence,
            Changes: meaningfulChanges,
            UnexplainedChange: comparison.UnexplainedChange);
    }

    private static BillExplanationItem CreateLineItemExplanation(
        BillLineItemChange change)
    {
        string impactText = FormatImpact(change.Difference);

        string description = change.ChangeType switch
        {
            BillLineItemChangeType.Added =>
                $"{change.Name} was added ({impactText}).",

            BillLineItemChangeType.Removed =>
                $"{change.Name} was removed ({impactText}).",

            BillLineItemChangeType.Increased =>
                $"{change.Name} increased ({impactText}).",

            BillLineItemChangeType.Decreased =>
                $"{change.Name} decreased ({impactText}).",

            _ =>
                $"{change.Name} did not change."
        };

        return new BillExplanationItem(
            Name: change.Name,
            Description: description,
            PreviousAmount: change.PreviousAmount,
            CurrentAmount: change.CurrentAmount,
            Impact: change.Difference,
            ChangeType: change.ChangeType);
    }

    private static string CreateSummary(
        BillStatementComparisonResult comparison)
    {
        decimal monthlyChange =
            comparison.TotalComparison.MonthlyChange;

        if (monthlyChange > 0m)
        {
            return
                $"Your {comparison.ProviderName} bill increased by " +
                $"{FormatCurrency(monthlyChange)} per month, " +
                $"or {FormatCurrency(comparison.TotalComparison.AnnualChange)} per year.";
        }

        if (monthlyChange < 0m)
        {
            return
                $"Your {comparison.ProviderName} bill decreased by " +
                $"{FormatCurrency(Math.Abs(monthlyChange))} per month, " +
                $"or {FormatCurrency(Math.Abs(comparison.TotalComparison.AnnualChange))} per year.";
        }

        return
            $"Your {comparison.ProviderName} bill did not change.";
    }

    private static string FormatImpact(decimal amount)
    {
        if (amount > 0m)
        {
            return $"+{FormatCurrency(amount)}";
        }

        if (amount < 0m)
        {
            return $"-{FormatCurrency(Math.Abs(amount))}";
        }

        return FormatCurrency(0m);
    }

    private static string FormatCurrency(decimal amount)
    {
        return $"${amount:0.00}";
    }
}

public sealed record BillExplanation(
    string ProviderName,
    string Summary,
    decimal MonthlyChange,
    decimal AnnualChange,
    BillExplanationConfidence Confidence,
    IReadOnlyList<BillExplanationItem> Changes,
    decimal UnexplainedChange);

public sealed record BillExplanationItem(
    string Name,
    string Description,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal Impact,
    BillLineItemChangeType ChangeType);