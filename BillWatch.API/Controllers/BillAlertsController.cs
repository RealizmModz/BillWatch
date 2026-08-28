using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
public sealed class BillAlertsController : ControllerBase
{
    private const int DefaultTake =
        50;

    private const int MaximumTake =
        100;

    private readonly BillWatchDbContext
        _dbContext;

    private readonly UserManager<ApplicationUser>
        _userManager;

    public BillAlertsController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext =
            dbContext;

        _userManager =
            userManager;
    }

    [HttpGet]
    public async Task<ActionResult<
        IReadOnlyList<BillAlertResult>>>
        GetAlerts(
            [FromQuery] bool includeDismissed = false,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] int take = DefaultTake,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        take =
            Math.Clamp(
                take,
                1,
                MaximumTake);

        var query =
            _dbContext.BillAlerts
                .AsNoTracking()
                .Where(
                    alert =>
                        alert.UserId ==
                            userId);

        if (!includeDismissed)
        {
            query =
                query.Where(
                    alert =>
                        !alert.IsDismissed);
        }

        if (unreadOnly)
        {
            query =
                query.Where(
                    alert =>
                        !alert.IsRead);
        }

        var alerts =
            await query
                .OrderByDescending(
                    alert =>
                        alert.CreatedAtUtc)
                .ThenByDescending(
                    alert =>
                        alert.Id)
                .Take(
                    take)
                .Select(
                    alert =>
                        new BillAlertResult(
                            alert.Id,
                            alert.BillStreamId,
                            alert.BillChangeId,
                            alert.AlertType.ToString(),
                            alert.Severity.ToString(),
                            alert.Title,
                            alert.Message,
                            alert.IsRead,
                            alert.IsDismissed,
                            alert.CreatedAtUtc,
                            alert.UpdatedAtUtc))
                .ToListAsync(
                    cancellationToken);

        return Ok(
            alerts);
    }

    [HttpPost("{alertId:guid}/read")]
    public async Task<IActionResult>
        MarkRead(
            Guid alertId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        if (alertId ==
            Guid.Empty)
        {
            return NotFound();
        }

        var alert =
            await _dbContext.BillAlerts
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.Id ==
                            alertId &&
                        candidate.UserId ==
                            userId,
                    cancellationToken);

        /*
         * Do not reveal whether an alert exists for another user.
         */
        if (alert is null)
        {
            return NotFound();
        }

        if (alert.IsRead)
        {
            return NoContent();
        }

        alert.IsRead =
            true;

        alert.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{alertId:guid}/dismiss")]
    public async Task<IActionResult>
        Dismiss(
            Guid alertId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        if (alertId ==
            Guid.Empty)
        {
            return NotFound();
        }

        var alert =
            await _dbContext.BillAlerts
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.Id ==
                            alertId &&
                        candidate.UserId ==
                            userId,
                    cancellationToken);

        /*
         * Cross-user IDs deliberately produce the same 404 as
         * nonexistent IDs.
         */
        if (alert is null)
        {
            return NotFound();
        }

        if (alert.IsDismissed &&
            alert.IsRead)
        {
            return NoContent();
        }

        alert.IsDismissed =
            true;

        alert.IsRead =
            true;

        alert.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

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

public sealed record BillAlertResult(
    Guid Id,
    Guid? BillStreamId,
    Guid? BillChangeId,
    string AlertType,
    string Severity,
    string Title,
    string Message,
    bool IsRead,
    bool IsDismissed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);