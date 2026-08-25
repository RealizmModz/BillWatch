using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidHostedLinkCompletionService
{
    private readonly BillWatchDbContext _dbContext;
    private readonly PlaidApiClient _plaidApiClient;
    private readonly PlaidTokenProtector _tokenProtector;
    private readonly PlaidConnectionExchangeService _exchangeService;

    public PlaidHostedLinkCompletionService(
        BillWatchDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector,
        PlaidConnectionExchangeService exchangeService)
    {
        _dbContext = dbContext;
        _plaidApiClient = plaidApiClient;
        _tokenProtector = tokenProtector;
        _exchangeService = exchangeService;
    }

    public async Task<PlaidHostedLinkCompletionResult> CheckAndCompleteAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session =
            await _dbContext.PlaidLinkSessions
                .SingleOrDefaultAsync(
                    existing =>
                        existing.Id == sessionId &&
                        existing.UserId == userId,
                    cancellationToken);

        if (session is null)
        {
            throw new KeyNotFoundException(
                "Plaid Link session was not found.");
        }

        if (session.Status != PlaidLinkSessionStatus.Pending)
        {
            return CreateResult(
                session);
        }

        var now =
            DateTimeOffset.UtcNow;

        if (session.ExpiresAtUtc <= now)
        {
            return await SetTerminalStatusAsync(
                session,
                PlaidLinkSessionStatus.Expired,
                cancellationToken);
        }

        var linkToken =
            _tokenProtector.UnprotectLinkToken(
                session.ProtectedLinkToken);

        using var response =
            await _plaidApiClient.PostAsync(
                "/link/token/get",
                new
                {
                    link_token = linkToken
                },
                cancellationToken);

        var plaidState =
            ReadPlaidSessionState(
                response.RootElement);

        if (!string.IsNullOrWhiteSpace(
                plaidState.PublicToken))
        {
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

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return new PlaidHostedLinkCompletionResult(
                session.Id,
                session.Status.ToString(),
                connection);
        }

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
            return await SetTerminalStatusAsync(
                session,
                PlaidLinkSessionStatus.Failed,
                cancellationToken);
        }

        return CreateResult(
            session);
    }

    private static PlaidSessionState ReadPlaidSessionState(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "link_sessions",
                out var linkSessionsElement) ||
            linkSessionsElement.ValueKind !=
                JsonValueKind.Array)
        {
            return new PlaidSessionState(
                false,
                false,
                false,
                false,
                null);
        }

        var hasLinkSessions = false;
        var hasFinishedSession = false;
        var hasUnfinishedSession = false;
        var hasExitedSession = false;
        string? publicToken = null;

        foreach (var linkSession in
                 linkSessionsElement.EnumerateArray())
        {
            hasLinkSessions = true;

            var isFinished =
                HasNonEmptyString(
                    linkSession,
                    "finished_at");

            if (isFinished)
            {
                hasFinishedSession = true;
            }
            else
            {
                hasUnfinishedSession = true;
            }

            if (HasExitResult(
                    linkSession))
            {
                hasExitedSession = true;
            }

            publicToken ??=
                GetPublicTokenFromResults(
                    linkSession);

            publicToken ??=
                GetPublicTokenFromLegacySuccess(
                    linkSession);

            if (!string.IsNullOrWhiteSpace(
                    publicToken))
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

    private static string? GetPublicTokenFromResults(
        JsonElement linkSession)
    {
        if (!linkSession.TryGetProperty(
                "results",
                out var resultsElement) ||
            resultsElement.ValueKind !=
                JsonValueKind.Object)
        {
            return null;
        }

        if (!resultsElement.TryGetProperty(
                "item_add_results",
                out var itemAddResultsElement) ||
            itemAddResultsElement.ValueKind !=
                JsonValueKind.Array)
        {
            return null;
        }

        foreach (var itemResult in
                 itemAddResultsElement.EnumerateArray())
        {
            if (!itemResult.TryGetProperty(
                    "public_token",
                    out var publicTokenElement) ||
                publicTokenElement.ValueKind !=
                    JsonValueKind.String)
            {
                continue;
            }

            var publicToken =
                publicTokenElement.GetString();

            if (!string.IsNullOrWhiteSpace(
                    publicToken))
            {
                return publicToken;
            }
        }

        return null;
    }

    private static string? GetPublicTokenFromLegacySuccess(
        JsonElement linkSession)
    {
        if (!linkSession.TryGetProperty(
                "on_success",
                out var onSuccessElement) ||
            onSuccessElement.ValueKind !=
                JsonValueKind.Object)
        {
            return null;
        }

        if (!onSuccessElement.TryGetProperty(
                "public_token",
                out var publicTokenElement) ||
            publicTokenElement.ValueKind !=
                JsonValueKind.String)
        {
            return null;
        }

        var publicToken =
            publicTokenElement.GetString();

        return string.IsNullOrWhiteSpace(
                publicToken)
            ? null
            : publicToken;
    }

    private static bool HasExitResult(
        JsonElement linkSession)
    {
        if (linkSession.TryGetProperty(
                "exit",
                out var exitElement) &&
            exitElement.ValueKind ==
                JsonValueKind.Object)
        {
            return true;
        }

        return linkSession.TryGetProperty(
                   "on_exit",
                   out var legacyExitElement) &&
               legacyExitElement.ValueKind ==
                   JsonValueKind.Object;
    }

    private static bool HasNonEmptyString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var propertyElement) ||
            propertyElement.ValueKind !=
                JsonValueKind.String)
        {
            return false;
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
        var now =
            DateTimeOffset.UtcNow;

        session.Status =
            status;

        session.CompletedAtUtc =
            now;

        session.UpdatedAtUtc =
            now;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return CreateResult(
            session);
    }

    private static PlaidHostedLinkCompletionResult CreateResult(
        PlaidLinkSessionEntity session)
    {
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