using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidConnectionDisconnectService
{
    private const string ItemNotFoundErrorCode =
        "ITEM_NOT_FOUND";

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

    public async Task<bool> DisconnectAsync(
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
         * Resolve the resource using both UserId and connection ID.
         *
         * Cross-user manipulation therefore returns the same result as an
         * unknown connection.
         */
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                            connectionId &&
                        item.UserId ==
                            userId,
                    cancellationToken);

        if (connection is null)
        {
            return false;
        }

        /*
         * If any protected provider credential remains, attempt provider
         * revocation even if local state already says Disconnected.
         *
         * This repairs a possible partially completed previous operation.
         */
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
                        "item/remove",
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
                    ItemNotFoundErrorCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Provider state is already equivalent to the requested
                 * result. Continue with local credential removal.
                 */
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var now =
            DateTimeOffset.UtcNow;

        /*
         * Once provider revocation succeeds, remove the local credential
         * and synchronization cursor before reporting success.
         */
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
                .Where(
                    account =>
                        account.UserId ==
                            userId &&
                        account.BankConnectionId ==
                            connectionId)
                .ToListAsync(
                    cancellationToken);

        /*
         * Historical accounts and transactions remain available for
         * BillWatch history, but disconnected accounts cannot be treated as
         * currently active bank sources.
         */
        foreach (var account in
                 accounts)
        {
            account.IsActive =
                false;

            account.UpdatedAtUtc =
                now;
        }

        /*
         * The credential removal, disconnected status, and account
         * deactivation are one local database commit.
         *
         * If cancellation or a database failure occurs after Plaid has
         * already removed the Item, retrying is safe: ITEM_NOT_FOUND is
         * treated as successful provider revocation.
         */
        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }
}