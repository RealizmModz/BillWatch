using BillWatch.Core.Configuration;

namespace BillWatch.Tests.Configuration;

public sealed class BillWatchApiEndpointTests
{
    [Fact]
    public void Parse_AcceptsProductionHttpsOrigin()
    {
        var result =
            BillWatchApiEndpoint.Parse(
                "https://api.billwatch.example",
                allowLocalDevelopmentEndpoint: false);

        Assert.Equal(
            new Uri("https://api.billwatch.example/"),
            result);
    }

    [Fact]
    public void Parse_AllowsLoopbackOnlyForDevelopment()
    {
        var developmentResult =
            BillWatchApiEndpoint.Parse(
                "https://localhost:7243",
                allowLocalDevelopmentEndpoint: true);

        Assert.Equal(
            new Uri("https://localhost:7243/"),
            developmentResult);

        Assert.Throws<InvalidOperationException>(
            () =>
                BillWatchApiEndpoint.Parse(
                    "https://localhost:7243",
                    allowLocalDevelopmentEndpoint: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void Parse_RejectsMissingOrInvalidValue(
        string? configuredValue)
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                BillWatchApiEndpoint.Parse(
                    configuredValue,
                    allowLocalDevelopmentEndpoint: true));
    }

    [Fact]
    public void Parse_RejectsHttpEvenForDevelopment()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                BillWatchApiEndpoint.Parse(
                    "http://localhost:5189",
                    allowLocalDevelopmentEndpoint: true));
    }

    [Theory]
    [InlineData("https://user:password@api.billwatch.example/")]
    [InlineData("https://api.billwatch.example/api/")]
    [InlineData("https://api.billwatch.example/?key=value")]
    [InlineData("https://api.billwatch.example/#fragment")]
    public void Parse_RejectsAnythingBeyondOrigin(
        string configuredValue)
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                BillWatchApiEndpoint.Parse(
                    configuredValue,
                    allowLocalDevelopmentEndpoint: false));
    }

    [Theory]
    [InlineData("https://127.0.0.1:7243/")]
    [InlineData("https://10.0.2.2:7243/")]
    [InlineData("https://billwatch.local/")]
    [InlineData("https://host.docker.internal/")]
    public void Parse_RejectsLocalOrNumericReleaseHost(
        string configuredValue)
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                BillWatchApiEndpoint.Parse(
                    configuredValue,
                    allowLocalDevelopmentEndpoint: false));
    }
}
