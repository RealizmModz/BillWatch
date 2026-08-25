using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class BillStreamDiscoveryService
{
    private readonly RecurringBillDetectionService _recurringBillDetectionService;

    public BillStreamDiscoveryService()
    {
        _recurringBillDetectionService =
            new RecurringBillDetectionService();
    }

    public IReadOnlyList<BillStream> Discover(
        IEnumerable<BankTransaction> transactions,
        IEnumerable<BillStatement>? statements = null)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var transactionList =
            transactions.ToList();

        var statementList =
            statements?.ToList()
            ?? [];

        var detectedBills =
            _recurringBillDetectionService.Detect(
                transactionList);

        var streams =
            new List<BillStream>();

        foreach (var detectedBill in detectedBills)
        {
            var matchingTransactions =
                transactionList
                    .Where(transaction =>
                        string.Equals(
                            transaction.MerchantName,
                            detectedBill.MerchantName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(transaction =>
                        transaction.PostedDate)
                    .ToList();

            var matchingStatements =
                statementList
                    .Where(statement =>
                        string.Equals(
                            statement.ProviderName,
                            detectedBill.MerchantName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(statement =>
                        statement.BillingPeriodStart)
                    .ToList();

            var category =
                DetermineCategory(
                    detectedBill.MerchantName);

            streams.Add(
                new BillStream(
                    id: Guid.NewGuid(),
                    providerName: detectedBill.MerchantName,
                    category: category,
                    transactions: matchingTransactions,
                    statements: matchingStatements));
        }

        return streams
            .OrderBy(
                stream => stream.ProviderName,
                StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private static BillCategory DetermineCategory(
        string providerName)
    {
        if (providerName.Contains(
                "Midco",
                StringComparison.OrdinalIgnoreCase))
        {
            return BillCategory.Internet;
        }

        if (providerName.Contains(
                "Verizon",
                StringComparison.OrdinalIgnoreCase))
        {
            return BillCategory.MobilePhone;
        }

        if (providerName.Contains(
                "Black Hills Energy",
                StringComparison.OrdinalIgnoreCase))
        {
            return BillCategory.Utility;
        }

        return BillCategory.Unknown;
    }
}