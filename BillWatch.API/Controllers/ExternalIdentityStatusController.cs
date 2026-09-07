using BillWatch.API.Authorization;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillWatch.API.Controllers;

[ApiController]
[Route("api/auth/external")]
[Authorize]
[EnableRateLimiting("authentication")]
[SubscriptionAccessExempt]
public sealed class ExternalIdentityStatusController(
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null ||
            !user.IsActive ||
            await userManager.IsLockedOutAsync(user))
        {
            return Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user);

        var linkedProviders = logins
            .Select(login => login.LoginProvider)
            .Where(IsSupportedProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .Select(provider => new ExternalIdentityStatusItem(
                Provider: provider,
                DisplayName: GetProviderDisplayName(provider)))
            .ToArray();

        return Ok(new ExternalIdentityStatusResponse(linkedProviders));
    }

    private static bool IsSupportedProvider(string provider)
    {
        return provider is
            ExternalIdentityProviders.Google or
            ExternalIdentityProviders.Apple or
            ExternalIdentityProviders.Microsoft;
    }

    private static string GetProviderDisplayName(string provider)
    {
        return provider switch
        {
            ExternalIdentityProviders.Google => "Google",
            ExternalIdentityProviders.Apple => "Apple",
            ExternalIdentityProviders.Microsoft => "Microsoft",
            _ => "External provider"
        };
    }
}

public sealed record ExternalIdentityStatusResponse(
    IReadOnlyList<ExternalIdentityStatusItem> LinkedProviders);

public sealed record ExternalIdentityStatusItem(
    string Provider,
    string DisplayName);
