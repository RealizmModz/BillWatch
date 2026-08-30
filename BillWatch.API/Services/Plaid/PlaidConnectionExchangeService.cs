using System.Text;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidConnectionExchangeService
{
    private const int MaxPublicTokenLength =
        8 * 1024;

    private const int MaxAccessTokenLength =
        8 * 1024;

    /*
     * These limits mirror the current database model.
     *
     * Provider identifiers must never be silently truncated because doing
     * so could change their identity. Human-readable institution names may
     * be safely normalized and bounded for display/storage.
     */
    private const int MaxPlaidItemIdLength =
        200;

    private const int MaxPlaidInstitutionIdLength =
        100;

    private const int MaxInstitutionNameLength =
        200;

    private const string DefaultInstitutionName =
        "Connected institution";

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    private readonly BillWatchDbContext
        _dbContext;

    public PlaidConnectionExchangeService(
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector,
        BillWatchDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(
            plaidApiClient);

        ArgumentNullException.ThrowIfNull(
            tokenProtector);

        ArgumentNullException.ThrowIfNull(
            dbContext);

        _plaidApiClient =
            plaidApiClient;

        _tokenProtector =
            tokenProtector;

        _dbContext =
            dbContext;
    }

    public async Task<PlaidConnectionResult> ExchangeAndSaveAsync(
        Guid userId,
        string publicToken,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(
                publicToken))
        {
            throw new ArgumentException(
                "Plaid public token is required.",
                nameof(publicToken));
        }

        if (publicToken.Length >
            MaxPublicTokenLength)
        {
            throw new ArgumentException(
                "Plaid public token exceeds the allowed length.",
                nameof(publicToken));
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Public tokens are opaque credentials.
         *
         * Do not trim, normalize, persist, return, or log them.
         */
        using var exchangeResponse =
            await _plaidApiClient.PostAsync(
                "item/public_token/exchange",
                new
                {
                    public_token =
                        publicToken
                },
                cancellationToken);

        var exchangeRoot =
            exchangeResponse.RootElement;

        var accessToken =
            GetRequiredProviderString(
                exchangeRoot,
                "access_token",
                MaxAccessTokenLength,
                "Plaid returned an invalid token exchange response.");

        var itemId =
            GetRequiredProviderString(
                exchangeRoot,
                "item_id",
                MaxPlaidItemIdLength,
                "Plaid returned an invalid token exchange response.");

        /*
         * Validate that Data Protection can safely protect the credential
         * before touching the database.
         *
         * The plaintext access token remains server-memory-only and is
         * never persisted.
         */
        var protectedAccessToken =
            _tokenProtector.Protect(
                accessToken);

        cancellationToken.ThrowIfCancellationRequested();

        using var itemResponse =
            await _plaidApiClient.PostAsync(
                "item/get",
                new
                {
                    access_token =
                        accessToken
                },
                cancellationToken);

        var item =
            GetRequiredObject(
                itemResponse.RootElement,
                "item",
                "Plaid returned an invalid item response.");

        var returnedItemId =
            GetOptionalProviderString(
                item,
                "item_id",
                MaxPlaidItemIdLength,
                "Plaid returned an invalid item response.");

        /*
         * The item returned by /item/get must agree with the item received
         * from the token exchange whenever Plaid supplies both values.
         *
         * Never persist a credential if upstream responses disagree about
         * which Plaid Item it belongs to.
         */
        if (returnedItemId is not null &&
            !string.Equals(
                returnedItemId,
                itemId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Plaid returned inconsistent item identifiers.");
        }

        var institutionId =
            GetOptionalProviderString(
                item,
                "institution_id",
                MaxPlaidInstitutionIdLength,
                "Plaid returned an invalid item response.");

        var institutionName =
            NormalizeInstitutionName(
                GetOptionalDisplayString(
                    item,
                    "institution_name"));

        var now =
            DateTimeOffset.UtcNow;

        /*
         * Every lookup is ownership-scoped.
         *
         * A Plaid Item ID supplied or returned for one user can never cause
         * BillWatch to retrieve or update another user's BankConnection.
         */
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    existing =>
                        existing.UserId ==
                            userId &&
                        existing.PlaidItemId ==
                            itemId,
                    cancellationToken);

        var isNewConnection =
            connection is null;

        if (connection is null)
        {
            connection =
                new BankConnectionEntity
                {
                    UserId =
                        userId,

                    PlaidItemId =
                        itemId,

                    CreatedAtUtc =
                        now
                };

            _dbContext.BankConnections.Add(
                connection);
        }

        ApplyConnectionState(
            connection,
            institutionName,
            institutionId,
            itemId,
            protectedAccessToken,
            now);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException)
            when (isNewConnection)
        {
            /*
             * Two requests for the same user's Plaid Item can race between
             * the ownership-scoped lookup and insert.
             *
             * The database's unique (UserId, PlaidItemId) index remains the
             * authority. If another request won the race, detach our failed
             * insert, reload only that user's row, and update it.
             *
             * If no matching row exists, this was some other database
             * failure and must propagate.
             */
            _dbContext.Entry(
                    connection)
                .State =
                    EntityState.Detached;

            var concurrentConnection =
                await _dbContext.BankConnections
                    .SingleOrDefaultAsync(
                        existing =>
                            existing.UserId ==
                                userId &&
                            existing.PlaidItemId ==
                                itemId,
                        cancellationToken);

            if (concurrentConnection is null)
            {
                throw;
            }

            ApplyConnectionState(
                concurrentConnection,
                institutionName,
                institutionId,
                itemId,
                protectedAccessToken,
                now);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            connection =
                concurrentConnection;
        }

        return new PlaidConnectionResult(
            connection.Id,
            connection.InstitutionName,
            connection.Status.ToString());
    }

    private static void ApplyConnectionState(
        BankConnectionEntity connection,
        string institutionName,
        string? institutionId,
        string itemId,
        string protectedAccessToken,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(
            connection);

        connection.InstitutionName =
            institutionName;

        connection.PlaidInstitutionId =
            institutionId;

        connection.PlaidItemId =
            itemId;

        connection.ProtectedPlaidAccessToken =
            protectedAccessToken;

        connection.Status =
            BankConnectionStatus.Active;

        connection.UpdatedAtUtc =
            updatedAtUtc;
    }

    private static JsonElement GetRequiredObject(
        JsonElement parent,
        string propertyName,
        string safeFailureMessage)
    {
        if (parent.ValueKind !=
                JsonValueKind.Object ||
            !parent.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        return value;
    }

    private static string GetRequiredProviderString(
        JsonElement parent,
        string propertyName,
        int maxLength,
        string safeFailureMessage)
    {
        var value =
            GetOptionalProviderString(
                parent,
                propertyName,
                maxLength,
                safeFailureMessage);

        if (value is null)
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        return value;
    }

    private static string? GetOptionalProviderString(
        JsonElement parent,
        string propertyName,
        int maxLength,
        string safeFailureMessage)
    {
        if (parent.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        if (!parent.TryGetProperty(
                propertyName,
                out var value))
        {
            return null;
        }

        if (value.ValueKind is
            JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        var text =
            value.GetString();

        if (string.IsNullOrWhiteSpace(
                text))
        {
            return null;
        }

        /*
         * Provider identifiers are opaque values. Reject invalid lengths
         * rather than trimming or truncating them.
         */
        if (text.Length >
            maxLength)
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        if (text.Any(
                char.IsControl))
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        return text;
    }

    private static string? GetOptionalDisplayString(
        JsonElement parent,
        string propertyName)
    {
        if (parent.ValueKind !=
                JsonValueKind.Object ||
            !parent.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind is
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
            JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string NormalizeInstitutionName(
        string? institutionName)
    {
        if (string.IsNullOrWhiteSpace(
                institutionName))
        {
            return DefaultInstitutionName;
        }

        var builder =
            new StringBuilder(
                Math.Min(
                    institutionName.Length,
                    MaxInstitutionNameLength));

        var previousWasWhitespace =
            false;

        foreach (var character in
                 institutionName.Trim())
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
                    MaxInstitutionNameLength)
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
                MaxInstitutionNameLength)
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
            ? DefaultInstitutionName
            : normalized;
    }
}

public sealed record PlaidConnectionResult(
    Guid Id,
    string InstitutionName,
    string Status);