using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class BillStatementComparisonService
{
    private readonly BillComparisonService _billComparisonService = new();

    public BillStatementComparisonResult Compare(
        BillStatement previousStatement,
        BillStatement currentStatement)
    {
        ArgumentNullException.ThrowIfNull(previousStatement);
        ArgumentNullException.ThrowIfNull(currentStatement);

        if (!string.Equals(
                previousStatement.ProviderName,
                currentStatement.ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Statements from different providers cannot be compared.");
        }

        var totalComparison = _billComparisonService.Compare(
            previousStatement.TotalAmount,
            currentStatement.TotalAmount);

        var previousItems = CombineLineItems(
            previousStatement.LineItems);

        var currentItems = CombineLineItems(
            currentStatement.LineItems);

        var names = previousItems.Keys
            .Union(
                currentItems.Keys,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase);

        var changes = new List<BillLineItemChange>();

        foreach (var name in names)
        {
            previousItems.TryGetValue(
                name,
                out decimal previousAmount);

            currentItems.TryGetValue(
                name,
                out decimal currentAmount);

            changes.Add(
                new BillLineItemChange(
                    name,
                    previousAmount,
                    currentAmount));
        }

        decimal explainedChange = decimal.Round(
            changes.Sum(change => change.Difference),
            2,
            MidpointRounding.AwayFromZero);

        decimal unexplainedChange = decimal.Round(
            totalComparison.MonthlyChange - explainedChange,
            2,
            MidpointRounding.AwayFromZero);

        bool isFullyExplained =
            Math.Abs(unexplainedChange) <= 0.01m;

        var confidence = DetermineConfidence(
            totalComparison.MonthlyChange,
            unexplainedChange,
            isFullyExplained);

        return new BillStatementComparisonResult(
            ProviderName: previousStatement.ProviderName,
            TotalComparison: totalComparison,
            LineItemChanges: changes.AsReadOnly(),
            ExplainedChange: explainedChange,
            UnexplainedChange: unexplainedChange,
            IsFullyExplained: isFullyExplained,
            Confidence: confidence);
    }

    private static BillExplanationConfidence DetermineConfidence(
        decimal totalChange,
        decimal unexplainedChange,
        bool isFullyExplained)
    {
        if (isFullyExplained)
        {
            return BillExplanationConfidence.Confirmed;
        }

        if (totalChange == 0m)
        {
            return BillExplanationConfidence.Unknown;
        }

        decimal unexplainedPercentage =
            Math.Abs(unexplainedChange / totalChange) * 100m;

        if (unexplainedPercentage <= 10m)
        {
            return BillExplanationConfidence.StrongInference;
        }

        if (unexplainedPercentage <= 50m)
        {
            return BillExplanationConfidence.Possible;
        }

        return BillExplanationConfidence.Unknown;
    }

    private static Dictionary<string, decimal> CombineLineItems(
        IReadOnlyList<BillLineItem> lineItems)
    {
        var combined = new Dictionary<string, decimal>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in lineItems)
        {
            if (combined.TryGetValue(
                    item.Name,
                    out decimal existingAmount))
            {
                combined[item.Name] = decimal.Round(
                    existingAmount + item.Amount,
                    2,
                    MidpointRounding.AwayFromZero);
            }
            else
            {
                combined[item.Name] = item.Amount;
            }
        }

        return combined;
    }
}

public enum BillExplanationConfidence
{
    Confirmed,
    StrongInference,
    Possible,
    Unknown
}

public sealed record BillStatementComparisonResult(
    string ProviderName,
    BillComparisonResult TotalComparison,
    IReadOnlyList<BillLineItemChange> LineItemChanges,
    decimal ExplainedChange,
    decimal UnexplainedChange,
    bool IsFullyExplained,
    BillExplanationConfidence Confidence);