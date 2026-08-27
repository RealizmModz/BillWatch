using BillWatch.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BillWatch.ViewModels;

public sealed class BillsPageViewModel : INotifyPropertyChanged
{
    private readonly BillStreamService _billStreamService;

    private int _billsMonitored;
    private decimal _monthlyTotal;
    private IReadOnlyList<BillListItem> _bills = [];
    private bool _isLoading;
    private string _errorMessage = string.Empty;

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
        if (IsLoading)
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
                {
                    var monthlyChange =
                        decimal.Round(
                            stream.CurrentAmount -
                            stream.PreviousAverage,
                            2,
                            MidpointRounding.AwayFromZero);

                    var annualImpact =
                        decimal.Round(
                            monthlyChange * 12m,
                            2,
                            MidpointRounding.AwayFromZero);

                    var hasMeaningfulChange =
                        stream.PreviousAverage > 0m &&
                        Math.Abs(monthlyChange) >= 5m &&
                        Math.Abs(
                            monthlyChange /
                            stream.PreviousAverage) >= 0.10m;

                    return new BillListItem(
                        Id: stream.Id,
                        ProviderName: stream.ProviderName,
                        Category: FormatCategory(stream.Category),
                        CurrentAmount: stream.CurrentAmount,
                        PreviousAverage: stream.PreviousAverage,
                        MonthlyChange: monthlyChange,
                        AnnualImpact: annualImpact,
                        HasMeaningfulChange: hasMeaningfulChange,
                        Status: hasMeaningfulChange
                            ? "Changed"
                            : stream.IsActive
                                ? "Watching"
                                : "Inactive");
                })
                .OrderBy(
                    bill => bill.ProviderName,
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
    Guid Id,
    string ProviderName,
    string Category,
    decimal CurrentAmount,
    decimal PreviousAverage,
    decimal MonthlyChange,
    decimal AnnualImpact,
    bool HasMeaningfulChange,
    string Status);