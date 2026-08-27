using BillWatch.Services;

namespace BillWatch;

public partial class BillDetailPage : ContentPage
{
    private const long MaxUploadSizeBytes =
        15L * 1024 * 1024;

    private readonly BillStreamService
        _billStreamService;

    private Guid
        _billStreamId;

    private bool
        _hasLoaded;

    private bool
        _isLoading;

    private bool
        _isUploading;

    private bool
        _uploadSucceeded;

    private string
        _errorMessage =
            string.Empty;

    private string
        _uploadMessage =
            string.Empty;

    private BillStreamDetailResult?
        _detail;

    private IReadOnlyList<BillStatementDisplayItem>
        _statements =
            [];

    public BillDetailPage(
        BillStreamService billStreamService)
    {
        InitializeComponent();

        _billStreamService =
            billStreamService;

        BindingContext =
            this;
    }

    public string BillStreamId
    {
        get =>
            _billStreamId.ToString();

        set
        {
            if (!Guid.TryParse(
                    value,
                    out var id))
            {
                return;
            }

            if (_billStreamId ==
                id)
            {
                return;
            }

            _billStreamId =
                id;

            _hasLoaded =
                false;
        }
    }

    public bool IsLoading
    {
        get =>
            _isLoading;

        private set
        {
            if (_isLoading ==
                value)
            {
                return;
            }

            _isLoading =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(HasContent));
        }
    }

    public bool IsUploading
    {
        get =>
            _isUploading;

        private set
        {
            if (_isUploading ==
                value)
            {
                return;
            }

            _isUploading =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(CanUpload));
        }
    }

    public bool CanUpload =>
        !IsUploading &&
        !IsLoading &&
        _billStreamId != Guid.Empty;

    public string UploadMessage
    {
        get =>
            _uploadMessage;

        private set
        {
            if (_uploadMessage ==
                value)
            {
                return;
            }

            _uploadMessage =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(HasUploadSuccess));

            OnPropertyChanged(
                nameof(HasUploadError));
        }
    }

    public bool HasUploadSuccess =>
        _uploadSucceeded &&
        !string.IsNullOrWhiteSpace(
            UploadMessage);

    public bool HasUploadError =>
        !_uploadSucceeded &&
        !string.IsNullOrWhiteSpace(
            UploadMessage);

    public string ErrorMessage
    {
        get =>
            _errorMessage;

        private set
        {
            if (_errorMessage ==
                value)
            {
                return;
            }

            _errorMessage =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(HasError));

            OnPropertyChanged(
                nameof(HasContent));
        }
    }

    public bool HasError =>
        !string.IsNullOrWhiteSpace(
            ErrorMessage);

    public bool HasContent =>
        !IsLoading &&
        !HasError &&
        _detail is not null;

    public string ProviderName =>
        _detail?.ProviderName
        ?? "Bill details";

    public string Category =>
        FormatCategory(
            _detail?.Category);

    public string CurrentAmountText =>
        _detail is null
            ? "$0.00"
            : FormatMoney(
                _detail.CurrentAmount);

    public string PreviousAverageText
    {
        get
        {
            if (_detail is null ||
                _detail.PreviousAverage <=
                0m)
            {
                return
                    "Not enough history for an average yet.";
            }

            return
                $"Previous average: {FormatMoney(_detail.PreviousAverage)}";
        }
    }

    public BillChangeResult?
        LatestChange =>
            _detail?
                .Changes
                .OrderByDescending(
                    change =>
                        change.DetectedAtUtc)
                .FirstOrDefault();

    public bool HasDetectedChange =>
        LatestChange is not null;

    public bool HasNoDetectedChange =>
        !HasDetectedChange;

    public string ChangeSummaryText
    {
        get
        {
            var change =
                LatestChange;

            if (change is null)
            {
                return
                    string.Empty;
            }

            var difference =
                change.AmountDifference;

            if (difference > 0m)
            {
                return
                    $"+{FormatMoney(difference)}/month";
            }

            if (difference < 0m)
            {
                return
                    $"-{FormatMoney(Math.Abs(difference))}/month";
            }

            return
                "No price difference";
        }
    }

    public string AnnualImpactText
    {
        get
        {
            var change =
                LatestChange;

            if (change is null)
            {
                return
                    string.Empty;
            }

            var impact =
                change.AnnualizedImpact;

            if (impact > 0m)
            {
                return
                    $"+{FormatMoney(impact)} per year";
            }

            if (impact < 0m)
            {
                return
                    $"{FormatMoney(Math.Abs(impact))} less per year";
            }

            return
                "No annual cost impact";
        }
    }

    public string ExplanationText
    {
        get
        {
            var description =
                LatestChange?
                    .Description;

            return
                string.IsNullOrWhiteSpace(
                    description)
                    ? "BillWatch detected a change, but does not yet have enough evidence to explain the cause."
                    : description;
        }
    }

    public string ConfidenceText =>
        FormatConfidence(
            LatestChange?
                .Confidence);

    public IReadOnlyList<BillStatementDisplayItem>
        Statements =>
            _statements;

    public bool HasNoStatements =>
        Statements.Count ==
        0;

    protected override async void
        OnAppearing()
    {
        base.OnAppearing();

        if (_hasLoaded)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync(
        CancellationToken cancellationToken =
            default)
    {
        if (IsLoading)
        {
            return;
        }

        if (_billStreamId ==
            Guid.Empty)
        {
            ErrorMessage =
                "BillWatch could not determine which bill to open.";

            return;
        }

        try
        {
            IsLoading =
                true;

            ErrorMessage =
                string.Empty;

            _detail =
                await _billStreamService
                    .GetBillStreamDetailAsync(
                        _billStreamId,
                        cancellationToken);

            _statements =
                _detail
                    .Statements
                    .OrderByDescending(
                        statement =>
                            statement.PeriodEnd)
                    .Select(
                        statement =>
                            new BillStatementDisplayItem(
                                PeriodText:
                                    FormatPeriod(
                                        statement),

                                AmountText:
                                    FormatMoney(
                                        statement.TotalAmount),

                                DueDateText:
                                    FormatDueDate(
                                        statement)))
                    .ToList()
                    .AsReadOnly();

            _hasLoaded =
                true;

            NotifyDetailChanged();
        }
        catch (SessionExpiredException)
        {
            ErrorMessage =
                "Your BillWatch session expired. Please sign in again.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage =
                "Unable to load this bill from BillWatch.";
        }
        catch (Exception)
        {
            ErrorMessage =
                "Something went wrong while loading this bill.";
        }
        finally
        {
            IsLoading =
                false;
        }
    }

    private async void OnUploadStatementClicked(
        object? sender,
        EventArgs e)
    {
        if (IsUploading ||
            _billStreamId ==
                Guid.Empty)
        {
            return;
        }

        try
        {
            ClearUploadMessage();

            var selectedFile =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            "Choose bill statement"
                    });

            if (selectedFile is null)
            {
                return;
            }

            var extension =
                Path.GetExtension(
                        selectedFile.FileName)
                    .ToLowerInvariant();

            if (extension is not
                ".pdf" and not
                ".jpg" and not
                ".jpeg" and not
                ".png")
            {
                SetUploadError(
                    "Choose a PDF, JPG, JPEG, or PNG bill statement.");

                return;
            }

            await using var fileStream =
                await selectedFile
                    .OpenReadAsync();

            if (fileStream.CanSeek &&
                fileStream.Length >
                    MaxUploadSizeBytes)
            {
                SetUploadError(
                    "This statement is larger than the 15 MB upload limit.");

                return;
            }

            IsUploading =
                true;

            var mediaType =
                GetMediaType(
                    extension);

            var result =
                await _billStreamService
                    .UploadStatementAsync(
                        _billStreamId,
                        fileStream,
                        selectedFile.FileName,
                        mediaType);

            _uploadSucceeded =
                true;

            UploadMessage =
                result.Status == "Uploaded"
                    ? "Statement uploaded securely. It is ready for BillWatch's document-processing pipeline."
                    : $"Statement received. Current status: {result.Status}.";
        }
        catch (SessionExpiredException)
        {
            SetUploadError(
                "Your BillWatch session expired. Please sign in again.");
        }
        catch (HttpRequestException ex)
        {
            SetUploadError(
                string.IsNullOrWhiteSpace(
                    ex.Message)
                    ? "BillWatch could not upload this statement."
                    : ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            SetUploadError(
                "BillWatch could not access the selected file.");
        }
        catch (Exception)
        {
            SetUploadError(
                "Something went wrong while uploading this statement.");
        }
        finally
        {
            IsUploading =
                false;
        }
    }

    private void ClearUploadMessage()
    {
        _uploadSucceeded =
            false;

        UploadMessage =
            string.Empty;

        OnPropertyChanged(
            nameof(HasUploadSuccess));

        OnPropertyChanged(
            nameof(HasUploadError));
    }

    private void SetUploadError(
        string message)
    {
        _uploadSucceeded =
            false;

        UploadMessage =
            message;

        OnPropertyChanged(
            nameof(HasUploadSuccess));

        OnPropertyChanged(
            nameof(HasUploadError));
    }

    private void NotifyDetailChanged()
    {
        OnPropertyChanged(
            nameof(ProviderName));

        OnPropertyChanged(
            nameof(Category));

        OnPropertyChanged(
            nameof(CurrentAmountText));

        OnPropertyChanged(
            nameof(PreviousAverageText));

        OnPropertyChanged(
            nameof(LatestChange));

        OnPropertyChanged(
            nameof(HasDetectedChange));

        OnPropertyChanged(
            nameof(HasNoDetectedChange));

        OnPropertyChanged(
            nameof(ChangeSummaryText));

        OnPropertyChanged(
            nameof(AnnualImpactText));

        OnPropertyChanged(
            nameof(ExplanationText));

        OnPropertyChanged(
            nameof(ConfidenceText));

        OnPropertyChanged(
            nameof(Statements));

        OnPropertyChanged(
            nameof(HasNoStatements));

        OnPropertyChanged(
            nameof(HasContent));

        OnPropertyChanged(
            nameof(CanUpload));
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private static string GetMediaType(
        string extension)
    {
        return extension switch
        {
            ".pdf" =>
                "application/pdf",

            ".png" =>
                "image/png",

            ".jpg" or ".jpeg" =>
                "image/jpeg",

            _ =>
                "application/octet-stream"
        };
    }

    private static string FormatMoney(
        decimal amount)
    {
        return amount.ToString(
            "C2");
    }

    private static string FormatCategory(
        string? category)
    {
        return category switch
        {
            "MobilePhone" =>
                "Mobile phone",

            "NaturalGas" =>
                "Natural gas",

            null or "" =>
                string.Empty,

            _ =>
                category
        };
    }

    private static string FormatConfidence(
        string? confidence)
    {
        return confidence switch
        {
            "Confirmed" =>
                "Confirmed",

            "StrongInference" =>
                "Strong evidence",

            "Possible" =>
                "Possible",

            _ =>
                "Unknown"
        };
    }

    private static string FormatPeriod(
        BillStatementHistoryResult statement)
    {
        return
            $"{statement.PeriodStart:MMM d} – {statement.PeriodEnd:MMM d, yyyy}";
    }

    private static string FormatDueDate(
        BillStatementHistoryResult statement)
    {
        if (!statement.DueDate.HasValue)
        {
            return
                "Due date unavailable";
        }

        return
            $"Due {statement.DueDate.Value:MMM d, yyyy}";
    }
}

public sealed record BillStatementDisplayItem(
    string PeriodText,
    string AmountText,
    string DueDateText);