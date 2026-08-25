namespace BillWatch.Core.Models;

public sealed record BillLineItem
{
    public string Name { get; }

    public decimal Amount { get; }

    public BillLineItem(
        string name,
        decimal amount)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A bill line item must have a name.",
                nameof(name));
        }

        Name = name.Trim();

        Amount = decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero);
    }
}