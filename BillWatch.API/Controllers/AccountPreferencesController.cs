using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/account/preferences")]
[Authorize]
public sealed class AccountPreferencesController(
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountPreferencesResponse>> Get(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(user));
    }

    [HttpPut]
    public async Task<ActionResult<AccountPreferencesResponse>> Update(
        UpdateAccountPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        if (!Enum.TryParse<TimestampDisplayMode>(
                request.TimestampDisplayMode,
                ignoreCase: true,
                out var displayMode) ||
            !Enum.IsDefined(displayMode))
        {
            return ValidationProblem(
                "Timestamp display mode is invalid.");
        }

        user.TimestampDisplayMode = displayMode;

        var updateResult = await userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "BillWatch could not save your timestamp preference.");
        }

        return Ok(ToResponse(user));
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userIdText = userManager.GetUserId(User);

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return null;
        }

        return await userManager.FindByIdAsync(userId.ToString());
    }

    private static AccountPreferencesResponse ToResponse(
        ApplicationUser user)
    {
        return new AccountPreferencesResponse(
            user.TimestampDisplayMode.ToString());
    }
}

public sealed record AccountPreferencesResponse(
    string TimestampDisplayMode);

public sealed record UpdateAccountPreferencesRequest(
    string TimestampDisplayMode);
