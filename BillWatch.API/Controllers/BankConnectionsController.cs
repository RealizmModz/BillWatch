using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Plaid;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bank-connections")]
[Authorize]
public sealed class BankConnectionsController : ControllerBase
{
    private readonly BillWatchDbContext
        _dbContext;

    private readonly PlaidConnectionDisconnectService
        _disconnectService;

    private readonly UserManager<ApplicationUser>
        _userManager;

    public BankConnectionsController(
        BillWatchDbContext dbContext,
        PlaidConnectionDisconnectService disconnectService,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext =
            dbContext;

        _disconnectService =
            disconnectService;

        _userManager =
            userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetConnections(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        var connections =
            await _dbContext.BankConnections
                .AsNoTracking()
                .Where(connection =>
                    connection.UserId == userId)
                .OrderByDescending(connection =>
                    connection.CreatedAtUtc)
                .Select(connection =>
                    new BankConnectionResult(
                        connection.Id,
                        connection.InstitutionName,
                        connection.Status,
                        connection.LastSuccessfulSyncAtUtc,
                        connection.CreatedAtUtc))
                .ToListAsync(
                    cancellationToken);

        return Ok(
            connections);
    }

    [HttpDelete("{connectionId:guid}")]
    public async Task<IActionResult> Disconnect(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        var disconnected =
            await _disconnectService.DisconnectAsync(
                userId,
                connectionId,
                cancellationToken);

        if (!disconnected)
        {
            return NotFound();
        }

        return NoContent();
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

public sealed record BankConnectionResult(
    Guid Id,
    string InstitutionName,
    BankConnectionStatus Status,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    DateTimeOffset CreatedAtUtc);