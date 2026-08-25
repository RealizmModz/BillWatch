using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bill-streams")]
[Authorize]
public sealed class BillStreamsController : ControllerBase
{
    private readonly BillWatchDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillStreamsController(
        BillWatchDbContext dbContext,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetBillStreams(
        CancellationToken cancellationToken)
    {
        var userIdText = _userManager.GetUserId(User);

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return Unauthorized();
        }

        var billStreams = await _dbContext.BillStreams
            .AsNoTracking()
            .Where(stream => stream.UserId == userId)
            .OrderBy(stream => stream.ProviderName)
            .Select(stream => new
            {
                stream.Id,
                stream.ProviderName,
                Category = stream.Category.ToString(),
                stream.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(billStreams);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBillStream(
        CreateBillStreamRequest request,
        CancellationToken cancellationToken)
    {
        var userIdText = _userManager.GetUserId(User);

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return Unauthorized();
        }

        var providerName = request.ProviderName.Trim();

        if (string.IsNullOrWhiteSpace(providerName))
        {
            return BadRequest(new
            {
                message = "Provider name is required."
            });
        }

        if (!Enum.TryParse<BillCategory>(
                request.Category,
                ignoreCase: true,
                out var category))
        {
            return BadRequest(new
            {
                message = "Bill category is invalid."
            });
        }

        var billStream = new BillStreamEntity
        {
            UserId = userId,
            ProviderName = providerName,
            Category = category
        };

        _dbContext.BillStreams.Add(billStream);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new
            {
                billStream.Id,
                billStream.ProviderName,
                Category = billStream.Category.ToString(),
                billStream.IsActive
            });
    }
}

public sealed record CreateBillStreamRequest(
    string ProviderName,
    string Category);