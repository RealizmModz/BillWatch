using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class BillTransactionReconciliationService
{
    public BillTransactionReconciliationResult Reconcile(
        BankTransaction transaction,
        BillStatement statement)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(statement);

        bool merchantMatches =
            string.Equals(
                transaction.MerchantName,
                statement.ProviderName,
                StringComparison.OrdinalIgnoreCase);

        decimal difference = decimal.Round(
            transaction.Amount - statement.TotalAmount.Amount,
            2,
            MidpointRounding.AwayFromZero);

        bool amountMatches =
            Math.Abs(difference) <= 0.01m;

        bool isConfirmedMatch =
            merchantMatches &&
            amountMatches &&
            !transaction.IsPending;

        return new BillTransactionReconciliationResult(
            MerchantMatches: merchantMatches,
            AmountMatches: amountMatches,
            TransactionAmount: transaction.Amount,
            StatementAmount: statement.TotalAmount.Amount,
            Difference: difference,
            IsPending: transaction.IsPending,
            IsConfirmedMatch: isConfirmedMatch);
    }
}

public sealed record BillTransactionReconciliationResult(
    bool MerchantMatches,
    bool AmountMatches,
    decimal TransactionAmount,
    decimal StatementAmount,
    decimal Difference,
    bool IsPending,
    bool IsConfirmedMatch);