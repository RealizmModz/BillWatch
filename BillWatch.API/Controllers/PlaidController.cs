using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Plaid;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/plaid")]
[Authorize]
public sealed class PlaidController : ControllerBase
{
    private readonly PlaidLinkService _plaidLinkService;
    private readonly PlaidConnectionExchangeService _exchangeService;
    private readonly PlaidHostedLinkCompletionService _completionService;
    private readonly PlaidConnectionSyncCoordinator _syncCoordinator;
    private readonly UserManager<ApplicationUser> _userManager;

    public PlaidController(
        PlaidLinkService plaidLinkService,
        PlaidConnectionExchangeService exchangeService,
        PlaidHostedLinkCompletionService completionService,
        PlaidAccountSyncService accountSyncService,
        PlaidTransactionSyncService transactionSyncService,
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _plaidLinkService = plaidLinkService;
        _exchangeService = exchangeService;
        _completionService = completionService;
        _syncCoordinator =
            new PlaidConnectionSyncCoordinator(
                dbContext,
                accountSyncService,
                transactionSyncService);
        _userManager = userManager;
    }

    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkSession(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var session =
            await _plaidLinkService.CreateLinkSessionAsync(
                userId,
                cancellationToken:
                    cancellationToken);

        return Ok(new
        {
            session.SessionId,
            session.HostedLinkUrl
        });
    }

    [HttpPost("connections/{connectionId:guid}/update-link-token")]
    public async Task<IActionResult> CreateUpdateLinkSession(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var session =
                await _plaidLinkService.CreateLinkSessionAsync(
                    userId,
                    connectionId,
                    cancellationToken);

            return Ok(new
            {
                session.SessionId,
                session.HostedLinkUrl
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                message = "Bank connection was not found."
            });
        }
        catch (InvalidOperationException)
        {
            return Conflict(new
            {
                message = "Bank connection cannot be updated."
            });
        }
    }

    [HttpPost("link-session/{sessionId:guid}/complete")]
    public async Task<IActionResult> CompleteLinkSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var result =
                await _completionService.CheckAndCompleteAsync(
                    userId,
                    sessionId,
                    cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                message = "Plaid Link session was not found."
            });
        }
    }

    [HttpPost("accounts/sync")]
    public async Task<IActionResult> SyncAllAccounts(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _syncCoordinator.SyncAllAccountsAsync(
                userId,
                cancellationToken);

        return Ok(result);
    }

    [HttpPost("transactions/sync")]
    public async Task<IActionResult> SyncAllTransactions(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _syncCoordinator.SyncAllTransactionsAsync(
                userId,
                cancellationToken);

        return Ok(result);
    }

    [HttpPost("connections/{connectionId:guid}/accounts/sync")]
    public async Task<IActionResult> SyncAccounts(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var syncedCount =
                await _syncCoordinator.SyncAccountsAsync(
                    userId,
                    connectionId,
                    cancellationToken);

            return Ok(new
            {
                connectionId,
                syncedCount
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                message = "Bank connection was not found."
            });
        }
    }

    [HttpPost("connections/{connectionId:guid}/transactions/sync")]
    public async Task<IActionResult> SyncTransactions(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var result =
                await _syncCoordinator.SyncTransactionsAsync(
                    userId,
                    connectionId,
                    cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                message = "Bank connection was not found."
            });
        }
    }

    [HttpPost("exchange-public-token")]
    public async Task<IActionResult> ExchangePublicToken(
        ExchangePublicTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PublicToken))
        {
            return BadRequest(new
            {
                message = "Plaid public token is required."
            });
        }

        var connection =
            await _exchangeService.ExchangeAndSaveAsync(
                userId,
                request.PublicToken,
                cancellationToken);

        return Ok(connection);
    }

    private bool TryGetUserId(
        out Guid userId)
    {
        var userIdText =
            _userManager.GetUserId(User);

        return Guid.TryParse(
            userIdText,
            out userId);
    }
}

public sealed record ExchangePublicTokenRequest(
    string PublicToken);
