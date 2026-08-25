namespace BillWatch.Core.Models;

public sealed record BankTransaction
{
    public string MerchantName { get; }

    public DateOnly PostedDate { get; }

    public decimal Amount { get; }

    public bool IsPending { get; }

    public BankTransaction(
        string merchantName,
        DateOnly postedDate,
        decimal amount,
        bool isPending = false)
    {
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            throw new ArgumentException(
                "A bank transaction must have a merchant name.",
                nameof(merchantName));
        }

        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "A bill payment transaction amount cannot be negative.");
        }

        MerchantName = merchantName.Trim();
        PostedDate = postedDate;

        Amount = decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero);

        IsPending = isPending;
    }
}