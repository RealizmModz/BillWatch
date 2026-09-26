using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthReferenceDecoderTests
{
    [Fact]
    public void Architecture_RejectsOddRotaryHeadSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength:
                    16,
                hiddenSize:
                    96,
                layerCount:
                    1,
                attentionHeadCount:
                    32,
                keyValueHeadCount:
                    16,
                feedForwardSize:
                    192));
    }

    [Fact]
    public void RandomReferenceWeights_RequireMatchingArchitecture()
    {
        FullWorthLanguageModelArchitecture first =
            CreateArchitecture(contextLength: 16);

        FullWorthLanguageModelArchitecture second =
            CreateArchitecture(contextLength: 12);

        var wrongPlan =
            new FullWorthModelInitializationPlan(
                second,
                rootSeed: 123UL);

        Assert.Throws<ArgumentException>(() =>
            FullWorthReferenceModelWeights.CreateRandom(
                first,
                wrongPlan));
    }

    [Fact]
    public void ForwardPass_IsExactlyReproducibleForSameOwnedInitialization()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        int[] prefix =
            CreatePrefix("A");

        float[] first =
            CreateDecoder(
                    architecture,
                    rootSeed: 8_675_309UL)
                .GetNextTokenLogits(prefix);

        float[] second =
            CreateDecoder(
                    architecture,
                    rootSeed: 8_675_309UL)
                .GetNextTokenLogits(prefix);

        Assert.Equal(
            FullWorthByteTokenizer.VocabularySize,
            first.Length);

        Assert.Equal(
            first,
            second);

        Assert.All(
            first,
            value =>
                Assert.True(
                    float.IsFinite(value)));

        Assert.Contains(
            first,
            value => value != 0f);
    }

    [Fact]
    public void ForwardPass_ChangesWhenOwnedRandomSeedChanges()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        int[] prefix =
            CreatePrefix("A");

        float[] first =
            CreateDecoder(
                    architecture,
                    rootSeed: 1UL)
                .GetNextTokenLogits(prefix);

        float[] second =
            CreateDecoder(
                    architecture,
                    rootSeed: 2UL)
                .GetNextTokenLogits(prefix);

        Assert.False(
            first.SequenceEqual(second));
    }

    [Fact]
    public void ForwardPass_IsCausalForExistingPrefixPositions()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        FullWorthReferenceDecoder decoder =
            CreateDecoder(
                architecture,
                rootSeed: 444UL);

        var tokenizer =
            new FullWorthByteTokenizer();

        int[] encoded =
            tokenizer.Encode("AB");

        int[] shortPrefix =
            encoded[..2];

        int[] longerPrefix =
            encoded[..3];

        IReadOnlyList<float[]> shortLogits =
            decoder.GetTokenLogits(
                shortPrefix);

        IReadOnlyList<float[]> longerLogits =
            decoder.GetTokenLogits(
                longerPrefix);

        Assert.Equal(2, shortLogits.Count);
        Assert.Equal(3, longerLogits.Count);

        Assert.Equal(
            shortLogits[0],
            longerLogits[0]);

        Assert.Equal(
            shortLogits[1],
            longerLogits[1]);

        Assert.False(
            shortLogits[1]
                .SequenceEqual(
                    longerLogits[2]));
    }

    [Fact]
    public void NextTokenLogits_AreLastPositionLogits()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        FullWorthReferenceDecoder decoder =
            CreateDecoder(
                architecture,
                rootSeed: 789UL);

        int[] prefix =
            CreatePrefix("AC");

        IReadOnlyList<float[]> all =
            decoder.GetTokenLogits(prefix);

        float[] next =
            decoder.GetNextTokenLogits(prefix);

        Assert.Equal(
            all[^1],
            next);
    }

    [Fact]
    public void ForwardPass_RejectsPaddingInvalidAndOversizedPrefixes()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture(
                contextLength: 8);

        FullWorthReferenceDecoder decoder =
            CreateDecoder(
                architecture,
                rootSeed: 11UL);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decoder.GetNextTokenLogits(
                Array.Empty<int>()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decoder.GetNextTokenLogits(
                new[]
                {
                    FullWorthByteTokenizer.BeginToken,
                    FullWorthByteTokenizer.PaddingToken
                }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decoder.GetNextTokenLogits(
                new[]
                {
                    FullWorthByteTokenizer.BeginToken,
                    FullWorthByteTokenizer.VocabularySize
                }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decoder.GetNextTokenLogits(
                Enumerable.Repeat(
                        FullWorthByteTokenizer.BeginToken,
                        architecture.ContextLength + 1)
                    .ToArray()));
    }

    private static FullWorthReferenceDecoder CreateDecoder(
        FullWorthLanguageModelArchitecture architecture,
        ulong rootSeed)
    {
        var plan =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed);

        FullWorthReferenceModelWeights weights =
            FullWorthReferenceModelWeights.CreateRandom(
                architecture,
                plan);

        return new FullWorthReferenceDecoder(
            architecture,
            weights);
    }

    private static FullWorthLanguageModelArchitecture CreateArchitecture(
        int contextLength = 16)
    {
        return new FullWorthLanguageModelArchitecture(
            vocabularySize:
                FullWorthByteTokenizer.VocabularySize,
            contextLength:
                contextLength,
            hiddenSize:
                64,
            layerCount:
                2,
            attentionHeadCount:
                4,
            keyValueHeadCount:
                2,
            feedForwardSize:
                128);
    }

    private static int[] CreatePrefix(
        string text)
    {
        int[] encoded =
            new FullWorthByteTokenizer()
                .Encode(text);

        return encoded[..^1];
    }
}
