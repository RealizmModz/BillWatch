using BillWatch.Services;

namespace BillWatch;

public partial class BillDetailPage : ContentPage
{
    private const long MaxUploadSizeBytes =
        15L * 1024 * 1024;

    private static readonly TimeSpan[]
        UploadStatusPollingDelays =
        [
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(800),
            TimeSpan.FromMilliseconds(1200),
            TimeSpan.FromMilliseconds(1800),
            TimeSpan.FromMilliseconds(2500),
            TimeSpan.FromMilliseconds(3000)
        ];

    private readonly BillStreamService
        _billStreamService;

    private CancellationTokenSource
        _pageCancellationTokenSource =
            new();

    private Guid
        _billStreamId;

    private bool
        _hasLoaded;

    private bool
        _isLoading;

    private bool
        _isUploading;

    private bool
        _isProcessingUpload;

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

            OnPropertyChanged(
                nameof(CanUpload));
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

            NotifyUploadActivityChanged();
        }
    }

    public bool IsProcessingUpload
    {
        get =>
            _isProcessingUpload;

        private set
        {
            if (_isProcessingUpload ==
                value)
            {
                return;
            }

            _isProcessingUpload =
                value;

            OnPropertyChanged();

            NotifyUploadActivityChanged();
        }
    }

    public bool HasActiveUploadWork =>
        IsUploading ||
        IsProcessingUpload;

    public string UploadActivityText
    {
        get
        {
            if (IsUploading)
            {
                return
                    "Uploading statement securely…";
            }

            if (IsProcessingUpload)
            {
                return
                    "BillWatch is reading and validating your statement…";
            }

            return
                string.Empty;
        }
    }

    public bool CanUpload =>
        !HasActiveUploadWork &&
        !IsLoading &&
        _billStreamId !=
            Guid.Empty;

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
        _detail is not
            null;

    public string ProviderName =>
        _detail?.ProviderName
        ?? "Bill details";

    public string Category =>
        FormatCategory(
            _detail?.Category);

    public string MonitoringStatusText =>
        _detail?.IsActive ==
        true
            ? "MONITORING"
            : "INACTIVE";

    public string CurrentAmountText =>
        _detail is
        null
            ? "$0.00"
            : FormatMoney(
                _detail.CurrentAmount);

    public string PreviousAverageText
    {
        get
        {
            if (_detail is
                    null ||
                _detail.PreviousAverage <=
                    0m)
            {
                return
                    "Building historical average";
            }

            return
                $"Previous average {FormatMoney(_detail.PreviousAverage)}";
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
        LatestChange is not
        null;

    public bool HasNoDetectedChange =>
        !HasDetectedChange;

    public string ChangeHeadlineText
    {
        get
        {
            var change =
                LatestChange;

            if (change is
                null)
            {
                return
                    string.Empty;
            }

            if (change.AmountDifference >
                0m)
            {
                return
                    "Your bill increased";
            }

            if (change.AmountDifference <
                0m)
            {
                return
                    "Your bill decreased";
            }

            return
                "Your bill stayed the same";
        }
    }

    public string ChangeAmountPathText
    {
        get
        {
            var change =
                LatestChange;

            if (change is
                null)
            {
                return
                    string.Empty;
            }

            return
                $"{FormatMoney(change.PreviousAmount)} → {FormatMoney(change.CurrentAmount)}";
        }
    }

    public string ChangeSummaryText
    {
        get
        {
            var change =
                LatestChange;

            if (change is
                null)
            {
                return
                    string.Empty;
            }

            var difference =
                change.AmountDifference;

            if (difference >
                0m)
            {
                return
                    $"+{FormatMoney(difference)}/mo";
            }

            if (difference <
                0m)
            {
                return
                    $"-{FormatMoney(Math.Abs(difference))}/mo";
            }

            return
                "$0/month";
        }
    }

    public string AnnualImpactText
    {
        get
        {
            var change =
                LatestChange;

            if (change is
                null)
            {
                return
                    string.Empty;
            }

            var impact =
                change.AnnualizedImpact;

            if (impact >
                0m)
            {
                return
                    $"+{FormatMoney(impact)}/yr";
            }

            if (impact <
                0m)
            {
                return
                    $"-{FormatMoney(Math.Abs(impact))}/yr";
            }

            return
                "$0/year";
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
                    ? "BillWatch confirmed the amount changed, but there is not enough provider evidence yet to explain why."
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

    public string StatementHistorySummaryText
    {
        get
        {
            return Statements.Count switch
            {
                0 =>
                    "No provider statement history yet.",

                1 =>
                    "1 provider statement processed.",

                _ =>
                    $"{Statements.Count} provider statements processed."
            };
        }
    }

    public string LatestStatementSummaryText
    {
        get
        {
            var latest =
                _detail?
                    .Statements
                    .OrderByDescending(
                        statement =>
                            statement.PeriodEnd)
                    .FirstOrDefault();

            if (latest is
                null)
            {
                return
                    "No provider statement has been processed yet.";
            }

            if (latest.DueDate.HasValue)
            {
                return
                    $"Latest provider statement: {latest.PeriodEnd:MMM d, yyyy} · Due date shown: {latest.DueDate.Value:MMM d, yyyy}";
            }

            return
                $"Latest provider statement: {latest.PeriodEnd:MMM d, yyyy} · No explicit due date found";
        }
    }

    protected override async void
        OnAppearing()
    {
        base.OnAppearing();

        EnsurePageCancellationToken();

        if (_hasLoaded)
        {
            return;
        }

        await LoadAsync(
            _pageCancellationTokenSource.Token);
    }

    protected override void
        OnDisappearing()
    {
        _pageCancellationTokenSource.Cancel();

        base.OnDisappearing();
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

                                StatementDateText:
                                    FormatStatementDate(
                                        statement),

                                DueDateText:
                                    FormatDueDate(
                                        statement)))
                    .ToList()
                    .AsReadOnly();

            _hasLoaded =
                true;

            NotifyDetailChanged();
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
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
        if (HasActiveUploadWork ||
            _billStreamId ==
                Guid.Empty)
        {
            return;
        }

        try
        {
            ClearUploadMessage();

            var selectedFile =
                await FilePicker.Default
                    .PickAsync(
                        new PickOptions
                        {
                            PickerTitle =
                                "Choose bill statement"
                        });

            if (selectedFile is
                null)
            {
                return;
            }

            EnsurePageCancellationToken();

            var cancellationToken =
                _pageCancellationTokenSource
                    .Token;

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
                        mediaType,
                        cancellationToken);

            IsUploading =
                false;

            IsProcessingUpload =
                true;

            await TrackUploadProcessingAsync(
                result,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (_pageCancellationTokenSource
                .IsCancellationRequested)
        {
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

            IsProcessingUpload =
                false;
        }
    }

    private async Task TrackUploadProcessingAsync(
        BillStatementUploadResult upload,
        CancellationToken cancellationToken)
    {
        if (TryApplyTerminalUploadStatus(
                upload.Status))
        {
            if (IsProcessedStatus(
                    upload.Status))
            {
                await RefreshDetailAfterProcessingAsync(
                    cancellationToken);
            }

            return;
        }

        try
        {
            foreach (var delay in
                     UploadStatusPollingDelays)
            {
                await Task.Delay(
                    delay,
                    cancellationToken);

                var status =
                    await _billStreamService
                        .GetStatementUploadStatusAsync(
                            upload.BillStreamId,
                            upload.Id,
                            cancellationToken);

                if (!TryApplyTerminalUploadStatus(
                        status.Status))
                {
                    continue;
                }

                if (IsProcessedStatus(
                        status.Status))
                {
                    await RefreshDetailAfterProcessingAsync(
                        cancellationToken);
                }

                return;
            }

            SetUploadSuccess(
                "Statement uploaded securely. BillWatch is continuing to process it in the background.");
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            throw;
        }
        catch (SessionExpiredException)
        {
            SetUploadSuccess(
                "Statement uploaded securely. Sign in again later to see its processing status.");
        }
        catch (HttpRequestException)
        {
            SetUploadSuccess(
                "Statement uploaded securely. BillWatch will continue processing it in the background.");
        }
    }

    private async Task RefreshDetailAfterProcessingAsync(
        CancellationToken cancellationToken)
    {
        _hasLoaded =
            false;

        await LoadAsync(
            cancellationToken);
    }

    private bool TryApplyTerminalUploadStatus(
        string? status)
    {
        switch (status)
        {
            case "ReadyForParsing":
                SetUploadSuccess(
                    "BillWatch read the statement, but it needs review before the extracted values can become trusted bill history.");

                return true;

            case "NeedsOcr":
                SetUploadError(
                    "BillWatch could not confidently read this copy. Try a clearer PDF, screenshot, or photo of the statement.");

                return true;

            case "Processed":
                SetUploadSuccess(
                    "Statement processed. BillWatch updated this bill's evidence and history.");

                return true;

            case "Failed":
                SetUploadError(
                    "The statement was uploaded, but BillWatch could not safely process it. Try a clearer copy or a different file.");

                return true;

            default:
                return false;
        }
    }

    private static bool IsProcessedStatus(
        string? status)
    {
        return string.Equals(
            status,
            "Processed",
            StringComparison.Ordinal);
    }

    private void EnsurePageCancellationToken()
    {
        if (!_pageCancellationTokenSource
            .IsCancellationRequested)
        {
            return;
        }

        _pageCancellationTokenSource.Dispose();

        _pageCancellationTokenSource =
            new CancellationTokenSource();
    }

    private void NotifyUploadActivityChanged()
    {
        OnPropertyChanged(
            nameof(HasActiveUploadWork));

        OnPropertyChanged(
            nameof(UploadActivityText));

        OnPropertyChanged(
            nameof(CanUpload));
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

    private void SetUploadSuccess(
        string message)
    {
        _uploadSucceeded =
            true;

        UploadMessage =
            message;

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
            nameof(MonitoringStatusText));

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
            nameof(ChangeHeadlineText));

        OnPropertyChanged(
            nameof(ChangeAmountPathText));

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
            nameof(StatementHistorySummaryText));

        OnPropertyChanged(
            nameof(LatestStatementSummaryText));

        OnPropertyChanged(
            nameof(HasContent));

        OnPropertyChanged(
            nameof(CanUpload));
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs e)
    {
        _pageCancellationTokenSource
            .Cancel();

        await Navigation
            .PopModalAsync();
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
                "Confirmed from provider evidence",

            "StrongInference" =>
                "Strong evidence",

            "Possible" =>
                "Possible explanation",

            _ =>
                "Cause not confirmed"
        };
    }

    private static string FormatPeriod(
        BillStatementHistoryResult statement)
    {
        return
            $"{statement.PeriodStart:MMM d} – {statement.PeriodEnd:MMM d, yyyy}";
    }

    private static string FormatStatementDate(
        BillStatementHistoryResult statement)
    {
        if (!statement.StatementDate.HasValue)
        {
            return
                "Statement date unavailable";
        }

        return
            $"Statement dated {statement.StatementDate.Value:MMM d, yyyy}";
    }

    private static string FormatDueDate(
        BillStatementHistoryResult statement)
    {
        if (!statement.DueDate.HasValue)
        {
            return
                "No explicit due date found";
        }

        return
            $"Due date shown: {statement.DueDate.Value:MMM d, yyyy}";
    }
}

public sealed record BillStatementDisplayItem(
    string PeriodText,
    string AmountText,
    string StatementDateText,
    string DueDateText);