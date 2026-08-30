using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Statements;

public sealed class BillStatementAiShadowOptions
{
    public const string SectionName =
        "StatementAi:Shadow";

    /*
     * Both switches default to false and must be deliberately enabled.
     * Neither switch permits AI facts to influence persistence.
     */
    public bool Enabled { get; set; }

    public bool AllowProviderCalls { get; set; }
}

public sealed class BillStatementAiShadowOptionsValidator
    : IValidateOptions<BillStatementAiShadowOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        BillStatementAiShadowOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        if (options.AllowProviderCalls &&
            !options.Enabled)
        {
            return ValidateOptionsResult.Fail(
                "StatementAi:Shadow:AllowProviderCalls cannot be enabled while shadow mode is disabled.");
        }

        return ValidateOptionsResult.Success;
    }
}

/*
 * Fail-closed activation decision for future shadow orchestration.
 *
 * This policy never authorizes persistence. It only decides whether an
 * otherwise eligible, cost-controlled shadow provider attempt may occur.
 */
public sealed class BillStatementAiShadowActivationPolicy
{
    private readonly BillStatementAiShadowOptions _options;

    public BillStatementAiShadowActivationPolicy(
        IOptions<BillStatementAiShadowOptions> options)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        _options =
            options.Value;
    }

    public BillStatementAiShadowActivationDecision Evaluate(
        bool providerEnabled)
    {
        var failures =
            new List<string>();

        if (!_options.Enabled)
        {
            failures.Add(
                "AI statement shadow mode is disabled.");
        }

        if (!_options.AllowProviderCalls)
        {
            failures.Add(
                "AI statement shadow provider calls are disabled.");
        }

        if (!providerEnabled)
        {
            failures.Add(
                "The configured AI statement provider is disabled.");
        }

        return new BillStatementAiShadowActivationDecision(
            MayAttemptProvider:
                failures.Count ==
                0,
            Failures:
                failures.AsReadOnly());
    }
}

public sealed record BillStatementAiShadowActivationDecision(
    bool MayAttemptProvider,
    IReadOnlyList<string> Failures)
{
    public bool MayInfluencePersistence =>
        false;
}
