using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Bills;

public sealed class BankConnectionHealthAlertService
{
    private const string AlertTitle =
        "Bank connection needs attention";

    private readonly BillWatchDbContext
        _dbContext;

    public BankConnectionHealthAlertService(
        BillWatchDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task ReconcileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        var connections =
            await _dbContext.BankConnections
                .AsNoTracking()
                .Where(
                    connection =>
                        connection.UserId ==
                            userId)
                .OrderBy(
                    connection =>
                        connection.InstitutionName)
                .ThenBy(
                    connection =>
                        connection.Id)
                .ToListAsync(
                    cancellationToken);

        /*
         * Keep this comparison deliberately narrow.
         *
         * RequiresAttention means the persisted connection state says
         * BillWatch cannot currently rely on that connection.
         *
         * Disconnected is excluded because the user may have
         * intentionally disconnected it.
         */
        var attentionConnections =
            connections
                .Where(
                    connection =>
                        string.Equals(
                            connection.Status.ToString(),
                            "RequiresAttention",
                            StringComparison.Ordinal))
                .ToList();

        var existingAlerts =
            await _dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            null &&
                        alert.BillChangeId ==
                            null &&
                        alert.AlertType ==
                            BillAlertType.ConnectionIssue &&
                        alert.Title ==
                            AlertTitle)
                .OrderBy(
                    alert =>
                        alert.CreatedAtUtc)
                .ThenBy(
                    alert =>
                        alert.Id)
                .ToListAsync(
                    cancellationToken);

        if (attentionConnections.Count ==
            0)
        {
            if (existingAlerts.Count >
                0)
            {
                _dbContext.BillAlerts.RemoveRange(
                    existingAlerts);

                await _dbContext.SaveChangesAsync(
                    cancellationToken);
            }

            return;
        }

        var message =
            BuildMessage(
                attentionConnections
                    .Select(
                        connection =>
                            connection.InstitutionName)
                    .ToList());

        var now =
            DateTimeOffset.UtcNow;

        if (existingAlerts.Count ==
            0)
        {
            _dbContext.BillAlerts.Add(
                new BillAlertEntity
                {
                    UserId =
                        userId,

                    BillStreamId =
                        null,

                    BillChangeId =
                        null,

                    AlertType =
                        BillAlertType.ConnectionIssue,

                    Severity =
                        BillAlertSeverity.Warning,

                    Title =
                        AlertTitle,

                    Message =
                        message,

                    IsRead =
                        false,

                    IsDismissed =
                        false,

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                });

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return;
        }

        var primaryAlert =
            existingAlerts[0];

        var changed =
            primaryAlert.Severity !=
                BillAlertSeverity.Warning ||
            !string.Equals(
                primaryAlert.Message,
                message,
                StringComparison.Ordinal);

        if (changed)
        {
            primaryAlert.Severity =
                BillAlertSeverity.Warning;

            primaryAlert.Message =
                message;

            /*
             * The affected connection set materially changed,
             * so surface the corrected alert again.
             */
            primaryAlert.IsRead =
                false;

            primaryAlert.IsDismissed =
                false;

            primaryAlert.UpdatedAtUtc =
                now;
        }

        if (existingAlerts.Count >
            1)
        {
            _dbContext.BillAlerts.RemoveRange(
                existingAlerts.Skip(
                    1));
        }

        if (_dbContext.ChangeTracker
            .HasChanges())
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
    }

    private static string BuildMessage(
        IReadOnlyList<string> institutionNames)
    {
        var names =
            institutionNames
                .Where(
                    name =>
                        !string.IsNullOrWhiteSpace(
                            name))
                .Select(
                    name =>
                        name.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    name =>
                        name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (names.Count ==
            0)
        {
            return
                "A connected bank needs attention before BillWatch can continue reliable bank monitoring. Review the connection in Connect.";
        }

        if (names.Count ==
            1)
        {
            return
                $"{names[0]} needs attention before BillWatch can continue reliable bank monitoring. Review or reconnect it in Connect.";
        }

        var visibleNames =
            names
                .Take(
                    5)
                .ToList();

        var remainder =
            names.Count -
            visibleNames.Count;

        var remainderText =
            remainder >
            0
                ? $" and {remainder} more"
                : string.Empty;

        return
            $"{names.Count} bank connections need attention: {string.Join(", ", visibleNames)}{remainderText}. Review them in Connect so BillWatch can continue reliable monitoring.";
    }
}