using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly SecureBillStatementStorageService _storageService;

    public BillStatementUploadStatusController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        SecureBillStatementStorageService storageService)
    {
        _dbContext =
            dbContext;

        _userManager =
            userManager;

        _storageService =
            storageService;
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

    [HttpGet("file")]
    [EnableRateLimiting("statement-download")]
    public async Task<IActionResult> DownloadFile(
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

        var upload =
            await _dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(
                    candidate =>
                        candidate.Id == uploadId &&
                        candidate.BillStreamId == billStreamId &&
                        candidate.UserId == userId)
                .Select(
                    candidate => new
                    {
                        candidate.Id,
                        candidate.StorageKey,
                        candidate.MediaType,
                        candidate.FileExtension
                    })
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (upload is null)
        {
            return NotFound();
        }

        Stream statementFile;

        try
        {
            statementFile =
                _storageService.OpenRead(
                    userId,
                    upload.StorageKey);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }

        var downloadName =
            $"billwatch-statement-{upload.Id:N}{upload.FileExtension}";

        return File(
            statementFile,
            upload.MediaType,
            downloadName,
            enableRangeProcessing: false);
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
