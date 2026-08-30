using System.Security.Cryptography;
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
    private readonly BillWatchDbContext
        _dbContext;

    private readonly UserManager<ApplicationUser>
        _userManager;

    private readonly PlaidConnectionDisconnectService
        _disconnectService;

    private readonly SecureBillStatementStorageService
        _statementStorage;

    private readonly ILogger<AccountController>
        _logger;

    public AccountController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        PlaidConnectionDisconnectService disconnectService,
        SecureBillStatementStorageService statementStorage,
        ILogger<AccountController> logger)
    {
        _dbContext =
            dbContext;

        _userManager =
            userManager;

        _disconnectService =
            disconnectService;

        _statementStorage =
            statementStorage;

        _logger =
            logger;
    }

    [HttpGet("export")]
    [EnableRateLimiting("account-export")]
    public async Task<ActionResult<AccountDataExportResult>>
        ExportAccountData(
            CancellationToken cancellationToken)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByIdAsync(
                userId.ToString());

        if (user is null)
        {
            return NotFound();
        }

        var export =
            await AccountDataExportBuilder.CreateAsync(
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
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByIdAsync(
                userId.ToString());

        if (user is null)
        {
            return NoContent();
        }

        /*
         * Revoke external Plaid access before deleting the local
         * connection metadata and protected access tokens.
         *
         * We deliberately do not claim the account was deleted if
         * BillWatch cannot safely finish revocation.
         */
        var connectionIds =
            await _dbContext.BankConnections
                .AsNoTracking()
                .Where(
                    connection =>
                        connection.UserId ==
                            userId &&
                        connection.Status !=
                            BankConnectionStatus.Disconnected)
                .Select(
                    connection =>
                        connection.Id)
                .ToListAsync(
                    cancellationToken);

        foreach (var connectionId in
                 connectionIds)
        {
            try
            {
                await _disconnectService
                    .DisconnectAsync(
                        userId,
                        connectionId,
                        cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken
                    .IsCancellationRequested)
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

        /*
         * Storage keys come only from rows already ownership-scoped to
         * the authenticated user. No client-controlled path is accepted.
         */
        var storageKeys =
            await _dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(
                    upload =>
                        upload.UserId ==
                            userId)
                .Select(
                    upload =>
                        upload.StorageKey)
                .Distinct()
                .ToListAsync(
                    cancellationToken);

        foreach (var storageKey in
                 storageKeys)
        {
            if (!TryDeleteOwnedStatementFile(
                    userId,
                    storageKey))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message =
                            "BillWatch could not securely remove all stored statement files. Your BillWatch account was not deleted. Try again."
                    });
            }
        }

        IDbContextTransaction?
            transaction =
                null;

        try
        {
            if (_dbContext.Database
                .IsRelational())
            {
                transaction =
                    await _dbContext.Database
                        .BeginTransactionAsync(
                            cancellationToken);
            }

            /*
             * Phase 1:
             * Remove objects that depend on statements, streams,
             * accounts, or changes.
             */
            var alerts =
                await _dbContext.BillAlerts
                    .Where(
                        alert =>
                            alert.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var lineItems =
                await _dbContext.BillLineItems
                    .Where(
                        item =>
                            item.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var uploads =
                await _dbContext.BillStatementUploads
                    .Where(
                        upload =>
                            upload.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var aiEvaluations =
                await _dbContext.BillStatementAiEvaluations
                    .Where(
                        evaluation =>
                            evaluation.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var changes =
                await _dbContext.BillChanges
                    .Where(
                        change =>
                            change.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var transactions =
                await _dbContext.BankTransactions
                    .Where(
                        bankTransaction =>
                            bankTransaction.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            _dbContext.BillAlerts.RemoveRange(
                alerts);

            _dbContext.BillLineItems.RemoveRange(
                lineItems);

            _dbContext.BillStatementAiEvaluations.RemoveRange(
                aiEvaluations);

            _dbContext.BillStatementUploads.RemoveRange(
                uploads);

            _dbContext.BillChanges.RemoveRange(
                changes);

            _dbContext.BankTransactions.RemoveRange(
                transactions);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            /*
             * Phase 2:
             * Their dependent rows are gone, so statements and bank
             * accounts can now be safely removed.
             */
            var statements =
                await _dbContext.BillStatements
                    .Where(
                        statement =>
                            statement.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var bankAccounts =
                await _dbContext.BankAccounts
                    .Where(
                        account =>
                            account.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var linkSessions =
                await _dbContext.PlaidLinkSessions
                    .Where(
                        session =>
                            session.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            _dbContext.BillStatements.RemoveRange(
                statements);

            _dbContext.BankAccounts.RemoveRange(
                bankAccounts);

            _dbContext.PlaidLinkSessions.RemoveRange(
                linkSessions);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            /*
             * Phase 3:
             * Finally remove top-level financial resources.
             */
            var bankConnections =
                await _dbContext.BankConnections
                    .Where(
                        connection =>
                            connection.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            var billStreams =
                await _dbContext.BillStreams
                    .Where(
                        stream =>
                            stream.UserId ==
                                userId)
                    .ToListAsync(
                        cancellationToken);

            _dbContext.BankConnections.RemoveRange(
                bankConnections);

            _dbContext.BillStreams.RemoveRange(
                billStreams);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            /*
             * Identity data is last. Identity's own user-dependent
             * records are handled through UserManager.
             */
            var identityResult =
                await _userManager.DeleteAsync(
                    user);

            if (!identityResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Identity account deletion failed.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(
                    cancellationToken);
            }

            return NoContent();
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private bool TryDeleteOwnedStatementFile(
        Guid userId,
        string storageKey)
    {
        try
        {
            _statementStorage.Delete(
                storageKey);

            /*
             * Delete() deliberately suppresses normal cleanup errors
             * elsewhere in the upload pipeline. Account deletion is
             * different: verify that the owned file is truly gone
             * before claiming success.
             */
            try
            {
                using var remainingFile =
                    _statementStorage.OpenRead(
                        userId,
                        storageKey);

                _logger.LogError(
                    "BillWatch account deletion could not remove an owned statement file.");

                return false;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            _logger.LogError(
                "BillWatch account deletion could not remove statement storage because of {ExceptionType}.",
                exception.GetType().Name);

            return false;
        }
    }

    private bool TryGetUserId(
        out Guid userId)
    {
        var userIdText =
            _userManager.GetUserId(
                User);

        return Guid.TryParse(
            userIdText,
            out userId);
    }
}
