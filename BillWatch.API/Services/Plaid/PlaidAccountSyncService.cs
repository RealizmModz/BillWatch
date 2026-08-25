using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidAccountSyncService
{
    private readonly BillWatchDbContext _dbContext;
    private readonly PlaidApiClient _plaidApiClient;
    private readonly PlaidTokenProtector _tokenProtector;

    public PlaidAccountSyncService(
        BillWatchDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector)
    {
        _dbContext = dbContext;
        _plaidApiClient = plaidApiClient;
        _tokenProtector = tokenProtector;
    }

    public async Task<PlaidAccountSyncSummary> SyncAllAccountsAsync(
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

    public async Task<int> SyncAccountsAsync(
        Guid userId,
        Guid bankConnectionId,
        CancellationToken cancellationToken = default)
    {
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    existing =>
                        existing.Id == bankConnectionId &&
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

        var accessToken =
            _tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken);

        using var response =
            await _plaidApiClient.PostAsync(
                "/accounts/get",
                new
                {
                    access_token = accessToken
                },
                cancellationToken);

        if (!response.RootElement.TryGetProperty(
                "accounts",
                out var accountsElement) ||
            accountsElement.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Plaid did not return an accounts collection.");
        }

        var now =
            DateTimeOffset.UtcNow;

        var activePlaidAccountIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        var syncedCount = 0;

        foreach (var plaidAccount in
                 accountsElement.EnumerateArray())
        {
            var plaidAccountId =
                GetRequiredString(
                    plaidAccount,
                    "account_id");

            activePlaidAccountIds.Add(
                plaidAccountId);

            var account =
                await _dbContext.BankAccounts
                    .SingleOrDefaultAsync(
                        existing =>
                            existing.UserId == userId &&
                            existing.PlaidAccountId == plaidAccountId,
                        cancellationToken);

            if (account is null)
            {
                account =
                    new BankAccountEntity
                    {
                        UserId = userId,
                        BankConnectionId = bankConnectionId,
                        PlaidAccountId = plaidAccountId,
                        CreatedAtUtc = now
                    };

                _dbContext.BankAccounts.Add(
                    account);
            }

            account.BankConnectionId =
                bankConnectionId;

            account.Name =
                GetRequiredString(
                    plaidAccount,
                    "name");

            account.OfficialName =
                GetOptionalString(
                    plaidAccount,
                    "official_name");

            account.Mask =
                GetOptionalString(
                    plaidAccount,
                    "mask");

            var plaidType =
                GetRequiredString(
                    plaidAccount,
                    "type");

            var plaidSubtype =
                GetOptionalString(
                    plaidAccount,
                    "subtype");

            account.AccountType =
                MapAccountType(
                    plaidType,
                    plaidSubtype);

            account.AccountSubtype =
                plaidSubtype;

            account.IsActive =
                true;

            account.UpdatedAtUtc =
                now;

            syncedCount++;
        }

        var existingConnectionAccounts =
            await _dbContext.BankAccounts
                .Where(account =>
                    account.UserId == userId &&
                    account.BankConnectionId == bankConnectionId)
                .ToListAsync(cancellationToken);

        foreach (var account in
                 existingConnectionAccounts)
        {
            if (!activePlaidAccountIds.Contains(
                    account.PlaidAccountId))
            {
                account.IsActive =
                    false;

                account.UpdatedAtUtc =
                    now;
            }
        }

        connection.LastSuccessfulSyncAtUtc =
            now;

        connection.UpdatedAtUtc =
            now;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return syncedCount;
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

            _ => BankAccountType.Unknown
        };
    }

    private static string GetRequiredString(
        JsonElement element,
        string propertyName)
    {
        var value =
            GetOptionalString(
                element,
                propertyName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Plaid account is missing required field '{propertyName}'.");
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
}

public sealed record PlaidAccountSyncSummary(
    int ConnectionsSynced,
    int AccountsSynced);