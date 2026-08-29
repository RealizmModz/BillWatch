using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Statements;

public sealed class OpenAiBillStatementOptions
{
    public const string SectionName =
        "StatementAi:OpenAI";

    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } =
        "gpt-4.1-mini";

    public string Endpoint { get; set; } =
        "https://api.openai.com/v1/responses";

    public string PromptVersion { get; set; } =
        "bill-statement-extraction-v1";

    public int MaxDocumentCharacters { get; set; } =
        40_000;

    public int MaxOutputTokens { get; set; } =
        4_000;

    public int TimeoutSeconds { get; set; } =
        45;
}

public sealed class OpenAiBillStatementOptionsValidator
    : IValidateOptions<OpenAiBillStatementOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        OpenAiBillStatementOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        var failures =
            new List<string>();

        if (options.Enabled &&
            string.IsNullOrWhiteSpace(
                options.ApiKey))
        {
            failures.Add(
                "StatementAi:OpenAI:ApiKey is required when OpenAI statement extraction is enabled.");
        }

        if (string.IsNullOrWhiteSpace(
                options.Model))
        {
            failures.Add(
                "StatementAi:OpenAI:Model is required.");
        }

        if (!Uri.TryCreate(
                options.Endpoint,
                UriKind.Absolute,
                out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(
                endpoint.Host,
                "api.openai.com",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                endpoint.AbsolutePath.TrimEnd('/'),
                "/v1/responses",
                StringComparison.Ordinal))
        {
            failures.Add(
                "StatementAi:OpenAI:Endpoint must be the HTTPS OpenAI Responses endpoint.");
        }

        if (string.IsNullOrWhiteSpace(
                options.PromptVersion))
        {
            failures.Add(
                "StatementAi:OpenAI:PromptVersion is required.");
        }

        if (options.MaxDocumentCharacters is < 1_000 or > 200_000)
        {
            failures.Add(
                "StatementAi:OpenAI:MaxDocumentCharacters must be between 1,000 and 200,000.");
        }

        if (options.MaxOutputTokens is < 500 or > 16_000)
        {
            failures.Add(
                "StatementAi:OpenAI:MaxOutputTokens must be between 500 and 16,000.");
        }

        if (options.TimeoutSeconds is < 5 or > 180)
        {
            failures.Add(
                "StatementAi:OpenAI:TimeoutSeconds must be between 5 and 180.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                failures);
    }
}
