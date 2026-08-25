using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bank-accounts")]
[Authorize]
public sealed class BankAccountsController : ControllerBase
{
    private readonly BillWatchDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public BankAccountsController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetAccounts(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var accounts =
            await _dbContext.BankAccounts
                .AsNoTracking()
                .Where(account =>
                    account.UserId == userId &&
                    account.IsActive)
                .OrderBy(account =>
                    account.BankConnection.InstitutionName)
                .ThenBy(account =>
                    account.Name)
                .Select(account =>
                    new BankAccountResult(
                        account.Id,
                        account.BankConnectionId,
                        account.BankConnection.InstitutionName,
                        account.Name,
                        account.OfficialName,
                        account.Mask,
                        account.AccountType.ToString(),
                        account.AccountSubtype,
                        account.IsActive))
                .ToListAsync(
                    cancellationToken);

        return Ok(accounts);
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

public sealed record BankAccountResult(
    Guid Id,
    Guid BankConnectionId,
    string InstitutionName,
    string Name,
    string? OfficialName,
    string? Mask,
    string AccountType,
    string? AccountSubtype,
    bool IsActive);