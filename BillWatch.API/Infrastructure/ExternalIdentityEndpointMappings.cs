using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using Microsoft.AspNetCore.Identity;

namespace BillWatch.API.Infrastructure;

public static class ExternalIdentityEndpointMappings
{
    public static RouteGroupBuilder
        MapBillWatchExternalIdentityEndpoints(
            this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost(
                "/external/login",
                LoginAsync)
            .AllowAnonymous();

        group.MapPost(
                "/external/link",
                LinkAsync)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> LoginAsync(
        ExternalIdentityRequest request,
        IExternalIdentityTokenValidator tokenValidator,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken)
    {
        var externalIdentity =
            await ValidateIdentityAsync(
                request,
                tokenValidator,
                cancellationToken);

        if (externalIdentity is null)
        {
            return Results.Unauthorized();
        }

        var user =
            await userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        if (user is null ||
            !user.IsActive ||
            await userManager.IsLockedOutAsync(user))
        {
            /*
             * Do not reveal whether an external identity is linked to a
             * BillWatch account.
             */
            return Results.Unauthorized();
        }

        user.LastLoginAtUtc =
            DateTimeOffset.UtcNow;

        var updateResult =
            await userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            return Results.Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in is temporarily unavailable.");
        }

        /*
         * BillWatch's API remains bearer-token based. Setting the Identity
         * bearer scheme here makes SignInManager emit the same access/refresh
         * token response shape used by MapIdentityApi's password login.
         */
        signInManager.AuthenticationScheme =
            IdentityConstants.BearerScheme;

        await signInManager.SignInAsync(
            user,
            isPersistent:
                false);

        return Results.Empty;
    }

    private static async Task<IResult> LinkAsync(
        HttpContext httpContext,
        ExternalIdentityRequest request,
        IExternalIdentityTokenValidator tokenValidator,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var user =
            await userManager.GetUserAsync(
                httpContext.User);

        if (user is null ||
            !user.IsActive)
        {
            return Results.Unauthorized();
        }

        var externalIdentity =
            await ValidateIdentityAsync(
                request,
                tokenValidator,
                cancellationToken);

        if (externalIdentity is null)
        {
            return Results.BadRequest(
                new
                {
                    error =
                        "ExternalIdentityInvalid"
                });
        }

        var existingOwner =
            await userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        if (existingOwner is not null &&
            existingOwner.Id != user.Id)
        {
            return Results.Conflict(
                new
                {
                    error =
                        "ExternalIdentityAlreadyLinked"
                });
        }

        var currentLogins =
            await userManager.GetLoginsAsync(user);

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
                return Results.NoContent();
            }

            return Results.Conflict(
                new
                {
                    error =
                        "ExternalProviderAlreadyLinked"
                });
        }

        var addLoginResult =
            await userManager.AddLoginAsync(
                user,
                new UserLoginInfo(
                    externalIdentity.Provider,
                    externalIdentity.Subject,
                    GetProviderDisplayName(
                        externalIdentity.Provider)));

        if (!addLoginResult.Succeeded)
        {
            return Results.Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in could not be linked.");
        }

        return Results.NoContent();
    }

    private static async Task<ExternalIdentity?>
        ValidateIdentityAsync(
            ExternalIdentityRequest request,
            IExternalIdentityTokenValidator tokenValidator,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                request.Provider) ||
            string.IsNullOrWhiteSpace(
                request.IdToken))
        {
            return null;
        }

        try
        {
            return await tokenValidator.ValidateAsync(
                request.Provider,
                request.IdToken,
                cancellationToken);
        }
        catch (ExternalIdentityProviderUnavailableException)
        {
            return null;
        }
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

public sealed record ExternalIdentityRequest(
    string Provider,
    string IdToken);
