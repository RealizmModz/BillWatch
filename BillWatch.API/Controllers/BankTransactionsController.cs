using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bank-transactions")]
[Authorize]
public sealed class BankTransactionsController : ControllerBase
{
    private readonly BillWatchDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public BankTransactionsController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        take = Math.Clamp(
            take,
            1,
            500);

        var transactions =
            await _dbContext.BankTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.UserId == userId &&
                    !transaction.IsRemoved)
                .OrderByDescending(transaction =>
                    transaction.PostedDate)
                .ThenByDescending(transaction =>
                    transaction.CreatedAtUtc)
                .Take(take)
                .Select(transaction =>
                    new BankTransactionResult(
                        transaction.Id,
                        transaction.BankAccountId,
                        transaction.BankAccount.BankConnection.InstitutionName,
                        transaction.BankAccount.Name,
                        transaction.BankAccount.Mask,
                        transaction.Name,
                        transaction.MerchantName,
                        transaction.Amount,
                        transaction.IsoCurrencyCode,
                        transaction.PostedDate,
                        transaction.AuthorizedDate,
                        transaction.IsPending,
                        transaction.CategoryPrimary,
                        transaction.CategoryDetailed))
                .ToListAsync(
                    cancellationToken);

        return Ok(transactions);
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

public sealed record BankTransactionResult(
    Guid Id,
    Guid BankAccountId,
    string InstitutionName,
    string AccountName,
    string? AccountMask,
    string Name,
    string? MerchantName,
    decimal Amount,
    string? IsoCurrencyCode,
    DateOnly PostedDate,
    DateOnly? AuthorizedDate,
    bool IsPending,
    string? CategoryPrimary,
    string? CategoryDetailed);