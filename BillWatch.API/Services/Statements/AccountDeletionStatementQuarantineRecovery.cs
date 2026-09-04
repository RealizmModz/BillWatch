using BillWatch.API.Data;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Statements;

public sealed class AccountDeletionStatementQuarantineRecovery(
    BillWatchDbContext dbContext,
    SecureBillStatementStorageService statementStorage,
    ILogger<AccountDeletionStatementQuarantineRecovery> logger)
{
    public async Task<int> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        var entries =
            statementStorage.GetPendingAccountDeletionQuarantineEntries();

        if (entries.Count == 0)
        {
            return 0;
        }

        var reconciled = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userExists =
                await dbContext.Users
                    .AsNoTracking()
                    .AnyAsync(
                        user => user.Id == entry.UserId,
                        cancellationToken);

            try
            {
                if (userExists)
                {
                    statementStorage.RestoreAccountDeletionQuarantine(
                        entry);

                    logger.LogWarning(
                        "Restored a quarantined statement after an interrupted account deletion for user {UserId}.",
                        entry.UserId);
                }
                else
                {
                    statementStorage.CommitAccountDeletionQuarantine(
                        entry);

                    logger.LogInformation(
                        "Purged a quarantined statement after committed account deletion for user {UserId}.",
                        entry.UserId);
                }

                reconciled++;
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                logger.LogError(
                    "Account-deletion statement quarantine reconciliation failed for user {UserId} with {ExceptionType}.",
                    entry.UserId,
                    exception.GetType().Name);
            }
        }

        return reconciled;
    }
}
