using System.Globalization;
using System.Text;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidTransactionSyncService
{
    private const int PlaidPageSize =
        500;

    private const int MaxPagesPerSync =
        200;

    private const int MaxTransactionsPerSync =
        100_000;

    private const int DatabaseLookupChunkSize =
        500;

    private const int MaxMutationRetries =
        2;

    private const int MaxCursorLength =
        4 * 1024;

    private const int MaxPlaidTransactionIdLength =
        200;

    private const int MaxPlaidAccountIdLength =
        200;

    private const int MaxTransactionNameLength =
        300;

    private const int MaxMerchantNameLength =
        300;

    private const int MaxCategoryPrimaryLength =
        100;

    private const int MaxCategoryDetailedLength =
        200;

    /*
     * PostgreSQL mapping is precision 18, scale 2.
     */
    private const decimal MaxStoredAmount =
        9999999999999999.99m;

    private const string
        MutationDuringPaginationErrorCode =
            "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION";

    private readonly BillWatchDbContext
        _dbContext;

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    public PlaidTransactionSyncService(
        BillWatchDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        ArgumentNullException.ThrowIfNull(
            plaidApiClient);

        ArgumentNullException.ThrowIfNull(
            tokenProtector);

        _dbContext =
            dbContext;

        _plaidApiClient =
            plaidApiClient;

        _tokenProtector =
            tokenProtector;
    }

    public async Task<PlaidTransactionSyncSummary>
        SyncAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var connectionIds =
            await _dbContext.BankConnections
                .AsNoTracking()
                .Where(
                    connection =>
                        connection.UserId ==
                            userId &&
                        connection.Status ==
                            BankConnectionStatus.Active &&
                        connection.ProtectedPlaidAccessToken !=
                            null &&
                        connection.ProtectedPlaidAccessToken !=
                            string.Empty)
                .OrderBy(
                    connection =>
                        connection.Id)
                .Select(
                    connection =>
                        connection.Id)
                .ToListAsync(
                    cancellationToken);

        var totalAdded =
            0;

        var totalModified =
            0;

        var totalRemoved =
            0;

        foreach (var connectionId in
                 connectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result =
                await SyncConnectionAsync(
                    userId,
                    connectionId,
                    cancellationToken);

            totalAdded +=
                result.Added;

            totalModified +=
                result.Modified;

            totalRemoved +=
                result.Removed;
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
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        if (connectionId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid bank connection ID is required.",
                nameof(connectionId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Ownership is enforced while resolving the connection.
         */
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    existing =>
                        existing.Id ==
                            connectionId &&
                        existing.UserId ==
                            userId,
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
                .Where(
                    account =>
                        account.UserId ==
                            userId &&
                        account.BankConnectionId ==
                            connectionId)
                .ToListAsync(
                    cancellationToken);

        var accountsByPlaidId =
            accounts.ToDictionary(
                account =>
                    account.PlaidAccountId,
                StringComparer.Ordinal);

        if (accountsByPlaidId.Count ==
            0)
        {
            throw new InvalidOperationException(
                "Bank accounts must be synchronized before transactions.");
        }

        var accountIds =
            accounts
                .Select(
                    account =>
                        account.Id)
                .ToHashSet();

        var accessToken =
            _tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken);

        var originalCursor =
            ValidateStoredCursor(
                connection.TransactionsCursor);

        var delta =
            await FetchTransactionDeltaAsync(
                accessToken,
                originalCursor,
                cancellationToken);

        /*
         * Validate every provider account reference before changing local
         * transaction state.
         */
        foreach (var transaction in
                 delta.Added)
        {
            if (!accountsByPlaidId.ContainsKey(
                    transaction.PlaidAccountId))
            {
                throw new InvalidOperationException(
                    "Plaid returned a transaction for an unknown bank account.");
            }
        }

        foreach (var transaction in
                 delta.Modified)
        {
            if (!accountsByPlaidId.ContainsKey(
                    transaction.PlaidAccountId))
            {
                throw new InvalidOperationException(
                    "Plaid returned a transaction for an unknown bank account.");
            }
        }

        var incomingTransactionIds =
            delta.Added
                .Concat(
                    delta.Modified)
                .Select(
                    transaction =>
                        transaction.PlaidTransactionId)
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        var existingTransactions =
            await LoadExistingTransactionsAsync(
                userId,
                incomingTransactionIds,
                cancellationToken);

        foreach (var existingTransaction in
                 existingTransactions.Values)
        {
            /*
             * A provider transaction ID already attached to another bank
             * connection must never be silently reassigned.
             */
            if (!accountIds.Contains(
                    existingTransaction.BankAccountId))
            {
                throw new InvalidOperationException(
                    "Plaid transaction identity conflicts with another bank connection.");
            }
        }

        var now =
            DateTimeOffset.UtcNow;

        foreach (var plaidTransaction in
                 delta.Added)
        {
            UpsertTransaction(
                userId,
                accountsByPlaidId,
                existingTransactions,
                plaidTransaction,
                now);
        }

        foreach (var plaidTransaction in
                 delta.Modified)
        {
            UpsertTransaction(
                userId,
                accountsByPlaidId,
                existingTransactions,
                plaidTransaction,
                now);
        }

        await ApplyRemovedTransactionsAsync(
            userId,
            accountIds,
            delta.RemovedIds,
            now,
            cancellationToken);

        /*
         * Advance the cursor only after every page has been validated and
         * every local change is ready to commit.
         *
         * If SaveChanges fails, the previous cursor remains authoritative
         * and the delta can safely be replayed.
         */
        connection.TransactionsCursor =
            delta.NextCursor;

        connection.Status =
            BankConnectionStatus.Active;

        connection.LastSuccessfulSyncAtUtc =
            now;

        connection.UpdatedAtUtc =
            now;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new PlaidTransactionConnectionSyncResult(
            connection.Id,
            delta.Added.Count,
            delta.Modified.Count,
            delta.RemovedIds.Count);
    }

    private async Task<PlaidTransactionDelta>
        FetchTransactionDeltaAsync(
            string accessToken,
            string? originalCursor,
            CancellationToken cancellationToken)
    {
        for (var attempt = 0;
             attempt <= MaxMutationRetries;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await FetchTransactionDeltaOnceAsync(
                    accessToken,
                    originalCursor,
                    cancellationToken);
            }
            catch (PlaidApiException exception)
                when (
                    attempt <
                        MaxMutationRetries &&
                    string.Equals(
                        exception.ErrorCode,
                        MutationDuringPaginationErrorCode,
                        StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Plaid requires a transactions/sync pagination sequence to
                 * restart from the original cursor if the underlying set
                 * mutates during pagination.
                 */
            }
        }

        throw new InvalidOperationException(
            "Plaid transaction synchronization could not complete.");
    }

    private async Task<PlaidTransactionDelta>
        FetchTransactionDeltaOnceAsync(
            string accessToken,
            string? originalCursor,
            CancellationToken cancellationToken)
    {
        var cursor =
            originalCursor;

        var added =
            new List<PlaidTransactionData>();

        var modified =
            new List<PlaidTransactionData>();

        var removedIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        var eventTransactionIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        var seenCursors =
            new HashSet<string>(
                StringComparer.Ordinal);

        if (cursor is not null)
        {
            seenCursors.Add(
                cursor);
        }

        for (var pageNumber = 1;
             pageNumber <= MaxPagesPerSync;
             pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            object payload =
                cursor is null
                    ? new
                    {
                        access_token =
                            accessToken,

                        count =
                            PlaidPageSize,

                        options =
                            new
                            {
                                personal_finance_category_version =
                                    "v2"
                            }
                    }
                    : new
                    {
                        access_token =
                            accessToken,

                        cursor,

                        count =
                            PlaidPageSize,

                        options =
                            new
                            {
                                personal_finance_category_version =
                                    "v2"
                            }
                    };

            using var response =
                await _plaidApiClient.PostAsync(
                    "transactions/sync",
                    payload,
                    cancellationToken);

            var root =
                response.RootElement;

            if (root.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Plaid returned an invalid transaction sync response.");
            }

            ReadTransactions(
                root,
                "added",
                added,
                eventTransactionIds,
                removedIds);

            ReadTransactions(
                root,
                "modified",
                modified,
                eventTransactionIds,
                removedIds);

            ReadRemovedTransactions(
                root,
                removedIds,
                eventTransactionIds);

            var totalEvents =
                added.Count +
                modified.Count +
                removedIds.Count;

            if (totalEvents >
                MaxTransactionsPerSync)
            {
                throw new InvalidOperationException(
                    "Plaid transaction synchronization exceeded the allowed event limit.");
            }

            var hasMore =
                GetRequiredBoolean(
                    root,
                    "has_more");

            var nextCursor =
                GetRequiredOpaqueString(
                    root,
                    "next_cursor",
                    MaxCursorLength);

            if (hasMore &&
                !seenCursors.Add(
                    nextCursor))
            {
                throw new InvalidOperationException(
                    "Plaid returned a repeated transaction cursor.");
            }

            cursor =
                nextCursor;

            if (!hasMore)
            {
                return new PlaidTransactionDelta(
                    added,
                    modified,
                    removedIds.ToArray(),
                    cursor);
            }
        }

        throw new InvalidOperationException(
            "Plaid transaction synchronization exceeded the allowed page limit.");
    }

    private async Task<Dictionary<string, BankTransactionEntity>>
        LoadExistingTransactionsAsync(
            Guid userId,
            IReadOnlyList<string> transactionIds,
            CancellationToken cancellationToken)
    {
        var result =
            new Dictionary<string, BankTransactionEntity>(
                StringComparer.Ordinal);

        for (var offset = 0;
             offset < transactionIds.Count;
             offset += DatabaseLookupChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk =
                transactionIds
                    .Skip(
                        offset)
                    .Take(
                        DatabaseLookupChunkSize)
                    .ToArray();

            var rows =
                await _dbContext.BankTransactions
                    .Where(
                        transaction =>
                            transaction.UserId ==
                                userId &&
                            chunk.Contains(
                                transaction.PlaidTransactionId))
                    .ToListAsync(
                        cancellationToken);

            foreach (var row in
                     rows)
            {
                if (!result.TryAdd(
                        row.PlaidTransactionId,
                        row))
                {
                    throw new InvalidOperationException(
                        "Duplicate Plaid transaction identity exists in local storage.");
                }
            }
        }

        return result;
    }

    private async Task ApplyRemovedTransactionsAsync(
        Guid userId,
        IReadOnlySet<Guid> accountIds,
        IReadOnlyList<string> removedIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (removedIds.Count ==
            0)
        {
            return;
        }

        var accountIdArray =
            accountIds.ToArray();

        for (var offset = 0;
             offset < removedIds.Count;
             offset += DatabaseLookupChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk =
                removedIds
                    .Skip(
                        offset)
                    .Take(
                        DatabaseLookupChunkSize)
                    .ToArray();

            /*
             * Scope removal by both UserId and the current connection's
             * owned account IDs.
             */
            var transactionsToRemove =
                await _dbContext.BankTransactions
                    .Where(
                        transaction =>
                            transaction.UserId ==
                                userId &&
                            accountIdArray.Contains(
                                transaction.BankAccountId) &&
                            chunk.Contains(
                                transaction.PlaidTransactionId))
                    .ToListAsync(
                        cancellationToken);

            foreach (var transaction in
                     transactionsToRemove)
            {
                transaction.IsRemoved =
                    true;

                transaction.UpdatedAtUtc =
                    now;
            }
        }
    }

    private void UpsertTransaction(
        Guid userId,
        IReadOnlyDictionary<string, BankAccountEntity> accountsByPlaidId,
        IDictionary<string, BankTransactionEntity> existingTransactions,
        PlaidTransactionData plaidTransaction,
        DateTimeOffset now)
    {
        var account =
            accountsByPlaidId[
                plaidTransaction.PlaidAccountId];

        if (!existingTransactions.TryGetValue(
                plaidTransaction.PlaidTransactionId,
                out var transaction))
        {
            transaction =
                new BankTransactionEntity
                {
                    UserId =
                        userId,

                    BankAccountId =
                        account.Id,

                    PlaidTransactionId =
                        plaidTransaction.PlaidTransactionId,

                    CreatedAtUtc =
                        now
                };

            _dbContext.BankTransactions.Add(
                transaction);

            existingTransactions.Add(
                plaidTransaction.PlaidTransactionId,
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
        ICollection<PlaidTransactionData> destination,
        ISet<string> eventTransactionIds,
        IReadOnlySet<string> removedIds)
    {
        if (!root.TryGetProperty(
                propertyName,
                out var transactionsElement) ||
            transactionsElement.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid transaction sync collection.");
        }

        foreach (var element in
                 transactionsElement.EnumerateArray())
        {
            var transaction =
                ParseTransaction(
                    element);

            if (removedIds.Contains(
                    transaction.PlaidTransactionId) ||
                !eventTransactionIds.Add(
                    transaction.PlaidTransactionId))
            {
                throw new InvalidOperationException(
                    "Plaid returned conflicting transaction events.");
            }

            destination.Add(
                transaction);
        }
    }

    private static void ReadRemovedTransactions(
        JsonElement root,
        ISet<string> destination,
        IReadOnlySet<string> eventTransactionIds)
    {
        if (!root.TryGetProperty(
                "removed",
                out var removedElement) ||
            removedElement.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid removed-transaction collection.");
        }

        foreach (var element in
                 removedElement.EnumerateArray())
        {
            if (element.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Plaid returned an invalid removed-transaction record.");
            }

            var transactionId =
                GetRequiredOpaqueString(
                    element,
                    "transaction_id",
                    MaxPlaidTransactionIdLength);

            if (eventTransactionIds.Contains(
                    transactionId))
            {
                throw new InvalidOperationException(
                    "Plaid returned conflicting transaction events.");
            }

            destination.Add(
                transactionId);
        }
    }

    private static PlaidTransactionData
        ParseTransaction(
            JsonElement element)
    {
        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid transaction record.");
        }

        string?
            categoryPrimary =
                null;

        string?
            categoryDetailed =
                null;

        if (element.TryGetProperty(
                "personal_finance_category",
                out var categoryElement) &&
            categoryElement.ValueKind !=
                JsonValueKind.Null)
        {
            if (categoryElement.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Plaid returned an invalid transaction category.");
            }

            categoryPrimary =
                GetOptionalDisplayString(
                    categoryElement,
                    "primary",
                    MaxCategoryPrimaryLength);

            categoryDetailed =
                GetOptionalDisplayString(
                    categoryElement,
                    "detailed",
                    MaxCategoryDetailedLength);
        }

        return new PlaidTransactionData(
            PlaidTransactionId:
                GetRequiredOpaqueString(
                    element,
                    "transaction_id",
                    MaxPlaidTransactionIdLength),

            PlaidAccountId:
                GetRequiredOpaqueString(
                    element,
                    "account_id",
                    MaxPlaidAccountIdLength),

            Name:
                GetRequiredDisplayString(
                    element,
                    "name",
                    MaxTransactionNameLength),

            MerchantName:
                GetOptionalDisplayString(
                    element,
                    "merchant_name",
                    MaxMerchantNameLength),

            Amount:
                GetRequiredAmount(
                    element,
                    "amount"),

            IsoCurrencyCode:
                GetOptionalCurrencyCode(
                    element,
                    "iso_currency_code"),

            PostedDate:
                GetRequiredDate(
                    element,
                    "date"),

            AuthorizedDate:
                GetOptionalDate(
                    element,
                    "authorized_date"),

            IsPending:
                GetRequiredBoolean(
                    element,
                    "pending"),

            CategoryPrimary:
                categoryPrimary,

            CategoryDetailed:
                categoryDetailed);
    }

    private static decimal GetRequiredAmount(
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
                "Plaid returned an invalid transaction amount.");
        }

        if (value <
                -MaxStoredAmount ||
            value >
                MaxStoredAmount)
        {
            throw new InvalidOperationException(
                "Plaid transaction amount exceeds the supported range.");
        }

        /*
         * The database stores two decimal places. Reject values that would
         * otherwise be silently rounded during persistence.
         */
        if (decimal.Round(
                value,
                2,
                MidpointRounding.ToEven) !=
            value)
        {
            throw new InvalidOperationException(
                "Plaid transaction amount exceeds supported cent precision.");
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
                "Plaid returned an invalid transaction boolean field.");
        }

        return propertyElement.GetBoolean();
    }

    private static DateOnly GetRequiredDate(
        JsonElement element,
        string propertyName)
    {
        return GetOptionalDate(
                   element,
                   propertyName)
               ?? throw new InvalidOperationException(
                   "Plaid returned an invalid required transaction date.");
    }

    private static DateOnly? GetOptionalDate(
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

        if (propertyElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid transaction date.");
        }

        var value =
            propertyElement.GetString();

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
                "Plaid returned an invalid transaction date.");
        }

        return date;
    }

    private static string?
        GetOptionalCurrencyCode(
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

        if (propertyElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid currency code.");
        }

        var value =
            propertyElement.GetString();

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var normalized =
            value.Trim()
                .ToUpperInvariant();

        if (normalized.Length !=
                3 ||
            normalized.Any(
                character =>
                    !char.IsAsciiLetter(
                        character)))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid currency code.");
        }

        return normalized;
    }

    private static string GetRequiredOpaqueString(
        JsonElement element,
        string propertyName,
        int maxLength)
    {
        var value =
            GetOptionalOpaqueString(
                element,
                propertyName,
                maxLength);

        if (value is null)
        {
            throw new InvalidOperationException(
                "Plaid transaction is missing a required identifier.");
        }

        return value;
    }

    private static string? GetOptionalOpaqueString(
        JsonElement element,
        string propertyName,
        int maxLength)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (propertyElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid transaction identifier.");
        }

        var value =
            propertyElement.GetString();

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        if (value.Length >
                maxLength ||
            value.Any(
                char.IsControl))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid transaction identifier.");
        }

        return value;
    }

    private static string GetRequiredDisplayString(
        JsonElement element,
        string propertyName,
        int maxLength)
    {
        var value =
            GetOptionalDisplayString(
                element,
                propertyName,
                maxLength);

        if (value is null)
        {
            throw new InvalidOperationException(
                "Plaid transaction is missing a required display field.");
        }

        return value;
    }

    private static string? GetOptionalDisplayString(
        JsonElement element,
        string propertyName,
        int maxLength)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (propertyElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid transaction display field.");
        }

        return NormalizeDisplayText(
            propertyElement.GetString(),
            maxLength);
    }

    private static string? NormalizeDisplayText(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var builder =
            new StringBuilder(
                Math.Min(
                    value.Length,
                    maxLength));

        var previousWasWhitespace =
            false;

        foreach (var character in
                 value.Trim())
        {
            if (char.IsControl(
                    character) &&
                !char.IsWhiteSpace(
                    character))
            {
                continue;
            }

            if (char.IsWhiteSpace(
                    character))
            {
                if (previousWasWhitespace ||
                    builder.Length ==
                        0)
                {
                    continue;
                }

                if (builder.Length >=
                    maxLength)
                {
                    break;
                }

                builder.Append(
                    ' ');

                previousWasWhitespace =
                    true;

                continue;
            }

            if (builder.Length >=
                maxLength)
            {
                break;
            }

            builder.Append(
                character);

            previousWasWhitespace =
                false;
        }

        var normalized =
            builder
                .ToString()
                .Trim();

        return normalized.Length ==
            0
            ? null
            : normalized;
    }

    private static string?
        ValidateStoredCursor(
            string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(
                cursor))
        {
            return null;
        }

        if (cursor.Length >
                MaxCursorLength ||
            cursor.Any(
                char.IsControl))
        {
            throw new InvalidOperationException(
                "Stored Plaid transaction cursor is invalid.");
        }

        return cursor;
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

    private sealed record PlaidTransactionDelta(
        IReadOnlyList<PlaidTransactionData> Added,
        IReadOnlyList<PlaidTransactionData> Modified,
        IReadOnlyList<string> RemovedIds,
        string NextCursor);
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