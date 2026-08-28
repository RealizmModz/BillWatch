using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Models;
using BillWatch.Core.Services;
using Microsoft.EntityFrameworkCore;
using EntityBillAlertType =
    BillWatch.API.Data.Entities.BillAlertType;

namespace BillWatch.API.Services.Statements;

public sealed class BillStatementChangeDetectionService
{
    private const int MaxDescriptionLength =
        950;

    private const int MaxAlertTitleLength =
        300;

    private const int MaxAlertMessageLength =
        2000;

    private readonly BillWatchDbContext
        _dbContext;

    private readonly BillAnalysisService
        _analysisService =
            new();

    private readonly BillStatementEvidenceAlertService
        _evidenceAlertService;

    public BillStatementChangeDetectionService(
        BillWatchDbContext dbContext)
    {
        _dbContext =
            dbContext;

        _evidenceAlertService =
            new BillStatementEvidenceAlertService(
                dbContext);
    }

    public async Task<BillStatementChangeReconciliationResult>
        ReconcileAsync(
            Guid userId,
            Guid billStreamId,
            BillStatementEntity? pendingStatement = null,
            IReadOnlyList<BillLineItemEntity>? pendingLineItems = null,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (billStreamId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        if (pendingStatement is not null &&
            (
                pendingStatement.UserId !=
                    userId ||
                pendingStatement.BillStreamId !=
                    billStreamId
            ))
        {
            throw new InvalidOperationException(
                "The pending statement does not belong to the requested bill stream.");
        }

        var providerName =
            await _dbContext.BillStreams
                .AsNoTracking()
                .Where(
                    stream =>
                        stream.Id ==
                            billStreamId &&
                        stream.UserId ==
                            userId)
                .Select(
                    stream =>
                        stream.ProviderName)
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                providerName))
        {
            throw new InvalidOperationException(
                "The owned bill stream could not be found.");
        }

        var statements =
            await _dbContext.BillStatements
                .Where(
                    statement =>
                        statement.UserId ==
                            userId &&
                        statement.BillStreamId ==
                            billStreamId)
                .ToListAsync(
                    cancellationToken);

        if (pendingStatement is not null &&
            statements.All(
                statement =>
                    statement.Id !=
                    pendingStatement.Id))
        {
            statements.Add(
                pendingStatement);
        }

        var canonicalStatements =
            statements
                .GroupBy(
                    statement =>
                        new
                        {
                            statement.PeriodStart,
                            statement.PeriodEnd
                        })
                .Select(
                    group =>
                        group
                            .OrderByDescending(
                                statement =>
                                    statement.RetrievedAtUtc)
                            .ThenByDescending(
                                statement =>
                                    statement.CreatedAtUtc)
                            .ThenByDescending(
                                statement =>
                                    statement.Id)
                            .First())
                .OrderBy(
                    statement =>
                        statement.PeriodStart)
                .ThenBy(
                    statement =>
                        statement.PeriodEnd)
                .ThenBy(
                    statement =>
                        statement.Id)
                .ToList();

        var statementIds =
            canonicalStatements
                .Select(
                    statement =>
                        statement.Id)
                .ToArray();

        List<BillLineItemEntity> lineItems;

        if (statementIds.Length ==
            0)
        {
            lineItems =
                [];
        }
        else
        {
            lineItems =
                await _dbContext.BillLineItems
                    .AsNoTracking()
                    .Where(
                        lineItem =>
                            lineItem.UserId ==
                                userId &&
                            statementIds.Contains(
                                lineItem.BillStatementId))
                    .OrderBy(
                        lineItem =>
                            lineItem.SortOrder)
                    .ToListAsync(
                        cancellationToken);
        }

        if (pendingLineItems is not null)
        {
            foreach (var pendingLineItem in
                     pendingLineItems)
            {
                if (pendingLineItem.UserId !=
                        userId ||
                    !statementIds.Contains(
                        pendingLineItem.BillStatementId))
                {
                    throw new InvalidOperationException(
                        "A pending line item does not belong to the requested bill history.");
                }

                if (lineItems.All(
                        item =>
                            item.Id !=
                            pendingLineItem.Id))
                {
                    lineItems.Add(
                        pendingLineItem);
                }
            }
        }

        var lineItemsByStatement =
            lineItems
                .GroupBy(
                    lineItem =>
                        lineItem.BillStatementId)
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        (IReadOnlyList<BillLineItemEntity>)
                        group
                            .OrderBy(
                                lineItem =>
                                    lineItem.SortOrder)
                            .ToList()
                            .AsReadOnly());

        var existingChanges =
            await _dbContext.BillChanges
                .Where(
                    change =>
                        change.UserId ==
                            userId &&
                        change.BillStreamId ==
                            billStreamId &&
                        (
                            change.ChangeType ==
                                BillChangeType.TotalIncrease ||
                            change.ChangeType ==
                                BillChangeType.TotalDecrease
                        ))
                .ToListAsync(
                    cancellationToken);

        var existingAlerts =
            await _dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            billStreamId &&
                        alert.BillChangeId.HasValue)
                .ToListAsync(
                    cancellationToken);

        var alertsByChangeId =
            existingAlerts
                .GroupBy(
                    alert =>
                        alert.BillChangeId!.Value)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.ToList());

        var desiredChanges =
            BuildDesiredChanges(
                providerName,
                canonicalStatements,
                lineItemsByStatement);

        var existingByPair =
            new Dictionary<
                StatementPair,
                BillChangeEntity>();

        var duplicateExistingChanges =
            new List<BillChangeEntity>();

        foreach (var existingChange in
                 existingChanges)
        {
            if (!existingChange
                    .PreviousStatementId
                    .HasValue)
            {
                duplicateExistingChanges.Add(
                    existingChange);

                continue;
            }

            var pair =
                new StatementPair(
                    existingChange
                        .PreviousStatementId
                        .Value,

                    existingChange
                        .CurrentStatementId);

            if (!existingByPair.TryAdd(
                    pair,
                    existingChange))
            {
                duplicateExistingChanges.Add(
                    existingChange);
            }
        }

        var createdCount =
            0;

        var updatedCount =
            0;

        var now =
            DateTimeOffset.UtcNow;

        var activeChanges =
            new List<BillChangeEntity>(
                desiredChanges.Count);

        foreach (var desiredChange in
                 desiredChanges)
        {
            var pair =
                new StatementPair(
                    desiredChange
                        .PreviousStatement
                        .Id,

                    desiredChange
                        .CurrentStatement
                        .Id);

            if (!existingByPair.TryGetValue(
                    pair,
                    out var existingChange))
            {
                var newChange =
                    CreateChangeEntity(
                        userId,
                        billStreamId,
                        desiredChange,
                        now);

                _dbContext.BillChanges.Add(
                    newChange);

                activeChanges.Add(
                    newChange);

                createdCount++;

                continue;
            }

            existingByPair.Remove(
                pair);

            if (ApplyDesiredValues(
                    existingChange,
                    desiredChange,
                    now))
            {
                updatedCount++;
            }

            activeChanges.Add(
                existingChange);
        }

        var changesToRemove =
            duplicateExistingChanges
                .Concat(
                    existingByPair.Values)
                .DistinctBy(
                    change =>
                        change.Id)
                .ToList();

        foreach (var changeToRemove in
                 changesToRemove)
        {
            if (!alertsByChangeId.TryGetValue(
                    changeToRemove.Id,
                    out var linkedAlerts))
            {
                continue;
            }

            _dbContext.BillAlerts.RemoveRange(
                linkedAlerts);
        }

        if (changesToRemove.Count >
            0)
        {
            _dbContext.BillChanges.RemoveRange(
                changesToRemove);
        }

        foreach (var activeChange in
                 activeChanges)
        {
            ReconcileAlert(
                providerName,
                activeChange,
                alertsByChangeId,
                now);

            IReadOnlyList<BillLineItemEntity>
                previousEvidence =
                    [];

            IReadOnlyList<BillLineItemEntity>
                currentEvidence =
                    [];

            if (activeChange
                    .PreviousStatementId
                    .HasValue &&
                lineItemsByStatement.TryGetValue(
                    activeChange
                        .PreviousStatementId
                        .Value,
                    out var previousItems))
            {
                previousEvidence =
                    previousItems;
            }

            if (lineItemsByStatement.TryGetValue(
                    activeChange.CurrentStatementId,
                    out var currentItems))
            {
                currentEvidence =
                    currentItems;
            }

            await _evidenceAlertService
                .ReconcileAsync(
                    userId,
                    billStreamId,
                    providerName,
                    activeChange,
                    previousEvidence,
                    currentEvidence,
                    now,
                    cancellationToken);
        }

        return new BillStatementChangeReconciliationResult(
            CreatedCount:
                createdCount,

            UpdatedCount:
                updatedCount,

            RemovedCount:
                changesToRemove.Count);
    }

    private IReadOnlyList<DesiredBillChange>
        BuildDesiredChanges(
            string providerName,
            IReadOnlyList<BillStatementEntity> statements,
            IReadOnlyDictionary<
                Guid,
                IReadOnlyList<BillLineItemEntity>>
                lineItemsByStatement)
    {
        if (statements.Count <
            2)
        {
            return [];
        }

        var desiredChanges =
            new List<DesiredBillChange>(
                statements.Count - 1);

        for (var index = 1;
             index < statements.Count;
             index++)
        {
            var previous =
                statements[index - 1];

            var current =
                statements[index];

            if (!string.Equals(
                    previous.CurrencyCode,
                    current.CurrencyCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lineItemsByStatement.TryGetValue(
                previous.Id,
                out var previousLineItems);

            lineItemsByStatement.TryGetValue(
                current.Id,
                out var currentLineItems);

            var previousDomainStatement =
                CreateDomainStatement(
                    providerName,
                    previous,
                    previousLineItems ??
                        []);

            var currentDomainStatement =
                CreateDomainStatement(
                    providerName,
                    current,
                    currentLineItems ??
                        []);

            var analysis =
                _analysisService.Analyze(
                    previousDomainStatement,
                    currentDomainStatement);

            var totalComparison =
                analysis.Comparison
                    .TotalComparison;

            if (totalComparison.MonthlyChange ==
                0m)
            {
                continue;
            }

            var changeType =
                totalComparison.MonthlyChange >
                0m
                    ? BillChangeType.TotalIncrease
                    : BillChangeType.TotalDecrease;

            desiredChanges.Add(
                new DesiredBillChange(
                    PreviousStatement:
                        previous,

                    CurrentStatement:
                        current,

                    ChangeType:
                        changeType,

                    PreviousAmount:
                        totalComparison.PreviousAmount,

                    CurrentAmount:
                        totalComparison.CurrentAmount,

                    AmountDifference:
                        totalComparison.MonthlyChange,

                    AnnualizedImpact:
                        totalComparison.AnnualChange,

                    Description:
                        BuildDescription(
                            analysis)));
        }

        return desiredChanges.AsReadOnly();
    }

    private static BillStatement
        CreateDomainStatement(
            string providerName,
            BillStatementEntity statement,
            IReadOnlyList<BillLineItemEntity> lineItems)
    {
        return new BillStatement(
            providerName:
                providerName,

            billingPeriodStart:
                statement.PeriodStart,

            billingPeriodEnd:
                statement.PeriodEnd,

            totalAmount:
                new BillAmount(
                    statement.TotalAmount),

            lineItems:
                lineItems.Select(
                    lineItem =>
                        new BillLineItem(
                            lineItem.Description,
                            lineItem.Amount)));
    }

    private static string BuildDescription(
        BillAnalysisResult analysis)
    {
        var summary =
            analysis.Explanation
                .Summary;

        var meaningfulChanges =
            analysis.Explanation
                .Changes
                .Take(
                    4)
                .ToList();

        if (meaningfulChanges.Count ==
            0)
        {
            return TruncateDescription(
                $"{summary} The provider statements confirm the amount change; BillWatch has not identified the cause yet.");
        }

        var evidence =
            string.Join(
                " ",
                meaningfulChanges.Select(
                    change =>
                        change.Description));

        if (Math.Abs(
                analysis.Explanation
                    .UnexplainedChange) <=
            0.01m)
        {
            return TruncateDescription(
                $"{summary} Why: {evidence}");
        }

        var unexplainedAmount =
            Math.Abs(
                analysis.Explanation
                    .UnexplainedChange);

        return TruncateDescription(
            $"{summary} Evidence found: {evidence} {FormatMoney(unexplainedAmount)}/month remains unexplained.");
    }

    private void ReconcileAlert(
        string providerName,
        BillChangeEntity change,
        IReadOnlyDictionary<
            Guid,
            List<BillAlertEntity>> alertsByChangeId,
        DateTimeOffset now)
    {
        var desiredAlert =
            BuildDesiredAlert(
                providerName,
                change);

        if (desiredAlert is null)
        {
            return;
        }

        alertsByChangeId.TryGetValue(
            change.Id,
            out var linkedAlerts);

        linkedAlerts ??=
            [];

        var changeAlerts =
            linkedAlerts
                .Where(
                    alert =>
                        alert.AlertType ==
                            EntityBillAlertType.BillIncrease ||
                        alert.AlertType ==
                            EntityBillAlertType.BillDecrease)
                .OrderBy(
                    alert =>
                        alert.CreatedAtUtc)
                .ThenBy(
                    alert =>
                        alert.Id)
                .ToList();

        if (changeAlerts.Count ==
            0)
        {
            _dbContext.BillAlerts.Add(
                new BillAlertEntity
                {
                    UserId =
                        change.UserId,

                    BillStreamId =
                        change.BillStreamId,

                    BillChangeId =
                        change.Id,

                    BillChange =
                        change,

                    AlertType =
                        desiredAlert.AlertType,

                    Severity =
                        desiredAlert.Severity,

                    Title =
                        desiredAlert.Title,

                    Message =
                        desiredAlert.Message,

                    IsRead =
                        false,

                    IsDismissed =
                        false,

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                });

            return;
        }

        var primaryAlert =
            changeAlerts[0];

        var contentChanged =
            primaryAlert.AlertType !=
                desiredAlert.AlertType ||
            primaryAlert.Severity !=
                desiredAlert.Severity ||
            !string.Equals(
                primaryAlert.Title,
                desiredAlert.Title,
                StringComparison.Ordinal) ||
            !string.Equals(
                primaryAlert.Message,
                desiredAlert.Message,
                StringComparison.Ordinal);

        if (contentChanged)
        {
            primaryAlert.AlertType =
                desiredAlert.AlertType;

            primaryAlert.Severity =
                desiredAlert.Severity;

            primaryAlert.Title =
                desiredAlert.Title;

            primaryAlert.Message =
                desiredAlert.Message;

            primaryAlert.IsRead =
                false;

            primaryAlert.IsDismissed =
                false;

            primaryAlert.UpdatedAtUtc =
                now;
        }

        if (changeAlerts.Count >
            1)
        {
            _dbContext.BillAlerts.RemoveRange(
                changeAlerts.Skip(
                    1));
        }
    }

    private static DesiredBillAlert?
        BuildDesiredAlert(
            string providerName,
            BillChangeEntity change)
    {
        var monthlyImpact =
            Math.Abs(
                change.AmountDifference);

        var annualImpact =
            Math.Abs(
                change.AnnualizedImpact);

        switch (change.ChangeType)
        {
            case BillChangeType.TotalIncrease:
                {
                    var title =
                        TruncateAlertValue(
                            $"{providerName} increased by {FormatMoney(monthlyImpact)}/month",
                            MaxAlertTitleLength);

                    var message =
                        TruncateAlertValue(
                            $"{FormatMoney(change.PreviousAmount)} → {FormatMoney(change.CurrentAmount)}. +{FormatMoney(monthlyImpact)}/month · +{FormatMoney(annualImpact)}/year. {change.Description}",
                            MaxAlertMessageLength);

                    return new DesiredBillAlert(
                        AlertType:
                            EntityBillAlertType.BillIncrease,

                        Severity:
                            BillAlertSeverity.Warning,

                        Title:
                            title,

                        Message:
                            message);
                }

            case BillChangeType.TotalDecrease:
                {
                    var title =
                        TruncateAlertValue(
                            $"{providerName} decreased by {FormatMoney(monthlyImpact)}/month",
                            MaxAlertTitleLength);

                    var message =
                        TruncateAlertValue(
                            $"{FormatMoney(change.PreviousAmount)} → {FormatMoney(change.CurrentAmount)}. {FormatMoney(monthlyImpact)}/month less · {FormatMoney(annualImpact)}/year less. {change.Description}",
                            MaxAlertMessageLength);

                    return new DesiredBillAlert(
                        AlertType:
                            EntityBillAlertType.BillDecrease,

                        Severity:
                            BillAlertSeverity.Info,

                        Title:
                            title,

                        Message:
                            message);
                }

            default:
                return null;
        }
    }

    private static BillChangeEntity
        CreateChangeEntity(
            Guid userId,
            Guid billStreamId,
            DesiredBillChange desiredChange,
            DateTimeOffset now)
    {
        return new BillChangeEntity
        {
            UserId =
                userId,

            BillStreamId =
                billStreamId,

            PreviousStatementId =
                desiredChange
                    .PreviousStatement
                    .Id,

            CurrentStatementId =
                desiredChange
                    .CurrentStatement
                    .Id,

            PreviousStatement =
                desiredChange
                    .PreviousStatement,

            CurrentStatement =
                desiredChange
                    .CurrentStatement,

            ChangeType =
                desiredChange.ChangeType,

            Confidence =
                BillChangeConfidence.Confirmed,

            Description =
                desiredChange.Description,

            PreviousAmount =
                desiredChange.PreviousAmount,

            CurrentAmount =
                desiredChange.CurrentAmount,

            AmountDifference =
                desiredChange.AmountDifference,

            AnnualizedImpact =
                desiredChange.AnnualizedImpact,

            IsAcknowledged =
                false,

            DetectedAtUtc =
                now,

            CreatedAtUtc =
                now,

            UpdatedAtUtc =
                now
        };
    }

    private static bool ApplyDesiredValues(
        BillChangeEntity existingChange,
        DesiredBillChange desiredChange,
        DateTimeOffset now)
    {
        var changed =
            false;

        if (existingChange.ChangeType !=
            desiredChange.ChangeType)
        {
            existingChange.ChangeType =
                desiredChange.ChangeType;

            changed =
                true;
        }

        if (existingChange.Confidence !=
            BillChangeConfidence.Confirmed)
        {
            existingChange.Confidence =
                BillChangeConfidence.Confirmed;

            changed =
                true;
        }

        if (!string.Equals(
                existingChange.Description,
                desiredChange.Description,
                StringComparison.Ordinal))
        {
            existingChange.Description =
                desiredChange.Description;

            changed =
                true;
        }

        if (existingChange.PreviousAmount !=
            desiredChange.PreviousAmount)
        {
            existingChange.PreviousAmount =
                desiredChange.PreviousAmount;

            changed =
                true;
        }

        if (existingChange.CurrentAmount !=
            desiredChange.CurrentAmount)
        {
            existingChange.CurrentAmount =
                desiredChange.CurrentAmount;

            changed =
                true;
        }

        if (existingChange.AmountDifference !=
            desiredChange.AmountDifference)
        {
            existingChange.AmountDifference =
                desiredChange.AmountDifference;

            changed =
                true;
        }

        if (existingChange.AnnualizedImpact !=
            desiredChange.AnnualizedImpact)
        {
            existingChange.AnnualizedImpact =
                desiredChange.AnnualizedImpact;

            changed =
                true;
        }

        if (!changed)
        {
            return false;
        }

        existingChange.DetectedAtUtc =
            now;

        existingChange.UpdatedAtUtc =
            now;

        return true;
    }

    private static string FormatMoney(
        decimal amount)
    {
        return
            $"${amount:0.00}";
    }

    private static string TruncateDescription(
        string value)
    {
        if (value.Length <=
            MaxDescriptionLength)
        {
            return value;
        }

        return
            value[..(MaxDescriptionLength - 1)]
            + "…";
    }

    private static string TruncateAlertValue(
        string value,
        int maximumLength)
    {
        if (value.Length <=
            maximumLength)
        {
            return value;
        }

        return
            value[..(maximumLength - 1)]
            + "…";
    }

    private readonly record struct StatementPair(
        Guid PreviousStatementId,
        Guid CurrentStatementId);

    private sealed record DesiredBillChange(
        BillStatementEntity PreviousStatement,
        BillStatementEntity CurrentStatement,
        BillChangeType ChangeType,
        decimal PreviousAmount,
        decimal CurrentAmount,
        decimal AmountDifference,
        decimal AnnualizedImpact,
        string Description);

    private sealed record DesiredBillAlert(
        EntityBillAlertType AlertType,
        BillAlertSeverity Severity,
        string Title,
        string Message);
}

public sealed record BillStatementChangeReconciliationResult(
    int CreatedCount,
    int UpdatedCount,
    int RemovedCount);