using System.Security.Cryptography;
using BillWatch.API.Authorization;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Accounts;
using BillWatch.API.Services.Plaid;
using BillWatch.API.Services.Statements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController : ControllerBase
{
    private readonly BillWatchDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PlaidConnectionDisconnectService _disconnectService;
    private readonly SecureBillStatementStorageService _statementStorage;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        PlaidConnectionDisconnectService disconnectService,
        SecureBillStatementStorageService statementStorage,
        ILogger<AccountController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _disconnectService = disconnectService;
        _statementStorage = statementStorage;
        _logger = logger;
    }

    [HttpGet("export")]
    [EnableRateLimiting("account-export")]
    public async Task<ActionResult<AccountDataExportResult>> ExportAccountData(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return NotFound();
        }

        var export = await AccountDataExportBuilder.CreateAsync(
            _dbContext,
            user,
            cancellationToken);

        Response.Headers.Append(
            "Content-Disposition",
            "attachment; filename=\"billwatch-data-export.json\"");

        return Ok(export);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount(
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return NoContent();
        }

        if (!string.Equals(
                request.Confirmation,
                "DELETE",
                StringComparison.Ordinal))
        {
            return BadRequest(
                new
                {
                    message =
                        "Type DELETE to confirm permanent account deletion."
                });
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            !await _userManager.CheckPasswordAsync(
                user,
                request.CurrentPassword))
        {
            return UnauthorizedDeletion(
                "Current password is incorrect.");
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                return UnauthorizedDeletion(
                    "A current authenticator code is required.");
            }

            var validTwoFactorCode =
                await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    NormalizeAuthenticatorCode(request.TwoFactorCode));

            if (!validTwoFactorCode)
            {
                return UnauthorizedDeletion(
                    "The authenticator code is invalid.");
            }
        }

        /*
         * Staff identities anchor privileged audit/access-key history.
         * Do not silently erase that security provenance or relax its
         * restrictive foreign keys. A privileged operator must remove all
         * staff roles first, after which normal self-service deletion can
         * proceed through this same path.
         */
        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Any(BillWatchRoles.IsStaffRole))
        {
            return Conflict(
                new
                {
                    message =
                        "BillWatch staff accounts cannot be self-deleted while privileged roles are assigned. Remove the staff roles through the authorized admin workflow first."
                });
        }

        /*
         * Revoke external Plaid access before deleting local connection
         * metadata and protected access tokens. If revocation cannot be
         * completed safely, do not claim that the BillWatch account was
         * deleted.
         */
        var connectionIds = await _dbContext.BankConnections
            .AsNoTracking()
            .Where(connection =>
                connection.UserId == userId &&
                connection.Status != BankConnectionStatus.Disconnected)
            .Select(connection => connection.Id)
            .ToListAsync(cancellationToken);

        foreach (var connectionId in connectionIds)
        {
            try
            {
                await _disconnectService.DisconnectAsync(
                    userId,
                    connectionId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is
                    PlaidApiException or
                    HttpRequestException or
                    CryptographicException or
                    InvalidOperationException)
            {
                _logger.LogWarning(
                    "BillWatch account deletion could not revoke a bank connection because of {ExceptionType}.",
                    exception.GetType().Name);

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        message =
                            "BillWatch could not safely finish deleting your account because a bank connection could not be revoked. Your BillWatch account was not deleted. Try again shortly."
                    });
            }
        }

        var storageKeys = await _dbContext.BillStatementUploads
            .AsNoTracking()
            .Where(upload => upload.UserId == userId)
            .Select(upload => upload.StorageKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        /*
         * The filesystem cannot participate in the PostgreSQL transaction.
         * Move owned statements into a same-volume quarantine first instead
         * of destroying them. A DB rollback restores them. A committed user
         * deletion purges them. The statement worker reconciles an interrupted
         * quarantine after process crashes by checking whether the user still
         * exists.
         */
        var quarantinedStatements =
            new List<BillStatementDeletionQuarantineEntry>();

        try
        {
            foreach (var storageKey in storageKeys)
            {
                quarantinedStatements.Add(
                    _statementStorage.QuarantineForAccountDeletion(
                        userId,
                        storageKey));
            }
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            RestoreQuarantinedStatementsBestEffort(
                quarantinedStatements);

            _logger.LogError(
                "BillWatch account deletion could not quarantine statement storage because of {ExceptionType}.",
                exception.GetType().Name);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message =
                        "BillWatch could not securely prepare stored statement files for deletion. Your BillWatch account was not deleted. Try again."
                });
        }

        IDbContextTransaction? transaction = null;
        var databaseCommitted = false;

        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);
            }

            /*
             * Phase 0: erase user-scoped subscription/program records.
             * Redemptions restrict entitlement deletion, so they go first.
             * Access-key RedemptionCount intentionally remains consumed:
             * deleting an account must never make a one-use key reusable.
             */
            var accessKeyRedemptions =
                await _dbContext.SubscriptionAccessKeyRedemptions
                    .Where(redemption => redemption.UserId == userId)
                    .ToListAsync(cancellationToken);

            _dbContext.SubscriptionAccessKeyRedemptions.RemoveRange(
                accessKeyRedemptions);

            await _dbContext.SaveChangesAsync(cancellationToken);

            var subscriptionEntitlements =
                await _dbContext.SubscriptionEntitlements
                    .Where(entitlement => entitlement.UserId == userId)
                    .ToListAsync(cancellationToken);

            var programMemberships =
                await _dbContext.UserProgramMemberships
                    .Where(membership => membership.UserId == userId)
                    .ToListAsync(cancellationToken);

            _dbContext.SubscriptionEntitlements.RemoveRange(
                subscriptionEntitlements);
            _dbContext.UserProgramMemberships.RemoveRange(
                programMemberships);

            await _dbContext.SaveChangesAsync(cancellationToken);

            /* Phase 1: remove statement/stream/account dependents. */
            var alerts = await _dbContext.BillAlerts
                .Where(alert => alert.UserId == userId)
                .ToListAsync(cancellationToken);
            var lineItems = await _dbContext.BillLineItems
                .Where(item => item.UserId == userId)
                .ToListAsync(cancellationToken);
            var uploads = await _dbContext.BillStatementUploads
                .Where(upload => upload.UserId == userId)
                .ToListAsync(cancellationToken);
            var aiEvaluations = await _dbContext.BillStatementAiEvaluations
                .Where(evaluation => evaluation.UserId == userId)
                .ToListAsync(cancellationToken);
            var changes = await _dbContext.BillChanges
                .Where(change => change.UserId == userId)
                .ToListAsync(cancellationToken);
            var transactions = await _dbContext.BankTransactions
                .Where(bankTransaction => bankTransaction.UserId == userId)
                .ToListAsync(cancellationToken);

            _dbContext.BillAlerts.RemoveRange(alerts);
            _dbContext.BillLineItems.RemoveRange(lineItems);
            _dbContext.BillStatementAiEvaluations.RemoveRange(aiEvaluations);
            _dbContext.BillStatementUploads.RemoveRange(uploads);
            _dbContext.BillChanges.RemoveRange(changes);
            _dbContext.BankTransactions.RemoveRange(transactions);

            await _dbContext.SaveChangesAsync(cancellationToken);

            /* Phase 2: remove statement/account/session parents. */
            var statements = await _dbContext.BillStatements
                .Where(statement => statement.UserId == userId)
                .ToListAsync(cancellationToken);
            var bankAccounts = await _dbContext.BankAccounts
                .Where(account => account.UserId == userId)
                .ToListAsync(cancellationToken);
            var linkSessions = await _dbContext.PlaidLinkSessions
                .Where(session => session.UserId == userId)
                .ToListAsync(cancellationToken);

            _dbContext.BillStatements.RemoveRange(statements);
            _dbContext.BankAccounts.RemoveRange(bankAccounts);
            _dbContext.PlaidLinkSessions.RemoveRange(linkSessions);

            await _dbContext.SaveChangesAsync(cancellationToken);

            /* Phase 3: remove top-level financial resources. */
            var bankConnections = await _dbContext.BankConnections
                .Where(connection => connection.UserId == userId)
                .ToListAsync(cancellationToken);
            var billStreams = await _dbContext.BillStreams
                .Where(stream => stream.UserId == userId)
                .ToListAsync(cancellationToken);

            _dbContext.BankConnections.RemoveRange(bankConnections);
            _dbContext.BillStreams.RemoveRange(billStreams);

            await _dbContext.SaveChangesAsync(cancellationToken);

            /*
             * Identity data is last. Admin audit targets and grantor
             * references are configured SetNull, so deleting a normal user
             * cannot erase unrelated security provenance.
             */
            var identityResult = await _userManager.DeleteAsync(user);

            if (!identityResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Identity account deletion failed.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            databaseCommitted = true;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            RestoreQuarantinedStatementsBestEffort(
                quarantinedStatements);

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        if (!databaseCommitted)
        {
            throw new InvalidOperationException(
                "Account deletion did not reach a committed state.");
        }

        var cleanupPending = false;

        foreach (var entry in quarantinedStatements)
        {
            try
            {
                _statementStorage.CommitAccountDeletionQuarantine(
                    entry);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                cleanupPending = true;

                _logger.LogError(
                    "BillWatch account deletion committed, but quarantined statement cleanup is pending because of {ExceptionType}.",
                    exception.GetType().Name);
            }
        }

        if (cleanupPending)
        {
            return Accepted(
                value: new
                {
                    message =
                        "Your BillWatch account was deleted. Secure cleanup of quarantined statement files is still being retried automatically."
                });
        }

        return NoContent();
    }

    private ObjectResult UnauthorizedDeletion(
        string title)
    {
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: title);
    }

    private void RestoreQuarantinedStatementsBestEffort(
        IEnumerable<BillStatementDeletionQuarantineEntry> entries)
    {
        foreach (var entry in entries.Reverse())
        {
            try
            {
                _statementStorage.RestoreAccountDeletionQuarantine(entry);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                _logger.LogCritical(
                    "BillWatch could not immediately restore a quarantined statement after account deletion rollback because of {ExceptionType}. Startup maintenance will retry recovery.",
                    exception.GetType().Name);
            }
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdText = _userManager.GetUserId(User);
        return Guid.TryParse(userIdText, out userId);
    }

    private static string NormalizeAuthenticatorCode(
        string code)
    {
        return code
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
    }
}

public sealed record DeleteAccountRequest(
    string Confirmation,
    string CurrentPassword,
    string? TwoFactorCode);
