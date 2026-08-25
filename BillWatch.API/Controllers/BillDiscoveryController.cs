using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/bill-discovery")]
[Authorize]
public sealed class BillDiscoveryController : ControllerBase
{
    private readonly RecurringBillDiscoveryPersistenceService
        _discoveryService;

    private readonly UserManager<ApplicationUser>
        _userManager;

    public BillDiscoveryController(
        RecurringBillDiscoveryPersistenceService discoveryService,
        UserManager<ApplicationUser> userManager)
    {
        _discoveryService = discoveryService;
        _userManager = userManager;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunDiscovery(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _discoveryService.DiscoverAndSaveAsync(
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