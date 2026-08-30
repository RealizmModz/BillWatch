using BillWatch.API.Services.Statements;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiShadowOptionsTests
{
    [Fact]
    public void Defaults_FailClosed()
    {
        var options =
            new BillStatementAiShadowOptions();

        var decision =
            CreatePolicy(
                    options)
                .Evaluate(
                    providerEnabled:
                        true);

        Assert.False(
            options.Enabled);

        Assert.False(
            options.AllowProviderCalls);

        Assert.False(
            decision.MayAttemptProvider);

        Assert.False(
            decision.MayInfluencePersistence);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ProviderAttempt_RequiresEveryExplicitGate(
        bool shadowEnabled,
        bool allowProviderCalls,
        bool providerEnabled)
    {
        var decision =
            CreatePolicy(
                    new BillStatementAiShadowOptions
                    {
                        Enabled =
                            shadowEnabled,
                        AllowProviderCalls =
                            allowProviderCalls
                    })
                .Evaluate(
                    providerEnabled);

        Assert.False(
            decision.MayAttemptProvider);

        Assert.False(
            decision.MayInfluencePersistence);
    }

    [Fact]
    public void ProviderAttempt_IsAllowedOnlyWhenAllGatesAreEnabled()
    {
        var decision =
            CreatePolicy(
                    new BillStatementAiShadowOptions
                    {
                        Enabled =
                            true,
                        AllowProviderCalls =
                            true
                    })
                .Evaluate(
                    providerEnabled:
                        true);

        Assert.True(
            decision.MayAttemptProvider);

        Assert.False(
            decision.MayInfluencePersistence);

        Assert.Empty(
            decision.Failures);
    }

    [Fact]
    public void ProviderCallsCannotBeEnabledWhileShadowModeIsDisabled()
    {
        var result =
            new BillStatementAiShadowOptionsValidator()
                .Validate(
                    name:
                        null,
                    new BillStatementAiShadowOptions
                    {
                        Enabled =
                            false,
                        AllowProviderCalls =
                            true
                    });

        Assert.True(
            result.Failed);
    }

    private static BillStatementAiShadowActivationPolicy CreatePolicy(
        BillStatementAiShadowOptions options)
    {
        return new BillStatementAiShadowActivationPolicy(
            Options.Create(
                options));
    }
}
