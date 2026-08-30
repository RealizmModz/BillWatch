namespace BillWatch.API.Services.Plaid;

public sealed class PlaidOptions
{
    public const string SectionName =
        "Plaid";

    public const string SandboxEnvironment =
        "sandbox";

    public const string ProductionEnvironment =
        "production";

    private const string SandboxBaseUrl =
        "https://sandbox.plaid.com/";

    private const string ProductionBaseUrl =
        "https://production.plaid.com/";

    public string ClientId { get; set; } =
        string.Empty;

    public string Secret { get; set; } =
        string.Empty;

    public string Environment { get; set; } =
        SandboxEnvironment;

    /*
     * Never silently fall back to another Plaid environment.
     *
     * An invalid production configuration must fail closed rather than
     * accidentally directing financial requests to sandbox or another
     * unintended endpoint.
     */
    public string BaseUrl
    {
        get
        {
            var environment =
                Environment?
                    .Trim();

            if (string.Equals(
                    environment,
                    SandboxEnvironment,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SandboxBaseUrl;
            }

            if (string.Equals(
                    environment,
                    ProductionEnvironment,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ProductionBaseUrl;
            }

            throw new InvalidOperationException(
                $"Unsupported Plaid environment. Expected '{SandboxEnvironment}' or '{ProductionEnvironment}'.");
        }
    }

    public bool IsSandbox =>
        string.Equals(
            Environment?.Trim(),
            SandboxEnvironment,
            StringComparison.OrdinalIgnoreCase);

    public bool IsProduction =>
        string.Equals(
            Environment?.Trim(),
            ProductionEnvironment,
            StringComparison.OrdinalIgnoreCase);
}