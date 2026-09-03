using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Identity;

public sealed class ResendIdentityEmailSender(
    HttpClient httpClient,
    IOptions<IdentityEmailOptions> options)
    : IEmailSender<ApplicationUser>
{
    private readonly IdentityEmailOptions _options =
        options.Value;

    public Task SendConfirmationLinkAsync(
        ApplicationUser user,
        string email,
        string confirmationLink)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        var publicConfirmationLink =
            BuildEmailConfirmationLink(
                confirmationLink);

        return SendSecurityEmailAsync(
            email,
            "Confirm your BillWatch email",
            "Confirm email",
            "Confirm this email address to protect your BillWatch account and enable secure account recovery.",
            publicConfirmationLink);
    }

    public Task SendPasswordResetLinkAsync(
        ApplicationUser user,
        string email,
        string resetLink)
    {
        ArgumentNullException.ThrowIfNull(user);

        return SendSecurityEmailAsync(
            email,
            "Reset your BillWatch password",
            "Reset password",
            "Use this secure link to choose a new BillWatch password. If you did not request this, you can ignore this email.",
            resetLink);
    }

    public Task SendPasswordResetCodeAsync(
        ApplicationUser user,
        string email,
        string resetCode)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        var resetLink =
            BuildPasswordResetLink(
                email,
                resetCode);

        return SendSecurityEmailAsync(
            email,
            "Reset your BillWatch password",
            "Reset password",
            "Use this secure link to choose a new BillWatch password. If you did not request this, you can ignore this email.",
            resetLink);
    }

    private async Task SendSecurityEmailAsync(
        string email,
        string subject,
        string actionText,
        string message,
        string actionUrl)
    {
        if (!_options.Enabled)
        {
            return;
        }

        EnsureConfigured();

        if (!Uri.TryCreate(
                actionUrl,
                UriKind.Absolute,
                out var actionUri) ||
            !string.Equals(
                actionUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(
                actionUri.UserInfo))
        {
            throw new InvalidOperationException(
                "Identity email action URLs must use HTTPS without embedded credentials.");
        }

        var encodedMessage =
            HtmlEncoder.Default.Encode(
                message);

        var encodedActionText =
            HtmlEncoder.Default.Encode(
                actionText);

        var encodedActionUrl =
            HtmlEncoder.Default.Encode(
                actionUri.AbsoluteUri);

        var html =
            $"""
            <!doctype html>
            <html>
            <body style="margin:0;padding:0;background:#f5f7fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;color:#18212f;">
              <div style="max-width:560px;margin:0 auto;padding:40px 20px;">
                <div style="background:#ffffff;border:1px solid #e3e8ef;border-radius:20px;padding:32px;">
                  <div style="font-size:20px;font-weight:700;margin-bottom:20px;">BillWatch</div>
                  <p style="font-size:16px;line-height:1.6;margin:0 0 24px;">{encodedMessage}</p>
                  <p style="margin:0 0 24px;">
                    <a href="{encodedActionUrl}" style="display:inline-block;background:#111827;color:#ffffff;text-decoration:none;font-weight:650;padding:12px 18px;border-radius:12px;">{encodedActionText}</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;color:#667085;margin:0;">For your security, never share password reset links, authenticator codes, or recovery codes with anyone.</p>
                </div>
              </div>
            </body>
            </html>
            """;

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "emails");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _options.ApiKey);

        request.Content =
            JsonContent.Create(
                new
                {
                    from =
                        $"{_options.FromName} <{_options.FromAddress}>",
                    to = new[]
                    {
                        email
                    },
                    subject,
                    html
                });

        using var response =
            await httpClient.SendAsync(
                request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "The identity email provider rejected the request.",
                inner: null,
                response.StatusCode);
        }
    }

    private string BuildEmailConfirmationLink(
        string generatedConfirmationLink)
    {
        if (!Uri.TryCreate(
                generatedConfirmationLink,
                UriKind.Absolute,
                out var generatedUri))
        {
            throw new InvalidOperationException(
                "Identity generated an invalid email confirmation URL.");
        }

        var query =
            QueryHelpers.ParseQuery(
                generatedUri.Query);

        var userId =
            query["userId"]
                .ToString();

        var code =
            query["code"]
                .ToString();

        if (string.IsNullOrWhiteSpace(
                userId) ||
            string.IsNullOrWhiteSpace(
                code))
        {
            throw new InvalidOperationException(
                "Identity generated an incomplete email confirmation URL.");
        }

        var relative =
            $"auth/confirm-email?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}";

        var changedEmail =
            query["changedEmail"]
                .ToString();

        if (!string.IsNullOrWhiteSpace(
                changedEmail))
        {
            relative +=
                $"&changedEmail={Uri.EscapeDataString(changedEmail)}";
        }

        return BuildPublicWebLink(
            relative);
    }

    private string BuildPasswordResetLink(
        string email,
        string resetCode)
    {
        return BuildPublicWebLink(
            $"reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(resetCode)}");
    }

    private string BuildPublicWebLink(
        string relative)
    {
        var baseUri =
            new Uri(
                EnsureTrailingSlash(
                    _options.PublicWebBaseUrl),
                UriKind.Absolute);

        return new Uri(
                baseUri,
                relative)
            .AbsoluteUri;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(
                _options.ApiKey) ||
            string.IsNullOrWhiteSpace(
                _options.FromAddress) ||
            string.IsNullOrWhiteSpace(
                _options.FromName) ||
            !Uri.TryCreate(
                _options.PublicWebBaseUrl,
                UriKind.Absolute,
                out var publicWebUri) ||
            !string.Equals(
                publicWebUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(
                publicWebUri.UserInfo))
        {
            throw new InvalidOperationException(
                "Identity email delivery is not configured securely.");
        }
    }

    private static string EnsureTrailingSlash(
        string value)
    {
        return value.EndsWith(
                "/",
                StringComparison.Ordinal)
            ? value
            : value + "/";
    }
}
