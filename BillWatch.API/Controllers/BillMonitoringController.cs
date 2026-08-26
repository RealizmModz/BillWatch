using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bill-monitoring")]
[Authorize]
public sealed class BillMonitoringController : ControllerBase
{
    private readonly BillMonitoringRefreshService
        _refreshService;

    private readonly UserManager<ApplicationUser>
        _userManager;

    public BillMonitoringController(
        BillMonitoringRefreshService refreshService,
        UserManager<ApplicationUser> userManager)
    {
        _refreshService = refreshService;
        _userManager = userManager;
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _refreshService.RefreshAsync(
                userId,
                cancellationToken);

        return Ok(result);
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