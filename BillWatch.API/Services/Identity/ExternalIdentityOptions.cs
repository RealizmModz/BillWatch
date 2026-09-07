namespace BillWatch.API.Services.Identity;

public sealed class ExternalIdentityOptions
{
    public const string SectionName =
        "ExternalIdentity";

    public ExternalIdentityProviderOptions Google { get; set; } =
        new()
        {
            Provider = ExternalIdentityProviders.Google,
            MetadataAddress =
                "https://accounts.google.com/.well-known/openid-configuration",
            AllowedMetadataHost =
                "accounts.google.com",
            AdditionalValidIssuers =
                [
                    "accounts.google.com"
                ]
        };

    public ExternalIdentityProviderOptions Apple { get; set; } =
        new()
        {
            Provider = ExternalIdentityProviders.Apple,
            MetadataAddress =
                "https://appleid.apple.com/.well-known/openid-configuration",
            AllowedMetadataHost =
                "appleid.apple.com"
        };

    public ExternalIdentityProviderOptions Microsoft { get; set; } =
        new()
        {
            Provider = ExternalIdentityProviders.Microsoft,
            MetadataAddress =
                "https://login.microsoftonline.com/consumers/v2.0/.well-known/openid-configuration",
            AllowedMetadataHost =
                "login.microsoftonline.com"
        };

    public ExternalIdentityProviderOptions? GetProvider(
        string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            ExternalIdentityProviders.Google =>
                Google,

            ExternalIdentityProviders.Apple =>
                Apple,

            ExternalIdentityProviders.Microsoft =>
                Microsoft,

            _ =>
                null
        };
    }
}

public sealed class ExternalIdentityProviderOptions
{
    public string Provider { get; set; } =
        string.Empty;

    public string? Audience { get; set; }

    public string MetadataAddress { get; set; } =
        string.Empty;

    public string AllowedMetadataHost { get; set; } =
        string.Empty;

    public string[] AdditionalValidIssuers { get; set; } =
        [];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Audience);
}

public static class ExternalIdentityProviders
{
    public const string Google =
        "google";

    public const string Apple =
        "apple";

    public const string Microsoft =
        "microsoft";
}
