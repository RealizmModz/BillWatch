using BillWatch.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BillWatch.ViewModels;

public sealed class BillsPageViewModel : INotifyPropertyChanged
{
    private readonly BillStreamService? _billStreamService;

    private int _billsMonitored;
    private decimal _monthlyTotal;
    private IReadOnlyList<BillListItem> _bills = [];
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    // Temporary constructor so the existing BillsPage still builds.
    // We will remove this when we inject the ViewModel into the page.
    public BillsPageViewModel()
    {
    }

    public BillsPageViewModel(
        BillStreamService billStreamService)
    {
        _billStreamService = billStreamService;
    }

    public int BillsMonitored
    {
        get => _billsMonitored;
        private set
        {
            if (_billsMonitored == value)
            {
                return;
            }

            _billsMonitored = value;
            OnPropertyChanged();
        }
    }

    public decimal MonthlyTotal
    {
        get => _monthlyTotal;
        private set
        {
            if (_monthlyTotal == value)
            {
                return;
            }

            _monthlyTotal = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<BillListItem> Bills
    {
        get => _bills;
        private set
        {
            if (ReferenceEquals(_bills, value))
            {
                return;
            }

            _bills = value;
            OnPropertyChanged();
        }
    }

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

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (_billStreamService is null || IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var billStreams =
                await _billStreamService.GetBillStreamsAsync(
                    cancellationToken);

            Bills = billStreams
                .Select(stream =>
                    new BillListItem(
                        ProviderName: stream.ProviderName,
                        Category: FormatCategory(stream.Category),
                        CurrentAmount: 0m,
                        PreviousAverage: 0m,
                        MonthlyChange: 0m,
                        AnnualImpact: 0m,
                        HasMeaningfulChange: false,
                        Status: stream.IsActive
                            ? "Watching"
                            : "Inactive"))
                .OrderBy(bill =>
                    bill.ProviderName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();

            BillsMonitored = Bills.Count;

            MonthlyTotal = decimal.Round(
                Bills.Sum(bill => bill.CurrentAmount),
                2,
                MidpointRounding.AwayFromZero);
        }
        catch (HttpRequestException)
        {
            ErrorMessage =
                "Unable to load your bills from BillWatch.";
        }
        catch (Exception)
        {
            ErrorMessage =
                "Something went wrong while loading your bills.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatCategory(
        string category)
    {
        return category switch
        {
            "MobilePhone" => "Mobile phone",
            "NaturalGas" => "Natural gas",
            _ => category
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record BillListItem(
    string ProviderName,
    string Category,
    decimal CurrentAmount,
    decimal PreviousAverage,
    decimal MonthlyChange,
    decimal AnnualImpact,
    bool HasMeaningfulChange,
    string Status);