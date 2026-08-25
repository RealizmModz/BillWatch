using System.Collections.ObjectModel;

namespace BillWatch.Core.Models;

public enum BillCategory
{
    Unknown,
    Internet,
    MobilePhone,
    Electricity,
    NaturalGas,
    Utility,
    Other
}

public sealed record BillStream
{
    public Guid Id { get; }

    public string ProviderName { get; }

    public BillCategory Category { get; }

    public IReadOnlyList<BankTransaction> Transactions { get; }

    public IReadOnlyList<BillStatement> Statements { get; }

    public BillStream(
        Guid id,
        string providerName,
        BillCategory category,
        IEnumerable<BankTransaction>? transactions = null,
        IEnumerable<BillStatement>? statements = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "A bill stream must have a valid ID.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException(
                "A bill stream must have a provider name.",
                nameof(providerName));
        }

        Id = id;

        ProviderName =
            providerName.Trim();

        Category =
            category;

        Transactions =
            new ReadOnlyCollection<BankTransaction>(
                (transactions ?? [])
                .OrderBy(transaction => transaction.PostedDate)
                .ToList());

        Statements =
            new ReadOnlyCollection<BillStatement>(
                (statements ?? [])
                .OrderBy(statement => statement.BillingPeriodStart)
                .ToList());
    }

    public BankTransaction? LatestTransaction =>
        Transactions.LastOrDefault();

    public BillStatement? LatestStatement =>
        Statements.LastOrDefault();

    public decimal? LatestAmount =>
        LatestStatement?.TotalAmount.Amount
        ?? LatestTransaction?.Amount;
}