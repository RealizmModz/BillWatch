using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Identity;

public sealed class IdentityEmailOptions
{
    public const string SectionName =
        "IdentityEmail";

    public bool Enabled { get; set; }

    public string ApiKey
    {
        get;
        set;
    } = string.Empty;

    public string FromAddress
    {
        get;
        set;
    } = "security@billbeacon.net";

    public string FromName
    {
        get;
        set;
    } = "BillWatch";

    public string PublicWebBaseUrl
    {
        get;
        set;
    } = "https://billbeacon.net";
}

public sealed class IdentityEmailOptionsValidator
    : IValidateOptions<IdentityEmailOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IdentityEmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(
                options.ApiKey))
        {
            return ValidateOptionsResult.Fail(
                "IdentityEmail:ApiKey is required when identity email delivery is enabled.");
        }

        if (string.IsNullOrWhiteSpace(
                options.FromAddress) ||
            !options.FromAddress.Contains(
                '@',
                StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "IdentityEmail:FromAddress must be a valid sender address.");
        }

        if (string.IsNullOrWhiteSpace(
                options.FromName))
        {
            return ValidateOptionsResult.Fail(
                "IdentityEmail:FromName is required when identity email delivery is enabled.");
        }

        if (!Uri.TryCreate(
                options.PublicWebBaseUrl,
                UriKind.Absolute,
                out var publicWebUri) ||
            !string.Equals(
                publicWebUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(
                publicWebUri.UserInfo))
        {
            return ValidateOptionsResult.Fail(
                "IdentityEmail:PublicWebBaseUrl must be an absolute HTTPS URL without embedded credentials.");
        }

        return ValidateOptionsResult.Success;
    }
}
