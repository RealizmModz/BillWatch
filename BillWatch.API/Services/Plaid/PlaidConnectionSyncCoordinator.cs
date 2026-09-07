using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidConnectionSyncCoordinator(
    BillWatchDbContext dbContext,
    PlaidAccountSyncService accountSyncService,
    PlaidTransactionSyncService transactionSyncService)
{
    public async Task<PlaidAccountSyncSummary> SyncAllAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var connectionIds =
            await GetActiveConnectionIdsAsync(
                userId,
                cancellationToken);

        var totalAccountsSynced = 0;

        foreach (var connectionId in connectionIds)
        {
            totalAccountsSynced +=
                await SyncAccountsAsync(
                    userId,
                    connectionId,
                    cancellationToken);
        }

        return new PlaidAccountSyncSummary(
            connectionIds.Count,
            totalAccountsSynced);
    }

    public async Task<PlaidTransactionSyncSummary> SyncAllTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var connectionIds =
            await GetActiveConnectionIdsAsync(
                userId,
                cancellationToken);

        var totalAdded = 0;
        var totalModified = 0;
        var totalRemoved = 0;

        foreach (var connectionId in connectionIds)
        {
            var result =
                await SyncTransactionsAsync(
                    userId,
                    connectionId,
                    cancellationToken);

            totalAdded += result.Added;
            totalModified += result.Modified;
            totalRemoved += result.Removed;
        }

        return new PlaidTransactionSyncSummary(
            connectionIds.Count,
            totalAdded,
            totalModified,
            totalRemoved);
    }

    public async Task<int> SyncAccountsAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(userId, connectionId);

        try
        {
            return await accountSyncService.SyncAccountsAsync(
                userId,
                connectionId,
                cancellationToken);
        }
        catch (PlaidApiException exception)
            when (PlaidConnectionAttentionClassifier.RequiresUserAttention(exception))
        {
            await PersistRequiresAttentionAsync(
                userId,
                connectionId,
                cancellationToken);

            throw;
        }
    }

    public async Task<PlaidTransactionConnectionSyncResult> SyncTransactionsAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(userId, connectionId);

        try
        {
            return await transactionSyncService.SyncConnectionAsync(
                userId,
                connectionId,
                cancellationToken);
        }
        catch (PlaidApiException exception)
            when (PlaidConnectionAttentionClassifier.RequiresUserAttention(exception))
        {
            await PersistRequiresAttentionAsync(
                userId,
                connectionId,
                cancellationToken);

            throw;
        }
    }

    private async Task<List<Guid>> GetActiveConnectionIdsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.BankConnections
            .AsNoTracking()
            .Where(connection =>
                connection.UserId == userId &&
                connection.Status == BankConnectionStatus.Active &&
                connection.ProtectedPlaidAccessToken != null &&
                connection.ProtectedPlaidAccessToken != string.Empty)
            .OrderBy(connection => connection.Id)
            .Select(connection => connection.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task PersistRequiresAttentionAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection =
            await dbContext.BankConnections.SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == connectionId &&
                    candidate.UserId == userId,
                cancellationToken);

        if (connection is null ||
            connection.Status == BankConnectionStatus.Disconnected)
        {
            return;
        }

        connection.Status =
            BankConnectionStatus.RequiresAttention;

        connection.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateIdentifiers(
        Guid userId,
        Guid connectionId)
    {
        ValidateUserId(userId);

        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid bank connection ID is required.",
                nameof(connectionId));
        }
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }
    }
}
