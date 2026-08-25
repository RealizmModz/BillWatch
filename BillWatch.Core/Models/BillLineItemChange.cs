namespace BillWatch.Core.Models;

public enum BillLineItemChangeType
{
    Added,
    Removed,
    Increased,
    Decreased,
    Unchanged
}

public sealed record BillLineItemChange
{
    public string Name { get; }

    public decimal PreviousAmount { get; }

    public decimal CurrentAmount { get; }

    public decimal Difference { get; }

    public BillLineItemChangeType ChangeType { get; }

    public BillLineItemChange(
        string name,
        decimal previousAmount,
        decimal currentAmount)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A bill line-item change must have a name.",
                nameof(name));
        }

        Name = name.Trim();

        PreviousAmount = decimal.Round(
            previousAmount,
            2,
            MidpointRounding.AwayFromZero);

        CurrentAmount = decimal.Round(
            currentAmount,
            2,
            MidpointRounding.AwayFromZero);

        Difference = decimal.Round(
            CurrentAmount - PreviousAmount,
            2,
            MidpointRounding.AwayFromZero);

        ChangeType = DetermineChangeType(
            PreviousAmount,
            CurrentAmount);
    }

    private static BillLineItemChangeType DetermineChangeType(
        decimal previousAmount,
        decimal currentAmount)
    {
        if (previousAmount == 0m && currentAmount != 0m)
        {
            return BillLineItemChangeType.Added;
        }

        if (previousAmount != 0m && currentAmount == 0m)
        {
            return BillLineItemChangeType.Removed;
        }

        if (currentAmount > previousAmount)
        {
            return BillLineItemChangeType.Increased;
        }

        if (currentAmount < previousAmount)
        {
            return BillLineItemChangeType.Decreased;
        }

        return BillLineItemChangeType.Unchanged;
    }
}