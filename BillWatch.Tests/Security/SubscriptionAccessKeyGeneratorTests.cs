using BillWatch.API.Services.Subscriptions;

namespace BillWatch.Tests.Security;

public sealed class SubscriptionAccessKeyGeneratorTests
{
    private readonly SubscriptionAccessKeyGenerator _generator =
        new();

    [Fact]
    public void Generate_ReturnsExpectedOneTimeDisplayFormat()
    {
        var generated =
            _generator.Generate();

        var segments =
            generated.PlaintextKey.Split('-');

        Assert.Equal(
            7,
            segments.Length);

        Assert.Equal(
            "BW",
            segments[0]);

        Assert.All(
            segments.Skip(1),
            segment =>
                Assert.Equal(
                    4,
                    segment.Length));

        Assert.Equal(
            generated.PlaintextKey[..7],
            generated.DisplayPrefix);

        Assert.Equal(
            64,
            generated.Hash.Length);
    }

    [Fact]
    public void Generate_ProducesDifferentKeysAndHashes()
    {
        var first =
            _generator.Generate();

        var second =
            _generator.Generate();

        Assert.NotEqual(
            first.PlaintextKey,
            second.PlaintextKey);

        Assert.NotEqual(
            first.Hash,
            second.Hash);
    }

    [Fact]
    public void ComputeHash_IsStableAcrossFormatting()
    {
        var generated =
            _generator.Generate();

        var compact =
            generated.PlaintextKey
                .Replace(
                    "-",
                    string.Empty,
                    StringComparison.Ordinal)
                .ToLowerInvariant();

        var spaced =
            string.Join(
                " ",
                generated.PlaintextKey.Split('-'));

        Assert.Equal(
            generated.Hash,
            _generator.ComputeHash(
                compact));

        Assert.Equal(
            generated.Hash,
            _generator.ComputeHash(
                spaced));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-billwatch-key")]
    [InlineData("BW-0000-0000-0000-0000-0000-0000")]
    [InlineData("BW-AAAA-AAAA-AAAA-AAAA-AAAA-AAAA-extra")]
    public void ComputeHash_RejectsInvalidKeys(
        string value)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                _generator.ComputeHash(
                    value));
    }
}
