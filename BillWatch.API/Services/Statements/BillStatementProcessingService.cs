using System.Threading.Channels;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Statements;

public sealed class BillStatementProcessingSignal
{
    private readonly Channel<bool> _channel =
        Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite,
                AllowSynchronousContinuations = false
            });

    public void Notify()
    {
        _channel.Writer.TryWrite(true);
    }

    public async ValueTask WaitAsync(
        CancellationToken cancellationToken)
    {
        await _channel.Reader.ReadAsync(cancellationToken);
    }
}

public sealed class BillStatementProcessingService
{
    private readonly BillWatchDbContext _dbContext;
    private readonly BillStatementDocumentTextReader _documentTextReader;
    private readonly IBillStatementExtractionService _extractionService;
    private readonly BillStatementValidationService _validationService;
    private readonly BillStatementPersistenceService _persistenceService;
    private readonly ILogger<BillStatementProcessingService> _logger;

    public BillStatementProcessingService(
        BillWatchDbContext dbContext,
        BillStatementDocumentTextReader documentTextReader,
        IBillStatementExtractionService extractionService,
        BillStatementValidationService validationService,
        BillStatementPersistenceService persistenceService,
        ILogger<BillStatementProcessingService> logger)
    {
        _dbContext = dbContext;
        _documentTextReader = documentTextReader;
        _extractionService = extractionService;
        _validationService = validationService;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public async Task<int> ProcessPendingBatchAsync(
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                "Batch size must be greater than zero.");
        }

        var candidates = await _dbContext.BillStatementUploads
            .AsNoTracking()
            .Where(upload =>
                upload.Status == BillStatementUploadStatus.Uploaded ||
                upload.Status == BillStatementUploadStatus.Processing)
            .OrderBy(upload => upload.CreatedAtUtc)
            .ThenBy(upload => upload.Id)
            .Select(upload =>
                new PendingStatementUpload(
                    upload.Id,
                    upload.UserId))
            .Take(maxItems)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ProcessUploadAsync(
                candidate.UploadId,
                candidate.UserId,
                cancellationToken);
        }

        return candidates.Count;
    }

    private async Task ProcessUploadAsync(
        Guid uploadId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var upload = await _dbContext.BillStatementUploads
            .SingleOrDefaultAsync(
                item =>
                    item.Id == uploadId &&
                    item.UserId == userId,
                cancellationToken);

        if (upload is null)
        {
            return;
        }

        if (upload.Status is not
            BillStatementUploadStatus.Uploaded and not
            BillStatementUploadStatus.Processing)
        {
            return;
        }

        if (upload.Status == BillStatementUploadStatus.Uploaded)
        {
            upload.Status = BillStatementUploadStatus.Processing;
            upload.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            /*
             * Read/extract the document only once. The transient text is
             * passed into the structured extraction boundary and is not
             * persisted as raw text.
             */
            var extractionResult = _documentTextReader.Read(
                upload.UserId,
                upload.StorageKey,
                upload.MediaType,
                upload.FileExtension);

            if (extractionResult.RequiresOcr)
            {
                upload.Status = BillStatementUploadStatus.NeedsOcr;
                upload.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            /*
             * Resolve Bill Stream context using BOTH resource ID and owner
             * ID. The context is a hint, not proof of statement facts.
             */
            var billStreamContext = await _dbContext.BillStreams
                .AsNoTracking()
                .Where(stream =>
                    stream.Id == upload.BillStreamId &&
                    stream.UserId == upload.UserId)
                .Select(stream =>
                    new
                    {
                        stream.ProviderName,
                        stream.Category
                    })
                .SingleOrDefaultAsync(cancellationToken);

            if (billStreamContext is null)
            {
                throw new InvalidOperationException(
                    "The statement upload is not associated with an owned Bill Stream.");
            }

            var structuredExtraction = await _extractionService.ExtractAsync(
                new BillStatementExtractionRequest(
                    DocumentText: extractionResult.Text,
                    Hints: new BillStatementExtractionHints(
                        ExpectedProviderName: billStreamContext.ProviderName,
                        ExpectedCategory: billStreamContext.Category.ToString())),
                cancellationToken);

            var parsedStatement = structuredExtraction.Statement;

            if (!structuredExtraction.IsReadyForValidation)
            {
                upload.Status = BillStatementUploadStatus.ReadyForParsing;
                upload.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var validationResult = _validationService.Validate(
                new BillStatementValidationInput(
                    TotalAmount: parsedStatement.TotalAmount,
                    PeriodStart: parsedStatement.BillingPeriodStart,
                    PeriodEnd: parsedStatement.BillingPeriodEnd,
                    StatementDate: parsedStatement.StatementDate,
                    DueDate: parsedStatement.DueDate,
                    CurrencyCode: parsedStatement.CurrencyCode),
                DateOnly.FromDateTime(DateTime.UtcNow));

            if (!validationResult.IsValid ||
                validationResult.RequiresReview)
            {
                upload.Status = BillStatementUploadStatus.ReadyForParsing;
                upload.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            await _persistenceService.PersistAsync(
                upload,
                parsedStatement,
                structuredExtraction.LineItems,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (ex is
                BillStatementTextExtractionException or
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            upload.Status = BillStatementUploadStatus.Failed;
            upload.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Bill statement upload {UploadId} failed during document evidence processing with {ExceptionType}.",
                upload.Id,
                ex.GetType().Name);
        }
    }

    private sealed record PendingStatementUpload(
        Guid UploadId,
        Guid UserId);
}

public sealed class BillStatementProcessingBackgroundService
    : BackgroundService
{
    private const int ProcessingBatchSize = 4;

    private static readonly TimeSpan ErrorRetryDelay =
        TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaintenanceInterval =
        TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BillStatementProcessingSignal _signal;
    private readonly ILogger<BillStatementProcessingBackgroundService> _logger;

    public BillStatementProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        BillStatementProcessingSignal signal,
        ILogger<BillStatementProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAccountDeletionQuarantineAsync(
                    stoppingToken);

                var processedCount = await ProcessBatchAsync(
                    stoppingToken);

                if (processedCount > 0)
                {
                    continue;
                }

                await WaitForSignalOrMaintenanceAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "The statement processing worker encountered {ExceptionType}.",
                    ex.GetType().Name);

                try
                {
                    await Task.Delay(
                        ErrorRetryDelay,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task<int> ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var processingService =
            scope.ServiceProvider.GetRequiredService<BillStatementProcessingService>();

        return await processingService.ProcessPendingBatchAsync(
            ProcessingBatchSize,
            cancellationToken);
    }

    private async Task ReconcileAccountDeletionQuarantineAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<BillWatchDbContext>();

        var statementStorage =
            scope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

        var recoveryLogger =
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<AccountDeletionStatementQuarantineRecovery>();

        var recovery = new AccountDeletionStatementQuarantineRecovery(
            dbContext,
            statementStorage,
            recoveryLogger);

        await recovery.ReconcileAsync(cancellationToken);
    }

    private async Task WaitForSignalOrMaintenanceAsync(
        CancellationToken cancellationToken)
    {
        using var maintenanceCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var signalTask =
            _signal.WaitAsync(
                    maintenanceCancellation.Token)
                .AsTask();

        var maintenanceTask =
            Task.Delay(
                MaintenanceInterval,
                maintenanceCancellation.Token);

        var completed = await Task.WhenAny(
            signalTask,
            maintenanceTask);

        maintenanceCancellation.Cancel();

        try
        {
            await completed;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            // The losing wait was canceled after the other one completed.
        }
    }
}
