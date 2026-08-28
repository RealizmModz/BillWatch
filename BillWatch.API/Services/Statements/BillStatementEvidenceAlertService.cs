using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Statements;

public sealed class BillStatementEvidenceAlertService
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    private readonly BillWatchDbContext
        _dbContext;

    public BillStatementEvidenceAlertService(
        BillWatchDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task ReconcileAsync(
        Guid userId,
        Guid billStreamId,
        string providerName,
        BillChangeEntity change,
        IReadOnlyList<BillLineItemEntity> previousLineItems,
        IReadOnlyList<BillLineItemEntity> currentLineItems,
        DateTimeOffset now,
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

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerName);

        ArgumentNullException.ThrowIfNull(
            change);

        ArgumentNullException.ThrowIfNull(
            previousLineItems);

        ArgumentNullException.ThrowIfNull(
            currentLineItems);

        if (change.UserId !=
                userId ||
            change.BillStreamId !=
                billStreamId)
        {
            throw new InvalidOperationException(
                "The bill change does not belong to the requested user and bill stream.");
        }

        ValidateLineItemOwnership(
            userId,
            change.PreviousStatementId,
            previousLineItems);

        ValidateLineItemOwnership(
            userId,
            change.CurrentStatementId,
            currentLineItems);

        var desiredAlerts =
            BuildDesiredAlerts(
                providerName.Trim(),
                previousLineItems,
                currentLineItems);

        var existingAlerts =
            await _dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            billStreamId &&
                        alert.BillChangeId ==
                            change.Id &&
                        (
                            alert.AlertType ==
                                BillAlertType.NewFee ||
                            alert.AlertType ==
                                BillAlertType.RemovedDiscount
                        ))
                .OrderBy(
                    alert =>
                        alert.CreatedAtUtc)
                .ThenBy(
                    alert =>
                        alert.Id)
                .ToListAsync(
                    cancellationToken);

        var existingByIdentity =
            new Dictionary<
                AlertIdentity,
                BillAlertEntity>();

        var duplicates =
            new List<BillAlertEntity>();

        foreach (var existingAlert in
                 existingAlerts)
        {
            var identity =
                new AlertIdentity(
                    existingAlert.AlertType,
                    existingAlert.Title);

            if (!existingByIdentity.TryAdd(
                    identity,
                    existingAlert))
            {
                duplicates.Add(
                    existingAlert);
            }
        }

        if (duplicates.Count >
            0)
        {
            _dbContext.BillAlerts.RemoveRange(
                duplicates);
        }

        foreach (var desired in
                 desiredAlerts)
        {
            var identity =
                new AlertIdentity(
                    desired.AlertType,
                    desired.Title);

            if (!existingByIdentity.TryGetValue(
                    identity,
                    out var existingAlert))
            {
                _dbContext.BillAlerts.Add(
                    new BillAlertEntity
                    {
                        UserId =
                            userId,

                        BillStreamId =
                            billStreamId,

                        BillChangeId =
                            change.Id,

                        BillChange =
                            change,

                        AlertType =
                            desired.AlertType,

                        Severity =
                            BillAlertSeverity.Warning,

                        Title =
                            desired.Title,

                        Message =
                            desired.Message,

                        IsRead =
                            false,

                        IsDismissed =
                            false,

                        CreatedAtUtc =
                            now,

                        UpdatedAtUtc =
                            now
                    });

                continue;
            }

            existingByIdentity.Remove(
                identity);

            var changed =
                existingAlert.Severity !=
                    BillAlertSeverity.Warning ||
                !string.Equals(
                    existingAlert.Message,
                    desired.Message,
                    StringComparison.Ordinal);

            if (!changed)
            {
                continue;
            }

            existingAlert.Severity =
                BillAlertSeverity.Warning;

            existingAlert.Message =
                desired.Message;

            existingAlert.IsRead =
                false;

            existingAlert.IsDismissed =
                false;

            existingAlert.UpdatedAtUtc =
                now;
        }

        if (existingByIdentity.Count >
            0)
        {
            _dbContext.BillAlerts.RemoveRange(
                existingByIdentity.Values);
        }
    }

    private static IReadOnlyList<DesiredEvidenceAlert>
        BuildDesiredAlerts(
            string providerName,
            IReadOnlyList<BillLineItemEntity> previousLineItems,
            IReadOnlyList<BillLineItemEntity> currentLineItems)
    {
        var previous =
            Aggregate(
                previousLineItems);

        var current =
            Aggregate(
                currentLineItems);

        var results =
            new List<DesiredEvidenceAlert>();

        foreach (var currentItem in
                 current.Values
                     .Where(
                         item =>
                             string.Equals(
                                 item.Category,
                                 "Fee",
                                 StringComparison.OrdinalIgnoreCase) &&
                             item.Amount >
                                 0m)
                     .OrderBy(
                         item =>
                             item.Description,
                         StringComparer.OrdinalIgnoreCase))
        {
            previous.TryGetValue(
                currentItem.Description,
                out var previousItem);

            var previousAmount =
                previousItem?.Amount
                ?? 0m;

            if (previousAmount >
                0m)
            {
                continue;
            }

            var title =
                Truncate(
                    $"{providerName}: new fee — {currentItem.Description}",
                    MaxTitleLength);

            var message =
                Truncate(
                    $"{FormatMoney(currentItem.Amount)} labeled \"{currentItem.Description}\" appeared on the latest provider statement. BillWatch is not assuming this fee will recur.",
                    MaxMessageLength);

            results.Add(
                new DesiredEvidenceAlert(
                    BillAlertType.NewFee,
                    title,
                    message));
        }

        foreach (var previousItem in
                 previous.Values
                     .Where(
                         item =>
                             string.Equals(
                                 item.Category,
                                 "Discount",
                                 StringComparison.OrdinalIgnoreCase) &&
                             item.Amount <
                                 0m)
                     .OrderBy(
                         item =>
                             item.Description,
                         StringComparer.OrdinalIgnoreCase))
        {
            current.TryGetValue(
                previousItem.Description,
                out var currentItem);

            var currentAmount =
                currentItem?.Amount
                ?? 0m;

            if (currentAmount <
                0m)
            {
                continue;
            }

            var discountAmount =
                Math.Abs(
                    previousItem.Amount);

            var title =
                Truncate(
                    $"{providerName}: discount removed — {previousItem.Description}",
                    MaxTitleLength);

            var message =
                Truncate(
                    $"A {FormatMoney(discountAmount)} discount labeled \"{previousItem.Description}\" was present on the previous provider statement but is absent from the latest statement. BillWatch has not assumed why the discount ended.",
                    MaxMessageLength);

            results.Add(
                new DesiredEvidenceAlert(
                    BillAlertType.RemovedDiscount,
                    title,
                    message));
        }

        return results.AsReadOnly();
    }

    private static Dictionary<string, AggregatedLineItem>
        Aggregate(
            IReadOnlyList<BillLineItemEntity> lineItems)
    {
        var results =
            new Dictionary<
                string,
                AggregatedLineItem>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in
                 lineItems)
        {
            var description =
                item.Description.Trim();

            if (description.Length ==
                0)
            {
                continue;
            }

            if (results.TryGetValue(
                    description,
                    out var existing))
            {
                results[description] =
                    existing with
                    {
                        Amount =
                            decimal.Round(
                                existing.Amount +
                                item.Amount,
                                2,
                                MidpointRounding.AwayFromZero),

                        Category =
                            existing.Category ??
                            item.Category
                    };

                continue;
            }

            results.Add(
                description,
                new AggregatedLineItem(
                    description,
                    item.Amount,
                    item.Category));
        }

        return results;
    }

    private static void ValidateLineItemOwnership(
        Guid userId,
        Guid? expectedStatementId,
        IReadOnlyList<BillLineItemEntity> lineItems)
    {
        if (!expectedStatementId.HasValue)
        {
            if (lineItems.Count >
                0)
            {
                throw new InvalidOperationException(
                    "Line-item evidence was supplied without an expected statement.");
            }

            return;
        }

        foreach (var lineItem in
                 lineItems)
        {
            if (lineItem.UserId !=
                    userId ||
                lineItem.BillStatementId !=
                    expectedStatementId.Value)
            {
                throw new InvalidOperationException(
                    "Line-item evidence does not belong to the requested statement history.");
            }
        }
    }

    private static string FormatMoney(
        decimal amount)
    {
        return
            $"${Math.Abs(amount):0.00}";
    }

    private static string Truncate(
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

    private readonly record struct AlertIdentity(
        BillAlertType AlertType,
        string Title);

    private sealed record AggregatedLineItem(
        string Description,
        decimal Amount,
        string? Category);

    private sealed record DesiredEvidenceAlert(
        BillAlertType AlertType,
        string Title,
        string Message);
}