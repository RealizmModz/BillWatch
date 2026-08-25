namespace BillWatch.API.Services.Plaid;

public sealed class PlaidOptions
{
    public const string SectionName = "Plaid";

    public string ClientId { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string Environment { get; set; } = "sandbox";

    public string BaseUrl =>
        Environment.ToLowerInvariant() switch
        {
            "production" => "https://production.plaid.com",
            _ => "https://sandbox.plaid.com"
        };
}