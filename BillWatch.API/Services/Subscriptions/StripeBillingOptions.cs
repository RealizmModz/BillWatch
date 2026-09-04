namespace BillWatch.API.Services.Subscriptions;

public sealed class StripeBillingOptions
{
    public const string SectionName = "StripeBilling";

    public bool Enabled { get; init; }

    public string SecretKey { get; init; } = string.Empty;

    public string WebhookSecret { get; init; } = string.Empty;

    public string MonthlyPriceId { get; init; } = string.Empty;

    public string YearlyPriceId { get; init; } = string.Empty;

    public string PublicWebBaseUrl { get; init; } = "https://billbeacon.net";

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        !string.IsNullOrWhiteSpace(WebhookSecret) &&
        !string.IsNullOrWhiteSpace(MonthlyPriceId) &&
        !string.IsNullOrWhiteSpace(YearlyPriceId) &&
        Uri.TryCreate(PublicWebBaseUrl, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public static StripeBillingOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        return new StripeBillingOptions
        {
            Enabled = section.GetValue<bool>(nameof(Enabled)),
            SecretKey = section[nameof(SecretKey)]?.Trim() ?? string.Empty,
            WebhookSecret = section[nameof(WebhookSecret)]?.Trim() ?? string.Empty,
            MonthlyPriceId = section[nameof(MonthlyPriceId)]?.Trim() ?? string.Empty,
            YearlyPriceId = section[nameof(YearlyPriceId)]?.Trim() ?? string.Empty,
            PublicWebBaseUrl = (section[nameof(PublicWebBaseUrl)] ?? "https://billbeacon.net").TrimEnd('/')
        };
    }
}
