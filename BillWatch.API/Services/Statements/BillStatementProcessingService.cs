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

                FullMode =
                    BoundedChannelFullMode.DropWrite,

                AllowSynchronousContinuations =
                    false
            });

    public void Notify()
    {
        _channel.Writer.TryWrite(
            true);
    }

    public async ValueTask WaitAsync(
        CancellationToken cancellationToken)
    {
        await _channel.Reader.ReadAsync(
            cancellationToken);
    }
}

public sealed class BillStatementProcessingService
{
    private static readonly DeterministicBillLineItemParser
        LineItemParser =
            new();

    private readonly BillWatchDbContext
        _dbContext;

    private readonly BillStatementDocumentTextReader
        _documentTextReader;

    private readonly DeterministicBillStatementParser
        _statementParser;

    private readonly BillStatementValidationService
        _validationService;

    private readonly BillStatementPersistenceService
        _persistenceService;

    private readonly ILogger<BillStatementProcessingService>
        _logger;

    public BillStatementProcessingService(
        BillWatchDbContext dbContext,
        BillStatementDocumentTextReader documentTextReader,
        DeterministicBillStatementParser statementParser,
        BillStatementPersistenceService persistenceService,
        ILogger<BillStatementProcessingService> logger)
        : this(
            dbContext,
            documentTextReader,
            statementParser,
            new BillStatementValidationService(),
            persistenceService,
            logger)
    {
    }

    public BillStatementProcessingService(
        BillWatchDbContext dbContext,
        BillStatementDocumentTextReader documentTextReader,
        DeterministicBillStatementParser statementParser,
        BillStatementValidationService validationService,
        BillStatementPersistenceService persistenceService,
        ILogger<BillStatementProcessingService> logger)
    {
        _dbContext =
            dbContext;

        _documentTextReader =
            documentTextReader;

        _statementParser =
            statementParser;

        _validationService =
            validationService;

        _persistenceService =
            persistenceService;

        _logger =
            logger;
    }

    public async Task<int> ProcessPendingBatchAsync(
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        if (maxItems <=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                "Batch size must be greater than zero.");
        }

        var candidates =
            await _dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(
                    upload =>
                        upload.Status ==
                            BillStatementUploadStatus.Uploaded ||
                        upload.Status ==
                            BillStatementUploadStatus.Processing)
                .OrderBy(
                    upload =>
                        upload.CreatedAtUtc)
                .ThenBy(
                    upload =>
                        upload.Id)
                .Select(
                    upload =>
                        new PendingStatementUpload(
                            upload.Id,
                            upload.UserId))
                .Take(
                    maxItems)
                .ToListAsync(
                    cancellationToken);

        foreach (var candidate in
                 candidates)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

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
        var upload =
            await _dbContext.BillStatementUploads
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                            uploadId &&
                        item.UserId ==
                            userId,
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

        if (upload.Status ==
            BillStatementUploadStatus.Uploaded)
        {
            upload.Status =
                BillStatementUploadStatus.Processing;

            upload.UpdatedAtUtc =
                DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }

        try
        {
            var extractionResult =
                _documentTextReader.Read(
                    upload.UserId,
                    upload.StorageKey,
                    upload.MediaType,
                    upload.FileExtension);

            if (extractionResult.RequiresOcr)
            {
                upload.Status =
                    BillStatementUploadStatus.NeedsOcr;

                upload.UpdatedAtUtc =
                    DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                return;
            }

            var parsedStatement =
                _statementParser.Parse(
                    extractionResult.Text);

            if (!parsedStatement.IsReadyForPersistence)
            {
                upload.Status =
                    BillStatementUploadStatus.ReadyForParsing;

                upload.UpdatedAtUtc =
                    DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                return;
            }

            var validationResult =
                _validationService.Validate(
                    new BillStatementValidationInput(
                        TotalAmount:
                            parsedStatement.TotalAmount,

                        PeriodStart:
                            parsedStatement.BillingPeriodStart,

                        PeriodEnd:
                            parsedStatement.BillingPeriodEnd,

                        StatementDate:
                            parsedStatement.StatementDate,

                        DueDate:
                            parsedStatement.DueDate,

                        CurrencyCode:
                            parsedStatement.CurrencyCode),
                    DateOnly.FromDateTime(
                        DateTime.UtcNow));

            if (!validationResult.IsValid ||
                validationResult.RequiresReview)
            {
                upload.Status =
                    BillStatementUploadStatus.ReadyForParsing;

                upload.UpdatedAtUtc =
                    DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                return;
            }

            var parsedLineItems =
                LineItemParser.Parse(
                    extractionResult.Text);

            await _persistenceService.PersistAsync(
                upload,
                parsedStatement,
                parsedLineItems,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex is
                    BillStatementTextExtractionException or
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
        {
            upload.Status =
                BillStatementUploadStatus.Failed;

            upload.UpdatedAtUtc =
                DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            _logger.LogWarning(
                "Bill statement upload {UploadId} failed during deterministic document processing with {ExceptionType}.",
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
    private const int ProcessingBatchSize =
        4;

    private static readonly TimeSpan ErrorRetryDelay =
        TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory
        _scopeFactory;

    private readonly BillStatementProcessingSignal
        _signal;

    private readonly ILogger<BillStatementProcessingBackgroundService>
        _logger;

    public BillStatementProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        BillStatementProcessingSignal signal,
        ILogger<BillStatementProcessingBackgroundService> logger)
    {
        _scopeFactory =
            scopeFactory;

        _signal =
            signal;

        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken
               .IsCancellationRequested)
        {
            try
            {
                var processedCount =
                    await ProcessBatchAsync(
                        stoppingToken);

                if (processedCount >
                    0)
                {
                    continue;
                }

                await _signal.WaitAsync(
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
        using var scope =
            _scopeFactory.CreateScope();

        var processingService =
            scope.ServiceProvider
                .GetRequiredService<
                    BillStatementProcessingService>();

        return await processingService
            .ProcessPendingBatchAsync(
                ProcessingBatchSize,
                cancellationToken);
    }
}