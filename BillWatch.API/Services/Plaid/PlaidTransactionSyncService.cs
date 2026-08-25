using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidTransactionSyncService
{
    private readonly BillWatchDbContext _dbContext;
    private readonly PlaidApiClient _plaidApiClient;
    private readonly PlaidTokenProtector _tokenProtector;

    public PlaidTransactionSyncService(
        BillWatchDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector)
    {
        _dbContext = dbContext;
        _plaidApiClient = plaidApiClient;
        _tokenProtector = tokenProtector;
    }

    public async Task<PlaidTransactionSyncSummary> SyncAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connectionIds =
            await _dbContext.BankConnections
                .Where(connection =>
                    connection.UserId == userId &&
                    connection.Status == BankConnectionStatus.Active &&
                    connection.ProtectedPlaidAccessToken != null)
                .Select(connection => connection.Id)
                .ToListAsync(cancellationToken);

        var totalAdded = 0;
        var totalModified = 0;
        var totalRemoved = 0;

        foreach (var connectionId in connectionIds)
        {
            var result =
                await SyncConnectionAsync(
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

    public async Task<PlaidTransactionConnectionSyncResult>
        SyncConnectionAsync(
            Guid userId,
            Guid connectionId,
            CancellationToken cancellationToken = default)
    {
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    existing =>
                        existing.Id == connectionId &&
                        existing.UserId == userId,
                    cancellationToken);

        if (connection is null)
        {
            throw new KeyNotFoundException(
                "Bank connection was not found.");
        }

        if (string.IsNullOrWhiteSpace(
                connection.ProtectedPlaidAccessToken))
        {
            throw new InvalidOperationException(
                "The bank connection does not have a Plaid access token.");
        }

        var accounts =
            await _dbContext.BankAccounts
                .Where(account =>
                    account.UserId == userId &&
                    account.BankConnectionId == connectionId)
                .ToListAsync(cancellationToken);

        var accountsByPlaidId =
            accounts.ToDictionary(
                account => account.PlaidAccountId,
                StringComparer.Ordinal);

        if (accountsByPlaidId.Count == 0)
        {
            throw new InvalidOperationException(
                "Bank accounts must be synchronized before transactions.");
        }

        var accessToken =
            _tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken);

        var originalCursor =
            connection.TransactionsCursor;

        var cursor =
            originalCursor;

        var added =
            new List<PlaidTransactionData>();

        var modified =
            new List<PlaidTransactionData>();

        var removedIds =
            new List<string>();

        var hasMore = true;

        while (hasMore)
        {
            object payload =
                string.IsNullOrWhiteSpace(cursor)
                    ? new
                    {
                        access_token = accessToken,
                        count = 500,
                        options = new
                        {
                            personal_finance_category_version = "v2"
                        }
                    }
                    : new
                    {
                        access_token = accessToken,
                        cursor,
                        count = 500,
                        options = new
                        {
                            personal_finance_category_version = "v2"
                        }
                    };

            using var response =
                await _plaidApiClient.PostAsync(
                    "/transactions/sync",
                    payload,
                    cancellationToken);

            var root =
                response.RootElement;

            ReadTransactions(
                root,
                "added",
                added);

            ReadTransactions(
                root,
                "modified",
                modified);

            ReadRemovedTransactions(
                root,
                removedIds);

            hasMore =
                root.TryGetProperty(
                    "has_more",
                    out var hasMoreElement) &&
                hasMoreElement.GetBoolean();

            if (!root.TryGetProperty(
                    "next_cursor",
                    out var nextCursorElement))
            {
                throw new InvalidOperationException(
                    "Plaid did not return a transaction cursor.");
            }

            cursor =
                nextCursorElement.GetString();

            if (string.IsNullOrWhiteSpace(cursor))
            {
                throw new InvalidOperationException(
                    "Plaid returned an empty transaction cursor.");
            }
        }

        var now =
            DateTimeOffset.UtcNow;

        foreach (var plaidTransaction in added)
        {
            await UpsertTransactionAsync(
                userId,
                accountsByPlaidId,
                plaidTransaction,
                now,
                cancellationToken);
        }

        foreach (var plaidTransaction in modified)
        {
            await UpsertTransactionAsync(
                userId,
                accountsByPlaidId,
                plaidTransaction,
                now,
                cancellationToken);
        }

        if (removedIds.Count > 0)
        {
            var transactionsToRemove =
                await _dbContext.BankTransactions
                    .Where(transaction =>
                        transaction.UserId == userId &&
                        removedIds.Contains(
                            transaction.PlaidTransactionId))
                    .ToListAsync(cancellationToken);

            foreach (var transaction in transactionsToRemove)
            {
                transaction.IsRemoved = true;
                transaction.UpdatedAtUtc = now;
            }
        }

        connection.TransactionsCursor =
            cursor;

        connection.LastSuccessfulSyncAtUtc =
            now;

        connection.UpdatedAtUtc =
            now;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new PlaidTransactionConnectionSyncResult(
            connection.Id,
            added.Count,
            modified.Count,
            removedIds.Count);
    }

    private async Task UpsertTransactionAsync(
        Guid userId,
        IReadOnlyDictionary<string, BankAccountEntity> accountsByPlaidId,
        PlaidTransactionData plaidTransaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!accountsByPlaidId.TryGetValue(
                plaidTransaction.PlaidAccountId,
                out var account))
        {
            throw new InvalidOperationException(
                $"Plaid returned a transaction for unknown account '{plaidTransaction.PlaidAccountId}'.");
        }

        var transaction =
            await _dbContext.BankTransactions
                .SingleOrDefaultAsync(
                    existing =>
                        existing.UserId == userId &&
                        existing.PlaidTransactionId ==
                            plaidTransaction.PlaidTransactionId,
                    cancellationToken);

        if (transaction is null)
        {
            transaction =
                new BankTransactionEntity
                {
                    UserId = userId,
                    BankAccountId = account.Id,
                    PlaidTransactionId =
                        plaidTransaction.PlaidTransactionId,
                    CreatedAtUtc = now
                };

            _dbContext.BankTransactions.Add(
                transaction);
        }

        transaction.BankAccountId =
            account.Id;

        transaction.Name =
            plaidTransaction.Name;

        transaction.MerchantName =
            plaidTransaction.MerchantName;

        transaction.Amount =
            plaidTransaction.Amount;

        transaction.IsoCurrencyCode =
            plaidTransaction.IsoCurrencyCode;

        transaction.PostedDate =
            plaidTransaction.PostedDate;

        transaction.AuthorizedDate =
            plaidTransaction.AuthorizedDate;

        transaction.IsPending =
            plaidTransaction.IsPending;

        transaction.IsRemoved =
            false;

        transaction.CategoryPrimary =
            plaidTransaction.CategoryPrimary;

        transaction.CategoryDetailed =
            plaidTransaction.CategoryDetailed;

        transaction.UpdatedAtUtc =
            now;
    }

    private static void ReadTransactions(
        JsonElement root,
        string propertyName,
        ICollection<PlaidTransactionData> destination)
    {
        if (!root.TryGetProperty(
                propertyName,
                out var transactionsElement) ||
            transactionsElement.ValueKind !=
                JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in
                 transactionsElement.EnumerateArray())
        {
            destination.Add(
                ParseTransaction(
                    element));
        }
    }

    private static void ReadRemovedTransactions(
        JsonElement root,
        ICollection<string> destination)
    {
        if (!root.TryGetProperty(
                "removed",
                out var removedElement) ||
            removedElement.ValueKind !=
                JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in
                 removedElement.EnumerateArray())
        {
            var transactionId =
                GetOptionalString(
                    element,
                    "transaction_id");

            if (!string.IsNullOrWhiteSpace(
                    transactionId))
            {
                destination.Add(
                    transactionId);
            }
        }
    }

    private static PlaidTransactionData ParseTransaction(
        JsonElement element)
    {
        var categoryPrimary =
            default(string);

        var categoryDetailed =
            default(string);

        if (element.TryGetProperty(
                "personal_finance_category",
                out var categoryElement) &&
            categoryElement.ValueKind ==
                JsonValueKind.Object)
        {
            categoryPrimary =
                GetOptionalString(
                    categoryElement,
                    "primary");

            categoryDetailed =
                GetOptionalString(
                    categoryElement,
                    "detailed");
        }

        return new PlaidTransactionData(
            GetRequiredString(
                element,
                "transaction_id"),

            GetRequiredString(
                element,
                "account_id"),

            GetRequiredString(
                element,
                "name"),

            GetOptionalString(
                element,
                "merchant_name"),

            GetRequiredDecimal(
                element,
                "amount"),

            GetOptionalString(
                element,
                "iso_currency_code"),

            GetRequiredDate(
                element,
                "date"),

            GetOptionalDate(
                element,
                "authorized_date"),

            GetRequiredBoolean(
                element,
                "pending"),

            categoryPrimary,
            categoryDetailed);
    }

    private static string GetRequiredString(
        JsonElement element,
        string propertyName)
    {
        var value =
            GetOptionalString(
                element,
                propertyName);

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"Plaid transaction is missing required field '{propertyName}'.");
        }

        return value;
    }

    private static string? GetOptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private static decimal GetRequiredDecimal(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            !propertyElement.TryGetDecimal(
                out var value))
        {
            throw new InvalidOperationException(
                $"Plaid transaction is missing required field '{propertyName}'.");
        }

        return value;
    }

    private static bool GetRequiredBoolean(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind is not
                JsonValueKind.True and not
                JsonValueKind.False)
        {
            throw new InvalidOperationException(
                $"Plaid transaction is missing required field '{propertyName}'.");
        }

        return propertyElement.GetBoolean();
    }

    private static DateOnly GetRequiredDate(
        JsonElement element,
        string propertyName)
    {
        var value =
            GetOptionalDate(
                element,
                propertyName);

        return value
            ?? throw new InvalidOperationException(
                $"Plaid transaction is missing required field '{propertyName}'.");
    }

    private static DateOnly? GetOptionalDate(
        JsonElement element,
        string propertyName)
    {
        var value =
            GetOptionalString(
                element,
                propertyName);

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw new InvalidOperationException(
                $"Plaid returned an invalid date for '{propertyName}'.");
        }

        return date;
    }

    private sealed record PlaidTransactionData(
        string PlaidTransactionId,
        string PlaidAccountId,
        string Name,
        string? MerchantName,
        decimal Amount,
        string? IsoCurrencyCode,
        DateOnly PostedDate,
        DateOnly? AuthorizedDate,
        bool IsPending,
        string? CategoryPrimary,
        string? CategoryDetailed);
}

public sealed record PlaidTransactionConnectionSyncResult(
    Guid ConnectionId,
    int Added,
    int Modified,
    int Removed);

public sealed record PlaidTransactionSyncSummary(
    int ConnectionsSynced,
    int Added,
    int Modified,
    int Removed);