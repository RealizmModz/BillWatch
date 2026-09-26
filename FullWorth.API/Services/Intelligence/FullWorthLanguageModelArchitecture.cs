using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// Versioned architecture contract for FullWorth's from-scratch decoder model.
/// This describes trainable intelligence only; it grants no data access and
/// makes no financial, persistence, ownership, or security decisions.
/// </summary>
public sealed class FullWorthLanguageModelArchitecture
{
    public const string ArchitectureVersion = "fullworth-decoder-v1";

    public FullWorthLanguageModelArchitecture(
        int vocabularySize,
        int contextLength,
        int hiddenSize,
        int layerCount,
        int attentionHeadCount,
        int keyValueHeadCount,
        int feedForwardSize,
        double rotaryTheta = 10_000d)
    {
        if (vocabularySize != FullWorthByteTokenizer.VocabularySize)
            throw new ArgumentOutOfRangeException(
                nameof(vocabularySize),
                "The model vocabulary must match the versioned FullWorth tokenizer.");

        if (contextLength < 2 ||
            contextLength > FullWorthByteTokenizer.MaxUtf8Bytes + 1)
            throw new ArgumentOutOfRangeException(nameof(contextLength));

        if (hiddenSize < 64 || hiddenSize > 16_384)
            throw new ArgumentOutOfRangeException(nameof(hiddenSize));

        if (layerCount < 1 || layerCount > 128)
            throw new ArgumentOutOfRangeException(nameof(layerCount));

        if (attentionHeadCount < 1 ||
            attentionHeadCount > 128 ||
            hiddenSize % attentionHeadCount != 0)
            throw new ArgumentOutOfRangeException(nameof(attentionHeadCount));

        int headSize =
            hiddenSize / attentionHeadCount;

        if (headSize % 2 != 0)
            throw new ArgumentOutOfRangeException(
                nameof(attentionHeadCount),
                "Rotary position encoding requires an even attention head size.");

        if (keyValueHeadCount < 1 ||
            keyValueHeadCount > attentionHeadCount ||
            attentionHeadCount % keyValueHeadCount != 0)
            throw new ArgumentOutOfRangeException(nameof(keyValueHeadCount));

        if (feedForwardSize < hiddenSize ||
            feedForwardSize > checked(hiddenSize * 16))
            throw new ArgumentOutOfRangeException(nameof(feedForwardSize));

        if (!double.IsFinite(rotaryTheta) || rotaryTheta <= 1d)
            throw new ArgumentOutOfRangeException(nameof(rotaryTheta));

        VocabularySize = vocabularySize;
        ContextLength = contextLength;
        HiddenSize = hiddenSize;
        LayerCount = layerCount;
        AttentionHeadCount = attentionHeadCount;
        KeyValueHeadCount = keyValueHeadCount;
        FeedForwardSize = feedForwardSize;
        RotaryTheta = rotaryTheta;
        CompatibilityId = CreateCompatibilityId();
    }

    public string Version => ArchitectureVersion;

    public string TokenizerVersion => FullWorthByteTokenizer.Version;

    public int VocabularySize { get; }

    public int ContextLength { get; }

    public int HiddenSize { get; }

    public int LayerCount { get; }

    public int AttentionHeadCount { get; }

    public int KeyValueHeadCount { get; }

    public int FeedForwardSize { get; }

    public double RotaryTheta { get; }

    public int HeadSize => HiddenSize / AttentionHeadCount;

    /// <summary>
    /// FullWorth v1 uses causal self-attention, rotary positional encoding,
    /// RMS normalization, SwiGLU feed-forward layers, grouped-query attention,
    /// and a token-embedding-tied output projection. No pretrained component is
    /// implied by this architecture.
    /// </summary>
    public bool UsesCausalAttention => true;

    public bool UsesRotaryPositionEncoding => true;

    public bool UsesRmsNormalization => true;

    public bool UsesSwiGlu => true;

    public bool TiesOutputEmbedding => true;

    /// <summary>
    /// Stable hash of the tokenizer and tensor-shape contract. Checkpoints must
    /// carry this value and fail closed when it does not match the runtime.
    /// </summary>
    public string CompatibilityId { get; }

    /// <summary>
    /// Parameter estimate for the specified bias-free v1 architecture.
    /// Rotary embeddings are non-parametric and the LM head shares token
    /// embedding weights.
    /// </summary>
    public long EstimateParameterCount()
    {
        checked
        {
            long hidden = HiddenSize;
            long headSize = HeadSize;
            long keyValueWidth = headSize * KeyValueHeadCount;

            long tokenEmbedding =
                (long)VocabularySize * hidden;

            long attention =
                (hidden * hidden) +
                (hidden * keyValueWidth) +
                (hidden * keyValueWidth) +
                (hidden * hidden);

            long feedForward =
                3L * hidden * FeedForwardSize;

            long layerNormWeights =
                2L * hidden;

            long transformerLayers =
                LayerCount *
                (attention + feedForward + layerNormWeights);

            long finalNorm =
                hidden;

            return tokenEmbedding + transformerLayers + finalNorm;
        }
    }

    /// <summary>
    /// Bootstrap shape for the first inspectable from-scratch training run.
    /// This is deliberately not declared the final production model size.
    /// </summary>
    public static FullWorthLanguageModelArchitecture CreateBootstrap() =>
        new(
            vocabularySize: FullWorthByteTokenizer.VocabularySize,
            contextLength: 1_024,
            hiddenSize: 512,
            layerCount: 8,
            attentionHeadCount: 8,
            keyValueHeadCount: 4,
            feedForwardSize: 1_536);

    private string CreateCompatibilityId()
    {
        string descriptor = string.Join(
            "|",
            ArchitectureVersion,
            FullWorthByteTokenizer.Version,
            VocabularySize.ToString(CultureInfo.InvariantCulture),
            ContextLength.ToString(CultureInfo.InvariantCulture),
            HiddenSize.ToString(CultureInfo.InvariantCulture),
            LayerCount.ToString(CultureInfo.InvariantCulture),
            AttentionHeadCount.ToString(CultureInfo.InvariantCulture),
            KeyValueHeadCount.ToString(CultureInfo.InvariantCulture),
            FeedForwardSize.ToString(CultureInfo.InvariantCulture),
            RotaryTheta.ToString("R", CultureInfo.InvariantCulture),
            "causal",
            "rope",
            "rmsnorm",
            "swiglu",
            "gqa",
            "tied-lm-head");

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(descriptor));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
