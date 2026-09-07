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
[EnableRateLimiting("authentication")]
[SubscriptionAccessExempt]
public sealed class ExternalIdentityController : ControllerBase
{
    private readonly UserManager<ApplicationUser>
        _userManager;

    private readonly SignInManager<ApplicationUser>
        _signInManager;

    private readonly IExternalIdentityTokenValidator
        _tokenValidator;

    public ExternalIdentityController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(signInManager);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _userManager =
            userManager;

        _signInManager =
            signInManager;

        _tokenValidator =
            new ExternalIdentityTokenValidator(
                configuration,
                httpClientFactory);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] ExternalIdentityLoginRequest request,
        CancellationToken cancellationToken)
    {
        var externalIdentity =
            await ValidateIdentityAsync(
                request.Provider,
                request.IdToken,
                cancellationToken);

        if (externalIdentity is null)
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        if (user is null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(
                user))
        {
            /*
             * Keep the failure intentionally indistinguishable from an
             * invalid provider token so callers cannot enumerate linked
             * external identities.
             */
            return Unauthorized();
        }

        /*
         * External identity proof must not silently bypass BillWatch's own
         * two-factor requirement. Until the Web/BFF flow can complete the
         * local second-factor step after provider authentication, fail closed
         * for accounts that have BillWatch 2FA enabled.
         */
        if (await _userManager.GetTwoFactorEnabledAsync(
                user))
        {
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title:
                    "Additional verification required.",
                detail:
                    "RequiresTwoFactor");
        }

        user.LastLoginAtUtc =
            DateTimeOffset.UtcNow;

        var updateResult =
            await _userManager.UpdateAsync(
                user);

        if (!updateResult.Succeeded)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in is temporarily unavailable.");
        }

        /*
         * BillWatch's API authentication contract remains Identity bearer
         * tokens. Selecting the bearer scheme causes SignInManager to emit
         * the same access/refresh-token response format used by the normal
         * Identity API login endpoint.
         */
        _signInManager.AuthenticationScheme =
            IdentityConstants.BearerScheme;

        await _signInManager.SignInAsync(
            user,
            isPersistent:
                false);

        return new EmptyResult();
    }

    [HttpPost("link")]
    [Authorize]
    public async Task<IActionResult> Link(
        [FromBody] ExternalIdentityLinkRequest request,
        CancellationToken cancellationToken)
    {
        var user =
            await _userManager.GetUserAsync(
                User);

        if (user is null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(
                user))
        {
            return Unauthorized();
        }

        /*
         * Adding a new sign-in method is an account-takeover-sensitive
         * operation. An existing bearer session is not sufficient proof on
         * its own because a stolen session must not be able to permanently
         * bind an attacker's Google/Apple/Microsoft identity.
         */
        if (string.IsNullOrWhiteSpace(
                request.CurrentPassword) ||
            !await _userManager.CheckPasswordAsync(
                user,
                request.CurrentPassword))
        {
            return Unauthorized();
        }

        if (await _userManager.GetTwoFactorEnabledAsync(
                user))
        {
            if (string.IsNullOrWhiteSpace(
                    request.TwoFactorCode))
            {
                return Unauthorized();
            }

            var validTwoFactorCode =
                await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    NormalizeAuthenticatorCode(
                        request.TwoFactorCode));

            if (!validTwoFactorCode)
            {
                return Unauthorized();
            }
        }

        var externalIdentity =
            await ValidateIdentityAsync(
                request.Provider,
                request.IdToken,
                cancellationToken);

        if (externalIdentity is null)
        {
            return BadRequest(
                new
                {
                    error =
                        "ExternalIdentityInvalid"
                });
        }

        var existingOwner =
            await _userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        if (existingOwner is not null &&
            existingOwner.Id != user.Id)
        {
            return Conflict(
                new
                {
                    error =
                        "ExternalIdentityAlreadyLinked"
                });
        }

        var currentLogins =
            await _userManager.GetLoginsAsync(
                user);

        var existingProviderLogin =
            currentLogins.FirstOrDefault(
                login =>
                    string.Equals(
                        login.LoginProvider,
                        externalIdentity.Provider,
                        StringComparison.OrdinalIgnoreCase));

        if (existingProviderLogin is not null)
        {
            if (string.Equals(
                    existingProviderLogin.ProviderKey,
                    externalIdentity.Subject,
                    StringComparison.Ordinal))
            {
                return NoContent();
            }

            return Conflict(
                new
                {
                    error =
                        "ExternalProviderAlreadyLinked"
                });
        }

        var addLoginResult =
            await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(
                    externalIdentity.Provider,
                    externalIdentity.Subject,
                    GetProviderDisplayName(
                        externalIdentity.Provider)));

        if (!addLoginResult.Succeeded)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in could not be linked.");
        }

        return NoContent();
    }

    private async Task<ExternalIdentity?>
        ValidateIdentityAsync(
            string provider,
            string idToken,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                provider) ||
            string.IsNullOrWhiteSpace(
                idToken))
        {
            return null;
        }

        try
        {
            return await _tokenValidator.ValidateAsync(
                provider,
                idToken,
                cancellationToken);
        }
        catch (ExternalIdentityProviderUnavailableException)
        {
            /*
             * Provider discovery/key outages are intentionally collapsed to
             * a safe authentication failure. No upstream body, token, or
             * discovery detail is returned to the client.
             */
            return null;
        }
    }

    private static string NormalizeAuthenticatorCode(
        string code)
    {
        return code
            .Replace(
                " ",
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "-",
                string.Empty,
                StringComparison.Ordinal);
    }

    private static string GetProviderDisplayName(
        string provider)
    {
        return provider switch
        {
            ExternalIdentityProviders.Google =>
                "Google",

            ExternalIdentityProviders.Apple =>
                "Apple",

            ExternalIdentityProviders.Microsoft =>
                "Microsoft",

            _ =>
                "External provider"
        };
    }
}

public sealed record ExternalIdentityLoginRequest(
    string Provider,
    string IdToken);

public sealed record ExternalIdentityLinkRequest(
    string Provider,
    string IdToken,
    string CurrentPassword,
    string? TwoFactorCode);