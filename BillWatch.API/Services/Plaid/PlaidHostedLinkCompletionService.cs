using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidHostedLinkCompletionService
{
    private const int MaxLinkSessions =
        50;

    private const int MaxItemAddResults =
        50;

    private const int MaxPublicTokenLength =
        8 * 1024;

    private readonly BillWatchDbContext
        _dbContext;

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    private readonly PlaidConnectionExchangeService
        _exchangeService;

    public PlaidHostedLinkCompletionService(
        BillWatchDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector,
        PlaidConnectionExchangeService exchangeService)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        ArgumentNullException.ThrowIfNull(
            plaidApiClient);

        ArgumentNullException.ThrowIfNull(
            tokenProtector);

        ArgumentNullException.ThrowIfNull(
            exchangeService);

        _dbContext =
            dbContext;

        _plaidApiClient =
            plaidApiClient;

        _tokenProtector =
            tokenProtector;

        _exchangeService =
            exchangeService;
    }

    public async Task<PlaidHostedLinkCompletionResult>
        CheckAndCompleteAsync(
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        if (sessionId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid Plaid Link session ID is required.",
                nameof(sessionId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Ownership is enforced in the same database query that resolves
         * the session. Cross-user IDs therefore behave exactly like
         * nonexistent IDs.
         */
        var session =
            await _dbContext.PlaidLinkSessions
                .SingleOrDefaultAsync(
                    existing =>
                        existing.Id ==
                            sessionId &&
                        existing.UserId ==
                            userId,
                    cancellationToken);

        if (session is null)
        {
            throw new KeyNotFoundException(
                "Plaid Link session was not found.");
        }

        if (session.Status !=
            PlaidLinkSessionStatus.Pending)
        {
            return CreateResult(
                session);
        }

        var now =
            DateTimeOffset.UtcNow;

        if (session.ExpiresAtUtc <=
            now)
        {
            return await SetTerminalStatusAsync(
                session,
                PlaidLinkSessionStatus.Expired,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(
                session.ProtectedLinkToken))
        {
            throw new InvalidOperationException(
                "Pending Plaid Link session does not contain a valid protected token.");
        }

        /*
         * Plaintext Link tokens exist only in server memory and are never
         * returned from this service.
         */
        var linkToken =
            _tokenProtector.UnprotectLinkToken(
                session.ProtectedLinkToken);

        using var response =
            await _plaidApiClient.PostAsync(
                "link/token/get",
                new
                {
                    link_token =
                        linkToken
                },
                cancellationToken);

        var plaidState =
            ReadPlaidSessionState(
                response.RootElement);

        if (plaidState.PublicToken is not null)
        {
            /*
             * The public token is immediately exchanged server-side. It is
             * never persisted and never returned to the MAUI client.
             */
            var connection =
                await _exchangeService.ExchangeAndSaveAsync(
                    userId,
                    plaidState.PublicToken,
                    cancellationToken);

            var completedAt =
                DateTimeOffset.UtcNow;

            session.Status =
                PlaidLinkSessionStatus.Completed;

            session.CompletedAtUtc =
                completedAt;

            session.UpdatedAtUtc =
                completedAt;

            /*
             * A terminal Hosted Link session no longer needs its Link
             * credential. Retaining the ciphertext serves no purpose.
             */
            session.ProtectedLinkToken =
                string.Empty;

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return new PlaidHostedLinkCompletionResult(
                session.Id,
                session.Status.ToString(),
                connection);
        }

        /*
         * No provider session yet, or at least one provider session is
         * still unfinished. The correct state remains Pending.
         */
        if (!plaidState.HasLinkSessions ||
            plaidState.HasUnfinishedSession)
        {
            return CreateResult(
                session);
        }

        if (plaidState.HasExitedSession)
        {
            return await SetTerminalStatusAsync(
                session,
                PlaidLinkSessionStatus.Exited,
                cancellationToken);
        }

        if (plaidState.HasFinishedSession)
        {
            /*
             * A finished Hosted Link flow without a public token cannot be
             * treated as a successful connection.
             */
            return await SetTerminalStatusAsync(
                session,
                PlaidLinkSessionStatus.Failed,
                cancellationToken);
        }

        return CreateResult(
            session);
    }

    private static PlaidSessionState
        ReadPlaidSessionState(
            JsonElement root)
    {
        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link status response.");
        }

        if (!root.TryGetProperty(
                "link_sessions",
                out var linkSessionsElement) ||
            linkSessionsElement.ValueKind ==
                JsonValueKind.Null)
        {
            return new PlaidSessionState(
                HasLinkSessions:
                    false,
                HasFinishedSession:
                    false,
                HasUnfinishedSession:
                    false,
                HasExitedSession:
                    false,
                PublicToken:
                    null);
        }

        if (linkSessionsElement.ValueKind !=
            JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link status response.");
        }

        if (linkSessionsElement.GetArrayLength() >
            MaxLinkSessions)
        {
            throw new InvalidOperationException(
                "Plaid returned too many Hosted Link session records.");
        }

        var hasLinkSessions =
            false;

        var hasFinishedSession =
            false;

        var hasUnfinishedSession =
            false;

        var hasExitedSession =
            false;

        string?
            publicToken =
                null;

        foreach (var linkSession in
                 linkSessionsElement.EnumerateArray())
        {
            if (linkSession.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Plaid returned an invalid Hosted Link session record.");
            }

            hasLinkSessions =
                true;

            var isFinished =
                HasNonEmptyOptionalString(
                    linkSession,
                    "finished_at");

            if (isFinished)
            {
                hasFinishedSession =
                    true;
            }
            else
            {
                hasUnfinishedSession =
                    true;
            }

            if (HasExitResult(
                    linkSession))
            {
                hasExitedSession =
                    true;
            }

            publicToken ??=
                GetPublicTokenFromResults(
                    linkSession);

            publicToken ??=
                GetPublicTokenFromLegacySuccess(
                    linkSession);

            if (publicToken is not null)
            {
                break;
            }
        }

        return new PlaidSessionState(
            hasLinkSessions,
            hasFinishedSession,
            hasUnfinishedSession,
            hasExitedSession,
            publicToken);
    }

    private static string?
        GetPublicTokenFromResults(
            JsonElement linkSession)
    {
        if (!linkSession.TryGetProperty(
                "results",
                out var resultsElement) ||
            resultsElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (resultsElement.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link result.");
        }

        if (!resultsElement.TryGetProperty(
                "item_add_results",
                out var itemAddResultsElement) ||
            itemAddResultsElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (itemAddResultsElement.ValueKind !=
            JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link result.");
        }

        if (itemAddResultsElement.GetArrayLength() >
            MaxItemAddResults)
        {
            throw new InvalidOperationException(
                "Plaid returned too many Hosted Link item results.");
        }

        foreach (var itemResult in
                 itemAddResultsElement.EnumerateArray())
        {
            if (itemResult.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Plaid returned an invalid Hosted Link item result.");
            }

            var publicToken =
                GetOptionalPublicToken(
                    itemResult,
                    "public_token");

            if (publicToken is not null)
            {
                return publicToken;
            }
        }

        return null;
    }

    private static string?
        GetPublicTokenFromLegacySuccess(
            JsonElement linkSession)
    {
        if (!linkSession.TryGetProperty(
                "on_success",
                out var onSuccessElement) ||
            onSuccessElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (onSuccessElement.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link success result.");
        }

        return GetOptionalPublicToken(
            onSuccessElement,
            "public_token");
    }

    private static string?
        GetOptionalPublicToken(
            JsonElement element,
            string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var publicTokenElement) ||
            publicTokenElement.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (publicTokenElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link public token.");
        }

        var publicToken =
            publicTokenElement.GetString();

        if (string.IsNullOrWhiteSpace(
                publicToken))
        {
            return null;
        }

        if (publicToken.Length >
                MaxPublicTokenLength ||
            publicToken.Any(
                char.IsControl))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link public token.");
        }

        /*
         * Public tokens are opaque credentials. Do not trim or otherwise
         * normalize them.
         */
        return publicToken;
    }

    private static bool HasExitResult(
        JsonElement linkSession)
    {
        if (TryReadOptionalObjectPresence(
                linkSession,
                "exit",
                out var hasExit) &&
            hasExit)
        {
            return true;
        }

        return TryReadOptionalObjectPresence(
                   linkSession,
                   "on_exit",
                   out var hasLegacyExit) &&
               hasLegacyExit;
    }

    private static bool
        TryReadOptionalObjectPresence(
            JsonElement element,
            string propertyName,
            out bool isPresent)
    {
        isPresent =
            false;

        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind ==
                JsonValueKind.Null)
        {
            return false;
        }

        if (propertyElement.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link session record.");
        }

        isPresent =
            true;

        return true;
    }

    private static bool
        HasNonEmptyOptionalString(
            JsonElement element,
            string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind ==
                JsonValueKind.Null)
        {
            return false;
        }

        if (propertyElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link session record.");
        }

        return !string.IsNullOrWhiteSpace(
            propertyElement.GetString());
    }

    private async Task<PlaidHostedLinkCompletionResult>
        SetTerminalStatusAsync(
            PlaidLinkSessionEntity session,
            PlaidLinkSessionStatus status,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        if (status ==
            PlaidLinkSessionStatus.Pending)
        {
            throw new ArgumentException(
                "Terminal status cannot be Pending.",
                nameof(status));
        }

        var now =
            DateTimeOffset.UtcNow;

        session.Status =
            status;

        session.CompletedAtUtc =
            now;

        session.UpdatedAtUtc =
            now;

        /*
         * Terminal sessions no longer need a usable Link credential.
         */
        session.ProtectedLinkToken =
            string.Empty;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return CreateResult(
            session);
    }

    private static PlaidHostedLinkCompletionResult
        CreateResult(
            PlaidLinkSessionEntity session)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        return new PlaidHostedLinkCompletionResult(
            session.Id,
            session.Status.ToString(),
            null);
    }

    private sealed record PlaidSessionState(
        bool HasLinkSessions,
        bool HasFinishedSession,
        bool HasUnfinishedSession,
        bool HasExitedSession,
        string? PublicToken);
}

public sealed record PlaidHostedLinkCompletionResult(
    Guid SessionId,
    string Status,
    PlaidConnectionResult? Connection);