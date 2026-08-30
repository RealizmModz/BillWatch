using System.Text;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidAccountSyncService
{
    private const int MaxAccountsPerResponse =
        1_000;

    private const int MaxPlaidAccountIdLength =
        200;

    private const int MaxAccountNameLength =
        200;

    private const int MaxOfficialNameLength =
        300;

    private const int MaxMaskLength =
        10;

    private const int MaxPlaidTypeLength =
        50;

    private const int MaxPlaidSubtypeLength =
        100;

    private readonly BillWatchDbContext
        _dbContext;

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    public PlaidAccountSyncService(
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

    public async Task<PlaidAccountSyncSummary>
        SyncAllAccountsAsync(
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

        var totalAccountsSynced =
            0;

        foreach (var connectionId in
                 connectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

    public async Task<int> SyncAccountsAsync(
        Guid userId,
        Guid bankConnectionId,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        if (bankConnectionId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid bank connection ID is required.",
                nameof(bankConnectionId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * UserId + resource ID is the ownership boundary. A connection
         * belonging to another user is indistinguishable from one that
         * does not exist.
         */
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    existing =>
                        existing.Id ==
                            bankConnectionId &&
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

        var accessToken =
            _tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken);

        using var response =
            await _plaidApiClient.PostAsync(
                "accounts/get",
                new
                {
                    access_token =
                        accessToken
                },
                cancellationToken);

        var plaidAccounts =
            ParseAccounts(
                response.RootElement);

        var activePlaidAccountIds =
            plaidAccounts
                .Select(
                    account =>
                        account.PlaidAccountId)
                .ToArray();

        /*
         * A provider account identifier must never silently migrate from
         * one BillWatch BankConnection to another.
         */
        if (activePlaidAccountIds.Length >
            0)
        {
            var conflictingAccountExists =
                await _dbContext.BankAccounts
                    .AsNoTracking()
                    .AnyAsync(
                        account =>
                            account.UserId ==
                                userId &&
                            account.BankConnectionId !=
                                bankConnectionId &&
                            activePlaidAccountIds.Contains(
                                account.PlaidAccountId),
                        cancellationToken);

            if (conflictingAccountExists)
            {
                throw new InvalidOperationException(
                    "Plaid account identity conflicts with another bank connection.");
            }
        }

        /*
         * Load this connection's accounts once instead of issuing one
         * database query for every provider account.
         */
        var existingConnectionAccounts =
            await _dbContext.BankAccounts
                .Where(
                    account =>
                        account.UserId ==
                            userId &&
                        account.BankConnectionId ==
                            bankConnectionId)
                .ToListAsync(
                    cancellationToken);

        var existingByPlaidId =
            existingConnectionAccounts
                .ToDictionary(
                    account =>
                        account.PlaidAccountId,
                    StringComparer.Ordinal);

        var now =
            DateTimeOffset.UtcNow;

        var activeIdSet =
            new HashSet<string>(
                activePlaidAccountIds,
                StringComparer.Ordinal);

        foreach (var plaidAccount in
                 plaidAccounts)
        {
            if (!existingByPlaidId.TryGetValue(
                    plaidAccount.PlaidAccountId,
                    out var account))
            {
                account =
                    new BankAccountEntity
                    {
                        UserId =
                            userId,

                        BankConnectionId =
                            bankConnectionId,

                        PlaidAccountId =
                            plaidAccount.PlaidAccountId,

                        CreatedAtUtc =
                            now
                    };

                _dbContext.BankAccounts.Add(
                    account);

                existingByPlaidId.Add(
                    plaidAccount.PlaidAccountId,
                    account);
            }

            account.Name =
                plaidAccount.Name;

            account.OfficialName =
                plaidAccount.OfficialName;

            account.Mask =
                plaidAccount.Mask;

            account.AccountType =
                MapAccountType(
                    plaidAccount.PlaidType,
                    plaidAccount.PlaidSubtype);

            account.AccountSubtype =
                plaidAccount.PlaidSubtype;

            account.IsActive =
                true;

            account.UpdatedAtUtc =
                now;
        }

        /*
         * Accounts absent from the provider's current account collection
         * become inactive. They are not deleted because historical
         * transactions may still reference them.
         */
        foreach (var account in
                 existingConnectionAccounts)
        {
            if (!activeIdSet.Contains(
                    account.PlaidAccountId))
            {
                account.IsActive =
                    false;

                account.UpdatedAtUtc =
                    now;
            }
        }

        connection.Status =
            BankConnectionStatus.Active;

        connection.LastSuccessfulSyncAtUtc =
            now;

        connection.UpdatedAtUtc =
            now;

        /*
         * All local account mutations and the successful-sync timestamp are
         * committed together.
         */
        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return plaidAccounts.Count;
    }

    private static IReadOnlyList<PlaidAccountData>
        ParseAccounts(
            JsonElement root)
    {
        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid accounts response.");
        }

        if (!root.TryGetProperty(
                "accounts",
                out var accountsElement) ||
            accountsElement.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Plaid did not return a valid accounts collection.");
        }

        if (accountsElement.GetArrayLength() >
            MaxAccountsPerResponse)
        {
            throw new InvalidOperationException(
                "Plaid returned too many accounts in one response.");
        }

        var accounts =
            new List<PlaidAccountData>(
                accountsElement.GetArrayLength());

        var seenAccountIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var element in
                 accountsElement.EnumerateArray())
        {
            if (element.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Plaid returned an invalid account record.");
            }

            var plaidAccountId =
                GetRequiredOpaqueString(
                    element,
                    "account_id",
                    MaxPlaidAccountIdLength);

            if (!seenAccountIds.Add(
                    plaidAccountId))
            {
                throw new InvalidOperationException(
                    "Plaid returned a duplicate account identifier.");
            }

            var name =
                GetRequiredDisplayString(
                    element,
                    "name",
                    MaxAccountNameLength);

            var officialName =
                GetOptionalDisplayString(
                    element,
                    "official_name",
                    MaxOfficialNameLength);

            var mask =
                GetOptionalOpaqueString(
                    element,
                    "mask",
                    MaxMaskLength);

            var plaidType =
                GetRequiredOpaqueString(
                    element,
                    "type",
                    MaxPlaidTypeLength);

            var plaidSubtype =
                GetOptionalOpaqueString(
                    element,
                    "subtype",
                    MaxPlaidSubtypeLength);

            accounts.Add(
                new PlaidAccountData(
                    plaidAccountId,
                    name,
                    officialName,
                    mask,
                    plaidType,
                    plaidSubtype));
        }

        return accounts;
    }

    private static BankAccountType MapAccountType(
        string plaidType,
        string? plaidSubtype)
    {
        return plaidType.ToLowerInvariant() switch
        {
            "depository"
                when string.Equals(
                    plaidSubtype,
                    "checking",
                    StringComparison.OrdinalIgnoreCase)
                => BankAccountType.Checking,

            "depository"
                when string.Equals(
                    plaidSubtype,
                    "savings",
                    StringComparison.OrdinalIgnoreCase)
                => BankAccountType.Savings,

            "credit"
                => BankAccountType.CreditCard,

            "loan"
                => BankAccountType.Loan,

            "investment"
                => BankAccountType.Other,

            "brokerage"
                => BankAccountType.Other,

            "other"
                => BankAccountType.Other,

            _ =>
                BankAccountType.Unknown
        };
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
                $"Plaid account is missing required field '{propertyName}'.");
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
                "Plaid returned an invalid account field.");
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
                "Plaid returned an invalid account field.");
        }

        /*
         * Opaque provider identifiers are preserved exactly.
         */
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
                $"Plaid account is missing required field '{propertyName}'.");
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
                "Plaid returned an invalid account display field.");
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

    private sealed record PlaidAccountData(
        string PlaidAccountId,
        string Name,
        string? OfficialName,
        string? Mask,
        string PlaidType,
        string? PlaidSubtype);
}

public sealed record PlaidAccountSyncSummary(
    int ConnectionsSynced,
    int AccountsSynced);