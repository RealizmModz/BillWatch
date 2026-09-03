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

                var twoFactorCode =
                    form["twoFactorCode"]
                        .ToString()
                        .Trim();

                var recoveryCode =
                    form["recoveryCode"]
                        .ToString()
                        .Trim();

                var isTwoFactorStep =
                    string.Equals(
                        form["twoFactor"],
                        "true",
                        StringComparison.OrdinalIgnoreCase);

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
                        BuildLoginRedirect(
                            "Enter your email and password.",
                            isTwoFactorStep));
                }

                if (isTwoFactorStep &&
                    string.IsNullOrWhiteSpace(
                        twoFactorCode) &&
                    string.IsNullOrWhiteSpace(
                        recoveryCode))
                {
                    return Results.Redirect(
                        BuildLoginRedirect(
                            "Enter an authenticator code or a recovery code.",
                            twoFactor: true));
                }

                var result =
                    await authenticationService
                        .LoginAsync(
                            context,
                            email,
                            password,
                            rememberMe,
                            twoFactorCode,
                            recoveryCode,
                            context.RequestAborted);

                if (result.RequiresTwoFactor)
                {
                    return Results.Redirect(
                        "/login?twoFactor=true");
                }

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        BuildLoginRedirect(
                            result.ErrorMessage ??
                            "Unable to sign in.",
                            isTwoFactorStep));
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
            "/auth/forgot-password",
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

                if (string.IsNullOrWhiteSpace(
                        email))
                {
                    return Results.Redirect(
                        "/forgot-password?error=" +
                        Uri.EscapeDataString(
                            "Enter your email address."));
                }

                var result =
                    await authenticationService
                        .RequestPasswordResetAsync(
                            email,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/forgot-password?error=" +
                        Uri.EscapeDataString(
                            result.ErrorMessage ??
                            "Password recovery is unavailable right now."));
                }

                return Results.Redirect(
                    "/forgot-password?sent=true");
            });

        endpoints.MapPost(
            "/auth/reset-password",
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

                var code =
                    form["code"]
                        .ToString()
                        .Trim();

                var newPassword =
                    form["newPassword"]
                        .ToString();

                var confirmPassword =
                    form["confirmPassword"]
                        .ToString();

                var returnQuery =
                    BuildResetPasswordQuery(
                        email,
                        code);

                if (string.IsNullOrWhiteSpace(
                        email) ||
                    string.IsNullOrWhiteSpace(
                        code))
                {
                    return Results.Redirect(
                        "/reset-password?error=" +
                        Uri.EscapeDataString(
                            "This password reset link is incomplete or invalid."));
                }

                if (string.IsNullOrWhiteSpace(
                        newPassword))
                {
                    return Results.Redirect(
                        "/reset-password" +
                        returnQuery +
                        "&error=" +
                        Uri.EscapeDataString(
                            "Create a new password."));
                }

                if (!string.Equals(
                        newPassword,
                        confirmPassword,
                        StringComparison.Ordinal))
                {
                    return Results.Redirect(
                        "/reset-password" +
                        returnQuery +
                        "&error=" +
                        Uri.EscapeDataString(
                            "The passwords do not match."));
                }

                var result =
                    await authenticationService
                        .ResetPasswordAsync(
                            email,
                            code,
                            newPassword,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/reset-password" +
                        returnQuery +
                        "&error=" +
                        Uri.EscapeDataString(
                            result.ErrorMessage ??
                            "Unable to reset your password."));
                }

                return Results.Redirect(
                    "/login?message=" +
                    Uri.EscapeDataString(
                        "Your password has been reset. Sign in with your new password."));
            });

        endpoints.MapGet(
            "/auth/confirm-email",
            async (
                HttpContext context,
                WebAuthenticationService authenticationService) =>
            {
                var userId =
                    context.Request.Query["userId"]
                        .ToString();

                var code =
                    context.Request.Query["code"]
                        .ToString();

                var changedEmail =
                    context.Request.Query["changedEmail"]
                        .ToString();

                if (string.IsNullOrWhiteSpace(
                        userId) ||
                    string.IsNullOrWhiteSpace(
                        code))
                {
                    return Results.Redirect(
                        "/login?error=" +
                        Uri.EscapeDataString(
                            "This email confirmation link is incomplete or invalid."));
                }

                var result =
                    await authenticationService
                        .ConfirmEmailAsync(
                            userId,
                            code,
                            changedEmail,
                            context.RequestAborted);

                return Results.Redirect(
                    result.Succeeded
                        ? "/login?message=" +
                          Uri.EscapeDataString(
                              "Your email address is confirmed.")
                        : "/login?error=" +
                          Uri.EscapeDataString(
                              result.ErrorMessage ??
                              "Unable to confirm this email address."));
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
                    "/");
            });

        return endpoints;
    }

    private static string BuildLoginRedirect(
        string error,
        bool twoFactor)
    {
        var path =
            twoFactor
                ? "/login?twoFactor=true&error="
                : "/login?error=";

        return path +
            Uri.EscapeDataString(
                error);
    }

    private static string BuildResetPasswordQuery(
        string email,
        string code)
    {
        return
            $"?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}";
    }
}
