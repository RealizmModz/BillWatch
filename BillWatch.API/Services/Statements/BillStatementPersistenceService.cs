using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Statements;

public sealed class BillStatementPersistenceService
{
    private readonly BillWatchDbContext
        _dbContext;

    private readonly BillStatementChangeDetectionService
        _changeDetectionService;

    public BillStatementPersistenceService(
        BillWatchDbContext dbContext,
        BillStatementChangeDetectionService changeDetectionService)
    {
        _dbContext =
            dbContext;

        _changeDetectionService =
            changeDetectionService;
    }

    public async Task<BillStatementPersistenceResult>
        PersistAsync(
            BillStatementUploadEntity upload,
            BillStatementStructuredData parsedStatement,
            IReadOnlyList<BillStatementStructuredLineItem> parsedLineItems,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            upload);

        ArgumentNullException.ThrowIfNull(
            parsedStatement);

        ArgumentNullException.ThrowIfNull(
            parsedLineItems);

        if (upload.UserId ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "The statement upload does not have an owner.");
        }

        if (upload.BillStreamId ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "The statement upload is not associated with a bill stream.");
        }

        if (!parsedStatement.IsReadyForPersistence ||
            !parsedStatement.TotalAmount.HasValue ||
            !parsedStatement.BillingPeriodStart.HasValue ||
            !parsedStatement.BillingPeriodEnd.HasValue)
        {
            throw new InvalidOperationException(
                "The parsed statement does not contain enough validated data to persist.");
        }

        var periodStart =
            parsedStatement
                .BillingPeriodStart
                .Value;

        var periodEnd =
            parsedStatement
                .BillingPeriodEnd
                .Value;

        var totalAmount =
            parsedStatement
                .TotalAmount
                .Value;

        var currencyCode =
            parsedStatement
                .CurrencyCode
                .Trim()
                .ToUpperInvariant();

        if (currencyCode.Length !=
            3)
        {
            throw new InvalidOperationException(
                "The parsed statement currency code is invalid.");
        }

        var existingStatement =
            await _dbContext.BillStatements
                .Where(
                    statement =>
                        statement.UserId ==
                            upload.UserId &&
                        statement.BillStreamId ==
                            upload.BillStreamId &&
                        statement.PeriodStart ==
                            periodStart &&
                        statement.PeriodEnd ==
                            periodEnd &&
                        statement.StatementDate ==
                            parsedStatement.StatementDate &&
                        statement.DueDate ==
                            parsedStatement.DueDate &&
                        statement.TotalAmount ==
                            totalAmount &&
                        statement.CurrencyCode ==
                            currencyCode)
                .OrderBy(
                    statement =>
                        statement.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        var now =
            DateTimeOffset.UtcNow;

        if (existingStatement is not null)
        {
            IReadOnlyList<BillLineItemEntity>
                pendingLineItems =
                    [];

            if (parsedLineItems.Count >
                0)
            {
                var existingLineItemCount =
                    await _dbContext.BillLineItems
                        .CountAsync(
                            lineItem =>
                                lineItem.UserId ==
                                    upload.UserId &&
                                lineItem.BillStatementId ==
                                    existingStatement.Id,
                            cancellationToken);

                if (existingLineItemCount ==
                    0)
                {
                    pendingLineItems =
                        CreateLineItems(
                            existingStatement,
                            parsedLineItems,
                            now);

                    _dbContext.BillLineItems.AddRange(
                        pendingLineItems);
                }

                await _changeDetectionService
                    .ReconcileAsync(
                        upload.UserId,
                        upload.BillStreamId,
                        pendingStatement:
                            null,
                        pendingLineItems:
                            pendingLineItems,
                        cancellationToken:
                            cancellationToken);
            }

            upload.BillStatementId =
                existingStatement.Id;

            upload.Status =
                BillStatementUploadStatus.Processed;

            upload.UpdatedAtUtc =
                now;

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return new BillStatementPersistenceResult(
                StatementId:
                    existingStatement.Id,

                WasCreated:
                    false);
        }

        var statement =
            new BillStatementEntity
            {
                UserId =
                    upload.UserId,

                BillStreamId =
                    upload.BillStreamId,

                PeriodStart =
                    periodStart,

                PeriodEnd =
                    periodEnd,

                StatementDate =
                    parsedStatement.StatementDate,

                DueDate =
                    parsedStatement.DueDate,

                TotalAmount =
                    totalAmount,

                CurrencyCode =
                    currencyCode,

                ProviderStatementId =
                    null,

                RetrievedAtUtc =
                    now,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        _dbContext.BillStatements.Add(
            statement);

        var lineItems =
            CreateLineItems(
                statement,
                parsedLineItems,
                now);

        if (lineItems.Count >
            0)
        {
            _dbContext.BillLineItems.AddRange(
                lineItems);
        }

        await _changeDetectionService
            .ReconcileAsync(
                upload.UserId,
                upload.BillStreamId,
                pendingStatement:
                    statement,
                pendingLineItems:
                    lineItems,
                cancellationToken:
                    cancellationToken);

        upload.BillStatementId =
            statement.Id;

        upload.Status =
            BillStatementUploadStatus.Processed;

        upload.UpdatedAtUtc =
            now;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new BillStatementPersistenceResult(
            StatementId:
                statement.Id,

            WasCreated:
                true);
    }

    private static IReadOnlyList<BillLineItemEntity>
        CreateLineItems(
            BillStatementEntity statement,
            IReadOnlyList<BillStatementStructuredLineItem> parsedLineItems,
            DateTimeOffset now)
    {
        if (parsedLineItems.Count ==
            0)
        {
            return [];
        }

        var results =
            new List<BillLineItemEntity>(
                parsedLineItems.Count);

        for (var index = 0;
             index < parsedLineItems.Count;
             index++)
        {
            var parsed =
                parsedLineItems[index];

            results.Add(
                new BillLineItemEntity
                {
                    UserId =
                        statement.UserId,

                    BillStatementId =
                        statement.Id,

                    BillStatement =
                        statement,

                    Description =
                        parsed.Description,

                    Amount =
                        parsed.Amount,

                    Category =
                        parsed.Category,

                    SortOrder =
                        index,

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                });
        }

        return results.AsReadOnly();
    }
}

public sealed record BillStatementPersistenceResult(
    Guid StatementId,
    bool WasCreated);