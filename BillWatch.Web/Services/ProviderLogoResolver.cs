namespace BillWatch.Web.Services;

public static class ProviderLogoResolver
{
    private static readonly IReadOnlyDictionary<string, string> ProviderDomains =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Spotify"] = "spotify.com",
            ["Netflix"] = "netflix.com",
            ["Hulu"] = "hulu.com",
            ["Disney+"] = "disneyplus.com",
            ["Disney Plus"] = "disneyplus.com",
            ["Max"] = "max.com",
            ["HBO Max"] = "max.com",
            ["YouTube"] = "youtube.com",
            ["YouTube Premium"] = "youtube.com",
            ["Amazon"] = "amazon.com",
            ["Amazon Prime"] = "amazon.com",
            ["Prime Video"] = "amazon.com",
            ["Apple"] = "apple.com",
            ["Apple Music"] = "apple.com",
            ["Apple TV+"] = "apple.com",
            ["iCloud"] = "icloud.com",
            ["Google"] = "google.com",
            ["Google One"] = "one.google.com",
            ["Microsoft"] = "microsoft.com",
            ["Microsoft 365"] = "microsoft.com",
            ["Xbox"] = "xbox.com",
            ["Adobe"] = "adobe.com",
            ["Dropbox"] = "dropbox.com",
            ["AT&T"] = "att.com",
            ["ATT"] = "att.com",
            ["Verizon"] = "verizon.com",
            ["T-Mobile"] = "t-mobile.com",
            ["T Mobile"] = "t-mobile.com",
            ["Xfinity"] = "xfinity.com",
            ["Comcast"] = "xfinity.com",
            ["Spectrum"] = "spectrum.com",
            ["Cox"] = "cox.com",
            ["DoorDash"] = "doordash.com",
            ["Uber"] = "uber.com",
            ["Uber One"] = "uber.com",
            ["Walmart"] = "walmart.com",
            ["Walmart+"] = "walmart.com",
            ["Target"] = "target.com",
            ["ChatGPT"] = "openai.com",
            ["OpenAI"] = "openai.com"
        };

    public static string? Resolve(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        if (!ProviderDomains.TryGetValue(providerName.Trim(), out var domain))
        {
            return null;
        }

        return $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz=128";
    }
}
