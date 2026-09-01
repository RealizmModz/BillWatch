using BillWatch.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BillWatch.Web.Infrastructure;

public static class AuthEndpointMappings
{
    public static IEndpointRouteBuilder
        MapBillWatchAuthEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        endpoints.MapPost(
            "/auth/login",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                var form =
                    await context.Request
                        .ReadFormAsync(
                            context.RequestAborted);

                var email =
                    form["email"]
                        .ToString()
                        .Trim();

                var password =
                    form["password"]
                        .ToString();

                var rememberMe =
                    string.Equals(
                        form["rememberMe"],
                        "on",
                        StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(
                        email) ||
                    string.IsNullOrWhiteSpace(
                        password))
                {
                    return Results.Redirect(
                        "/login?error=" +
                        Uri.EscapeDataString(
                            "Enter your email and password."));
                }

                var result =
                    await authenticationService
                        .LoginAsync(
                            context,
                            email,
                            password,
                            rememberMe,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/login?error=" +
                        Uri.EscapeDataString(
                            result.ErrorMessage ??
                            "Unable to sign in."));
                }

                return Results.Redirect(
                    "/app");
            });

        endpoints.MapPost(
            "/auth/register",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                var form =
                    await context.Request
                        .ReadFormAsync(
                            context.RequestAborted);

                var email =
                    form["email"]
                        .ToString()
                        .Trim();

                var password =
                    form["password"]
                        .ToString();

                var confirmPassword =
                    form["confirmPassword"]
                        .ToString();

                if (string.IsNullOrWhiteSpace(
                        email))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            "Enter an email address."));
                }

                if (string.IsNullOrWhiteSpace(
                        password))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            "Create a password."));
                }

                if (!string.Equals(
                        password,
                        confirmPassword,
                        StringComparison.Ordinal))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            "The passwords do not match."));
                }

                var result =
                    await authenticationService
                        .RegisterAsync(
                            context,
                            email,
                            password,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            result.ErrorMessage ??
                            "Unable to create your account."));
                }

                return Results.Redirect(
                    "/app");
            });

        endpoints.MapPost(
            "/auth/logout",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                await authenticationService
                    .LogoutAsync(
                        context);

                return Results.Redirect(
                    "/"); dotnet build BillWatch.Web\BillWatch.Web.csproj
            });

        return endpoints;
    }
}