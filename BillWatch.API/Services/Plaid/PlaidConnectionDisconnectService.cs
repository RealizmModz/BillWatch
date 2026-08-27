using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidConnectionDisconnectService
{
    private readonly BillWatchDbContext
        _dbContext;

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    public PlaidConnectionDisconnectService(
        BillWatchDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector)
    {
        _dbContext =
            dbContext;

        _plaidApiClient =
            plaidApiClient;

        _tokenProtector =
            tokenProtector;
    }

    public async Task<bool> DisconnectAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == connectionId &&
                        item.UserId == userId,
                    cancellationToken);

        if (connection is null)
        {
            return false;
        }

        if (connection.Status ==
            BankConnectionStatus.Disconnected)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(
                connection.ProtectedPlaidAccessToken))
        {
            var accessToken =
                _tokenProtector.Unprotect(
                    connection.ProtectedPlaidAccessToken);

            try
            {
                using var response =
                    await _plaidApiClient.PostAsync(
                        "/item/remove",
                        new
                        {
                            access_token =
                                accessToken
                        },
                        cancellationToken);
            }
            catch (PlaidApiException exception)
                when (string.Equals(
                    exception.ErrorCode,
                    "ITEM_NOT_FOUND",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Plaid already removed the item.
            }
        }

        var now =
            DateTimeOffset.UtcNow;

        connection.ProtectedPlaidAccessToken =
            null;

        connection.TransactionsCursor =
            null;

        connection.Status =
            BankConnectionStatus.Disconnected;

        connection.UpdatedAtUtc =
            now;

        var accounts =
            await _dbContext.BankAccounts
                .Where(account =>
                    account.UserId == userId &&
                    account.BankConnectionId == connectionId)
                .ToListAsync(
                    cancellationToken);

        foreach (var account in accounts)
        {
            account.IsActive =
                false;

            account.UpdatedAtUtc =
                now;
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }
}