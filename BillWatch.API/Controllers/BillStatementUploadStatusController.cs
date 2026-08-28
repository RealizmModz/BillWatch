using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Authorize]
[Route(
    "api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}")]
public sealed class BillStatementUploadStatusController
    : ControllerBase
{
    private readonly BillWatchDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillStatementUploadStatusController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext =
            dbContext;

        _userManager =
            userManager;
    }

    [HttpGet]
    public async Task<ActionResult<BillStatementUploadStatusResult>>
        GetStatus(
            Guid billStreamId,
            Guid uploadId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(
                out var userId))
        {
            return Unauthorized();
        }

        if (billStreamId ==
                Guid.Empty ||
            uploadId ==
                Guid.Empty)
        {
            return NotFound();
        }

        var result =
            await _dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(
                    upload =>
                        upload.Id ==
                            uploadId &&
                        upload.BillStreamId ==
                            billStreamId &&
                        upload.UserId ==
                            userId)
                .Select(
                    upload =>
                        new BillStatementUploadStatusResult(
                            upload.Id,
                            upload.BillStreamId,
                            upload.Status.ToString(),
                            upload.CreatedAtUtc,
                            upload.UpdatedAtUtc))
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(
            result);
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

public sealed record BillStatementUploadStatusResult(
    Guid Id,
    Guid BillStreamId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);