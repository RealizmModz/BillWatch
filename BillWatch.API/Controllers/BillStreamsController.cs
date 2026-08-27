using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bill-streams")]
[Authorize]
public sealed class BillStreamsController : ControllerBase
{
    private readonly BillWatchDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillStreamsController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BillStreamResult>>>
        GetBillStreams(
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query =
            _dbContext.BillStreams
                .AsNoTracking()
                .Where(stream =>
                    stream.UserId == userId);

        if (!includeInactive)
        {
            query =
                query.Where(stream =>
                    stream.IsActive);
        }

        var streams =
            await query
                .OrderBy(stream =>
                    stream.ProviderName)
                .ToListAsync(
                    cancellationToken);

        if (streams.Count == 0)
        {
            return Ok(
                Array.Empty<BillStreamResult>());
        }

        var streamIds =
            streams
                .Select(stream =>
                    stream.Id)
                .ToList();

        var transactions =
            await _dbContext.BankTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.UserId == userId &&
                    transaction.BillStreamId != null &&
                    streamIds.Contains(
                        transaction.BillStreamId.Value) &&
                    !transaction.IsRemoved &&
                    !transaction.IsPending)
                .OrderByDescending(transaction =>
                    transaction.PostedDate)
                .ThenByDescending(transaction =>
                    transaction.CreatedAtUtc)
                .ToListAsync(
                    cancellationToken);

        var results =
            streams
                .Select(stream =>
                {
                    var streamTransactions =
                        transactions
                            .Where(transaction =>
                                transaction.BillStreamId ==
                                stream.Id)
                            .OrderByDescending(transaction =>
                                transaction.PostedDate)
                            .ThenByDescending(transaction =>
                                transaction.CreatedAtUtc)
                            .ToList();

                    var currentAmount =
                        streamTransactions.Count == 0
                            ? 0m
                            : streamTransactions[0].Amount;

                    var previousAverage =
                        streamTransactions.Count <= 1
                            ? 0m
                            : streamTransactions
                                .Skip(1)
                                .Average(transaction =>
                                    transaction.Amount);

                    return new BillStreamResult(
                        Id:
                            stream.Id,

                        ProviderName:
                            stream.ProviderName,

                        Category:
                            stream.Category.ToString(),

                        IsActive:
                            stream.IsActive,

                        CurrentAmount:
                            currentAmount,

                        PreviousAverage:
                            previousAverage);
                })
                .ToList();

        return Ok(results);
    }

    [HttpGet("{billStreamId:guid}")]
    public async Task<ActionResult<BillStreamDetailResult>>
        GetBillStreamDetail(
            Guid billStreamId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (billStreamId == Guid.Empty)
        {
            return NotFound();
        }

        var stream =
            await _dbContext.BillStreams
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.Id == billStreamId &&
                        candidate.UserId == userId,
                    cancellationToken);

        if (stream is null)
        {
            return NotFound();
        }

        var transactions =
            await _dbContext.BankTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.UserId == userId &&
                    transaction.BillStreamId ==
                        billStreamId &&
                    !transaction.IsRemoved &&
                    !transaction.IsPending)
                .OrderByDescending(transaction =>
                    transaction.PostedDate)
                .ThenByDescending(transaction =>
                    transaction.CreatedAtUtc)
                .ToListAsync(
                    cancellationToken);

        var currentAmount =
            transactions.Count == 0
                ? 0m
                : transactions[0].Amount;

        var previousAverage =
            transactions.Count <= 1
                ? 0m
                : transactions
                    .Skip(1)
                    .Average(transaction =>
                        transaction.Amount);

        var statements =
            await _dbContext.BillStatements
                .AsNoTracking()
                .Where(statement =>
                    statement.UserId == userId &&
                    statement.BillStreamId ==
                        billStreamId)
                .OrderByDescending(statement =>
                    statement.PeriodEnd)
                .ThenByDescending(statement =>
                    statement.StatementDate)
                .Select(statement =>
                    new BillStatementHistoryResult(
                        statement.Id,
                        statement.PeriodStart,
                        statement.PeriodEnd,
                        statement.StatementDate,
                        statement.DueDate,
                        statement.TotalAmount,
                        statement.CurrencyCode))
                .ToListAsync(
                    cancellationToken);

        var changes =
            await _dbContext.BillChanges
                .AsNoTracking()
                .Where(change =>
                    change.UserId == userId &&
                    change.BillStreamId ==
                        billStreamId)
                .OrderByDescending(change =>
                    change.DetectedAtUtc)
                .Select(change =>
                    new BillChangeResult(
                        change.Id,
                        change.PreviousStatementId,
                        change.CurrentStatementId,
                        change.ChangeType.ToString(),
                        change.Confidence.ToString(),
                        change.Description,
                        change.PreviousAmount,
                        change.CurrentAmount,
                        change.AmountDifference,
                        change.AnnualizedImpact,
                        change.IsAcknowledged,
                        change.DetectedAtUtc))
                .ToListAsync(
                    cancellationToken);

        return Ok(
            new BillStreamDetailResult(
                Id:
                    stream.Id,

                ProviderName:
                    stream.ProviderName,

                Category:
                    stream.Category.ToString(),

                IsActive:
                    stream.IsActive,

                CurrentAmount:
                    currentAmount,

                PreviousAverage:
                    previousAverage,

                Statements:
                    statements,

                Changes:
                    changes));
    }

    [HttpPost]
    public async Task<ActionResult<BillStreamResult>>
        CreateBillStream(
            CreateBillStreamRequest request,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var providerName =
            request.ProviderName?.Trim();

        if (string.IsNullOrWhiteSpace(providerName))
        {
            return BadRequest(
                new
                {
                    message =
                        "Provider name is required."
                });
        }

        if (providerName.Length > 200)
        {
            return BadRequest(
                new
                {
                    message =
                        "Provider name is too long."
                });
        }

        if (!Enum.TryParse<BillCategory>(
                request.Category,
                ignoreCase: true,
                out var category))
        {
            return BadRequest(
                new
                {
                    message =
                        "Bill category is invalid."
                });
        }

        var normalizedProviderName =
            providerName.ToLower();

        var existingStream =
            await _dbContext.BillStreams
                .FirstOrDefaultAsync(
                    stream =>
                        stream.UserId == userId &&
                        stream.ProviderName.ToLower() ==
                        normalizedProviderName,
                    cancellationToken);

        if (existingStream is not null)
        {
            var changed =
                false;

            if (!existingStream.IsActive)
            {
                existingStream.IsActive =
                    true;

                changed =
                    true;
            }

            if (existingStream.Category ==
                    BillCategory.Unknown &&
                category != BillCategory.Unknown)
            {
                existingStream.Category =
                    category;

                changed =
                    true;
            }

            if (existingStream.Source ==
                BillStreamSource.Unknown)
            {
                existingStream.Source =
                    BillStreamSource.Manual;

                changed =
                    true;
            }

            if (changed)
            {
                existingStream.UpdatedAtUtc =
                    DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(
                    cancellationToken);
            }

            return Ok(
                new BillStreamResult(
                    Id:
                        existingStream.Id,

                    ProviderName:
                        existingStream.ProviderName,

                    Category:
                        existingStream.Category.ToString(),

                    IsActive:
                        existingStream.IsActive,

                    CurrentAmount:
                        0m,

                    PreviousAverage:
                        0m));
        }

        var now =
            DateTimeOffset.UtcNow;

        var stream =
            new BillStreamEntity
            {
                UserId =
                    userId,

                ProviderName =
                    providerName,

                Category =
                    category,

                Source =
                    BillStreamSource.Manual,

                IsActive =
                    true,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        _dbContext.BillStreams.Add(
            stream);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Ok(
            new BillStreamResult(
                Id:
                    stream.Id,

                ProviderName:
                    stream.ProviderName,

                Category:
                    stream.Category.ToString(),

                IsActive:
                    stream.IsActive,

                CurrentAmount:
                    0m,

                PreviousAverage:
                    0m));
    }

    private bool TryGetUserId(
        out Guid userId)
    {
        var userIdText =
            _userManager.GetUserId(User);

        return Guid.TryParse(
            userIdText,
            out userId);
    }
}

public sealed record CreateBillStreamRequest(
    string ProviderName,
    string Category);

public sealed record BillStreamResult(
    Guid Id,
    string ProviderName,
    string Category,
    bool IsActive,
    decimal CurrentAmount,
    decimal PreviousAverage);

public sealed record BillStreamDetailResult(
    Guid Id,
    string ProviderName,
    string Category,
    bool IsActive,
    decimal CurrentAmount,
    decimal PreviousAverage,
    IReadOnlyList<BillStatementHistoryResult> Statements,
    IReadOnlyList<BillChangeResult> Changes);

public sealed record BillStatementHistoryResult(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    decimal TotalAmount,
    string CurrencyCode);

public sealed record BillChangeResult(
    Guid Id,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    string ChangeType,
    string Confidence,
    string Description,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    bool IsAcknowledged,
    DateTimeOffset DetectedAtUtc);
