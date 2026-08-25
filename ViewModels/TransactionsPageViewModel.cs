using BillWatch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BillWatch.ViewModels;

public sealed class TransactionsPageViewModel : INotifyPropertyChanged
{
    private readonly BankDataService _bankDataService;

    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _summary = "Loading transaction history...";

    public TransactionsPageViewModel(
        BankDataService bankDataService)
    {
        _bankDataService = bankDataService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TransactionListItem> Transactions { get; } =
        new();

    public bool IsLoading
    {
        get => _isLoading;

        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;

        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public string Summary
    {
        get => _summary;

        private set
        {
            if (_summary == value)
            {
                return;
            }

            _summary = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var transactions =
                await _bankDataService.GetTransactionsAsync(
                    take: 100,
                    cancellationToken);

            Transactions.Clear();

            foreach (var transaction in transactions)
            {
                Transactions.Add(
                    new TransactionListItem(
                        transaction.Id,
                        transaction.MerchantName
                            ?? transaction.Name,
                        transaction.Name,
                        transaction.InstitutionName,
                        BuildAccountDescription(
                            transaction.AccountName,
                            transaction.AccountMask),
                        transaction.Amount,
                        FormatAmount(
                            transaction.Amount,
                            transaction.IsoCurrencyCode),
                        transaction.PostedDate,
                        transaction.PostedDate.ToString(
                            "MMM d, yyyy"),
                        transaction.IsPending,
                        transaction.IsPending
                            ? "Pending"
                            : "Posted",
                        transaction.CategoryPrimary
                            ?? "Uncategorized"));
            }

            Summary =
                Transactions.Count switch
                {
                    0 =>
                        "No bank transactions have been imported yet.",

                    1 =>
                        "1 transaction imported from your connected accounts.",

                    _ =>
                        $"{Transactions.Count} recent transactions imported from your connected accounts."
                };
        }
        catch
        {
            Transactions.Clear();

            Summary =
                "Transaction history is unavailable.";

            ErrorMessage =
                "BillWatch couldn't load your bank transactions. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string BuildAccountDescription(
        string accountName,
        string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
        {
            return accountName;
        }

        return $"{accountName} •••• {mask}";
    }

    private static string FormatAmount(
        decimal amount,
        string? currencyCode)
    {
        var formatted =
            amount.ToString("C");

        if (string.IsNullOrWhiteSpace(currencyCode) ||
            string.Equals(
                currencyCode,
                "USD",
                StringComparison.OrdinalIgnoreCase))
        {
            return formatted;
        }

        return $"{formatted} {currencyCode}";
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}

public sealed record TransactionListItem(
    Guid Id,
    string DisplayName,
    string OriginalName,
    string InstitutionName,
    string AccountDescription,
    decimal Amount,
    string FormattedAmount,
    DateOnly PostedDate,
    string FormattedDate,
    bool IsPending,
    string Status,
    string Category);