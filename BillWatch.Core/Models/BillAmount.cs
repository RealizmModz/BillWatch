namespace BillWatch.Core.Models;

public sealed record BillAmount
{
    public decimal Amount { get; }

    public BillAmount(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "A bill amount cannot be negative.");
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}