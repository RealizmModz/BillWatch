namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// In-memory FullWorth-owned weights for the slow reference decoder.
/// This type is for architecture correctness and small experiments, not the
/// production serving path.
/// </summary>
public sealed class FullWorthReferenceModelWeights
{
    private readonly IReadOnlyDictionary<string, float[]> _tensors;

    private FullWorthReferenceModelWeights(
        string architectureCompatibilityId,
        string initializationId,
        IReadOnlyDictionary<string, float[]> tensors)
    {
        ArchitectureCompatibilityId =
            architectureCompatibilityId;

        InitializationId =
            initializationId;

        _tensors =
            tensors;
    }

    public string ArchitectureCompatibilityId { get; }

    public string InitializationId { get; }

    public static FullWorthReferenceModelWeights CreateRandom(
        FullWorthLanguageModelArchitecture architecture,
        FullWorthModelInitializationPlan initializationPlan)
    {
        ArgumentNullException.ThrowIfNull(architecture);
        ArgumentNullException.ThrowIfNull(initializationPlan);

        if (!string.Equals(
            architecture.CompatibilityId,
            initializationPlan.ArchitectureCompatibilityId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The initialization plan does not match the model architecture.",
                nameof(initializationPlan));
        }

        FullWorthModelParameterLayout layout =
            FullWorthModelParameterLayout.Create(
                architecture);

        var tensors =
            new Dictionary<string, float[]>(
                layout.Tensors.Count,
                StringComparer.Ordinal);

        foreach (FullWorthModelParameterTensor tensor in layout.Tensors)
        {
            if (tensor.ElementCount > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "The CPU reference decoder cannot allocate a tensor larger than Int32.MaxValue elements.");
            }

            var values =
                new float[(int)tensor.ElementCount];

            FullWorthReferenceRandomInitializer.Fill(
                values,
                initializationPlan.CreateSpec(tensor));

            tensors.Add(
                tensor.Name,
                values);
        }

        return new FullWorthReferenceModelWeights(
            architecture.CompatibilityId,
            initializationPlan.InitializationId,
            tensors);
    }

    internal float[] GetTensor(
        string name)
    {
        if (!_tensors.TryGetValue(
            name,
            out float[]? tensor))
        {
            throw new InvalidOperationException(
                $"Required FullWorth model tensor is missing: {name}");
        }

        return tensor;
    }
}

/// <summary>
/// Slow, dependency-free decoder-only reference implementation.
/// It exists as a correctness oracle for future GPU kernels and is not
/// registered in the production inference path.
/// </summary>
public sealed class FullWorthReferenceDecoder
{
    public const float RmsNormEpsilon =
        1e-5f;

    private readonly FullWorthLanguageModelArchitecture _architecture;
    private readonly FullWorthReferenceModelWeights _weights;

    public FullWorthReferenceDecoder(
        FullWorthLanguageModelArchitecture architecture,
        FullWorthReferenceModelWeights weights)
    {
        _architecture =
            architecture ??
            throw new ArgumentNullException(nameof(architecture));

        _weights =
            weights ??
            throw new ArgumentNullException(nameof(weights));

        if (!string.Equals(
            architecture.CompatibilityId,
            weights.ArchitectureCompatibilityId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The reference weights do not match the model architecture.",
                nameof(weights));
        }
    }

    /// <summary>
    /// Returns logits for the token after the supplied non-padded prefix.
    /// Randomly initialized weights produce meaningless predictions until
    /// training; this method proves the model math, not model quality.
    /// </summary>
    public float[] GetNextTokenLogits(
        ReadOnlySpan<int> tokens)
    {
        IReadOnlyList<float[]> logits =
            GetTokenLogits(tokens);

        return logits[^1];
    }

    /// <summary>
    /// Returns vocabulary logits at every prefix position. This wider surface
    /// exists so tests can prove the causal mask: appending future tokens must
    /// not change logits for already-computed prefix positions.
    /// </summary>
    public IReadOnlyList<float[]> GetTokenLogits(
        ReadOnlySpan<int> tokens)
    {
        ValidateTokens(tokens);

        int sequenceLength =
            tokens.Length;

        int hiddenSize =
            _architecture.HiddenSize;

        float[] tokenEmbedding =
            _weights.GetTensor(
                "token_embedding.weight");

        var hidden =
            new float[
                checked(
                    sequenceLength *
                    hiddenSize)];

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            int token =
                tokens[position];

            int embeddingOffset =
                checked(
                    token *
                    hiddenSize);

            Array.Copy(
                tokenEmbedding,
                embeddingOffset,
                hidden,
                position * hiddenSize,
                hiddenSize);
        }

        int keyValueWidth =
            checked(
                _architecture.HeadSize *
                _architecture.KeyValueHeadCount);

        for (int layer = 0;
             layer < _architecture.LayerCount;
             layer++)
        {
            string prefix =
                $"layers.{layer}";

            float[] attentionInput =
                RmsNorm(
                    hidden,
                    _weights.GetTensor(
                        $"{prefix}.attention_norm.weight"),
                    sequenceLength,
                    hiddenSize);

            float[] query =
                Linear(
                    attentionInput,
                    sequenceLength,
                    hiddenSize,
                    _weights.GetTensor(
                        $"{prefix}.attention.q_proj.weight"),
                    hiddenSize);

            float[] key =
                Linear(
                    attentionInput,
                    sequenceLength,
                    hiddenSize,
                    _weights.GetTensor(
                        $"{prefix}.attention.k_proj.weight"),
                    keyValueWidth);

            float[] value =
                Linear(
                    attentionInput,
                    sequenceLength,
                    hiddenSize,
                    _weights.GetTensor(
                        $"{prefix}.attention.v_proj.weight"),
                    keyValueWidth);

            ApplyRotaryPositionEncoding(
                query,
                sequenceLength,
                _architecture.AttentionHeadCount,
                _architecture.HeadSize,
                _architecture.RotaryTheta);

            ApplyRotaryPositionEncoding(
                key,
                sequenceLength,
                _architecture.KeyValueHeadCount,
                _architecture.HeadSize,
                _architecture.RotaryTheta);

            float[] attended =
                CausalGroupedQueryAttention(
                    query,
                    key,
                    value,
                    sequenceLength,
                    _architecture.AttentionHeadCount,
                    _architecture.KeyValueHeadCount,
                    _architecture.HeadSize);

            float[] attentionOutput =
                Linear(
                    attended,
                    sequenceLength,
                    hiddenSize,
                    _weights.GetTensor(
                        $"{prefix}.attention.o_proj.weight"),
                    hiddenSize);

            AddInPlace(
                hidden,
                attentionOutput);

            float[] feedForwardInput =
                RmsNorm(
                    hidden,
                    _weights.GetTensor(
                        $"{prefix}.post_attention_norm.weight"),
                    sequenceLength,
                    hiddenSize);

            float[] gate =
                Linear(
                    feedForwardInput,
                    sequenceLength,
                    hiddenSize,
                    _weights.GetTensor(
                        $"{prefix}.ffn.gate_proj.weight"),
                    _architecture.FeedForwardSize);

            float[] up =
                Linear(
                    feedForwardInput,
                    sequenceLength,
                    hiddenSize,
                    _weights.GetTensor(
                        $"{prefix}.ffn.up_proj.weight"),
                    _architecture.FeedForwardSize);

            for (int index = 0;
                 index < gate.Length;
                 index++)
            {
                gate[index] =
                    Silu(gate[index]) *
                    up[index];
            }

            float[] feedForwardOutput =
                Linear(
                    gate,
                    sequenceLength,
                    _architecture.FeedForwardSize,
                    _weights.GetTensor(
                        $"{prefix}.ffn.down_proj.weight"),
                    hiddenSize);

            AddInPlace(
                hidden,
                feedForwardOutput);
        }

        float[] normalized =
            RmsNorm(
                hidden,
                _weights.GetTensor(
                    "final_norm.weight"),
                sequenceLength,
                hiddenSize);

        var logitsByPosition =
            new float[sequenceLength][];

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            int hiddenOffset =
                checked(
                    position *
                    hiddenSize);

            var logits =
                new float[
                    _architecture.VocabularySize];

            for (int token = 0;
                 token < logits.Length;
                 token++)
            {
                int embeddingOffset =
                    checked(
                        token *
                        hiddenSize);

                float sum = 0f;

                for (int feature = 0;
                     feature < hiddenSize;
                     feature++)
                {
                    sum +=
                        normalized[
                            hiddenOffset +
                            feature] *
                        tokenEmbedding[
                            embeddingOffset +
                            feature];
                }

                logits[token] =
                    sum;
            }

            logitsByPosition[position] =
                logits;
        }

        return logitsByPosition;
    }

    private void ValidateTokens(
        ReadOnlySpan<int> tokens)
    {
        if (tokens.Length == 0 ||
            tokens.Length > _architecture.ContextLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tokens),
                "The reference decoder requires a non-empty prefix within the configured context length.");
        }

        foreach (int token in tokens)
        {
            if (token <=
                    FullWorthByteTokenizer.PaddingToken ||
                token >=
                    _architecture.VocabularySize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tokens),
                    "The reference decoder accepts only non-padding tokens from the configured vocabulary.");
            }
        }
    }

    private static float[] RmsNorm(
        float[] input,
        float[] scale,
        int rows,
        int width)
    {
        if (scale.Length != width)
            throw new InvalidOperationException(
                "RMSNorm scale shape does not match the hidden width.");

        var output =
            new float[input.Length];

        for (int row = 0;
             row < rows;
             row++)
        {
            int offset =
                row * width;

            double sumSquares = 0d;

            for (int column = 0;
                 column < width;
                 column++)
            {
                float value =
                    input[offset + column];

                sumSquares +=
                    (double)value *
                    value;
            }

            float inverseRootMeanSquare =
                (float)(
                    1d /
                    Math.Sqrt(
                        (sumSquares / width) +
                        RmsNormEpsilon));

            for (int column = 0;
                 column < width;
                 column++)
            {
                output[offset + column] =
                    input[offset + column] *
                    inverseRootMeanSquare *
                    scale[column];
            }
        }

        return output;
    }

    private static float[] Linear(
        float[] input,
        int rows,
        int inputWidth,
        float[] weight,
        int outputWidth)
    {
        if (input.Length !=
                checked(
                    rows *
                    inputWidth) ||
            weight.Length !=
                checked(
                    outputWidth *
                    inputWidth))
        {
            throw new InvalidOperationException(
                "Linear tensor shape does not match the requested projection.");
        }

        var output =
            new float[
                checked(
                    rows *
                    outputWidth)];

        for (int row = 0;
             row < rows;
             row++)
        {
            int inputOffset =
                row * inputWidth;

            int outputOffset =
                row * outputWidth;

            for (int outputFeature = 0;
                 outputFeature < outputWidth;
                 outputFeature++)
            {
                int weightOffset =
                    outputFeature *
                    inputWidth;

                float sum = 0f;

                for (int inputFeature = 0;
                     inputFeature < inputWidth;
                     inputFeature++)
                {
                    sum +=
                        input[
                            inputOffset +
                            inputFeature] *
                        weight[
                            weightOffset +
                            inputFeature];
                }

                output[
                    outputOffset +
                    outputFeature] =
                    sum;
            }
        }

        return output;
    }

    private static void ApplyRotaryPositionEncoding(
        float[] values,
        int sequenceLength,
        int headCount,
        int headSize,
        double theta)
    {
        int rowWidth =
            checked(
                headCount *
                headSize);

        if (values.Length !=
            checked(
                sequenceLength *
                rowWidth))
        {
            throw new InvalidOperationException(
                "Rotary position tensor shape does not match the requested heads.");
        }

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            int rowOffset =
                position *
                rowWidth;

            for (int head = 0;
                 head < headCount;
                 head++)
            {
                int headOffset =
                    rowOffset +
                    (head * headSize);

                for (int pair = 0;
                     pair < headSize;
                     pair += 2)
                {
                    double inverseFrequency =
                        1d /
                        Math.Pow(
                            theta,
                            (double)pair /
                            headSize);

                    double angle =
                        position *
                        inverseFrequency;

                    float cosine =
                        (float)Math.Cos(angle);

                    float sine =
                        (float)Math.Sin(angle);

                    int firstIndex =
                        headOffset +
                        pair;

                    int secondIndex =
                        firstIndex + 1;

                    float first =
                        values[firstIndex];

                    float second =
                        values[secondIndex];

                    values[firstIndex] =
                        (first * cosine) -
                        (second * sine);

                    values[secondIndex] =
                        (first * sine) +
                        (second * cosine);
                }
            }
        }
    }

    private static float[] CausalGroupedQueryAttention(
        float[] query,
        float[] key,
        float[] value,
        int sequenceLength,
        int attentionHeadCount,
        int keyValueHeadCount,
        int headSize)
    {
        int queryWidth =
            checked(
                attentionHeadCount *
                headSize);

        int keyValueWidth =
            checked(
                keyValueHeadCount *
                headSize);

        if (query.Length !=
                checked(
                    sequenceLength *
                    queryWidth) ||
            key.Length !=
                checked(
                    sequenceLength *
                    keyValueWidth) ||
            value.Length !=
                checked(
                    sequenceLength *
                    keyValueWidth))
        {
            throw new InvalidOperationException(
                "Attention tensor shape does not match the configured heads.");
        }

        int queryHeadsPerKeyValueHead =
            attentionHeadCount /
            keyValueHeadCount;

        float scale =
            1f /
            MathF.Sqrt(headSize);

        var output =
            new float[query.Length];

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            int queryRowOffset =
                position *
                queryWidth;

            for (int queryHead = 0;
                 queryHead < attentionHeadCount;
                 queryHead++)
            {
                int keyValueHead =
                    queryHead /
                    queryHeadsPerKeyValueHead;

                int queryHeadOffset =
                    queryRowOffset +
                    (queryHead * headSize);

                var scores =
                    new float[position + 1];

                float maximum =
                    float.NegativeInfinity;

                for (int sourcePosition = 0;
                     sourcePosition <= position;
                     sourcePosition++)
                {
                    int keyHeadOffset =
                        (sourcePosition *
                            keyValueWidth) +
                        (keyValueHead *
                            headSize);

                    float dot = 0f;

                    for (int feature = 0;
                         feature < headSize;
                         feature++)
                    {
                        dot +=
                            query[
                                queryHeadOffset +
                                feature] *
                            key[
                                keyHeadOffset +
                                feature];
                    }

                    float score =
                        dot *
                        scale;

                    scores[sourcePosition] =
                        score;

                    maximum =
                        MathF.Max(
                            maximum,
                            score);
                }

                double denominator = 0d;

                for (int sourcePosition = 0;
                     sourcePosition < scores.Length;
                     sourcePosition++)
                {
                    float exponential =
                        (float)Math.Exp(
                            scores[sourcePosition] -
                            maximum);

                    scores[sourcePosition] =
                        exponential;

                    denominator +=
                        exponential;
                }

                int outputHeadOffset =
                    queryRowOffset +
                    (queryHead * headSize);

                for (int sourcePosition = 0;
                     sourcePosition < scores.Length;
                     sourcePosition++)
                {
                    float probability =
                        (float)(
                            scores[sourcePosition] /
                            denominator);

                    int valueHeadOffset =
                        (sourcePosition *
                            keyValueWidth) +
                        (keyValueHead *
                            headSize);

                    for (int feature = 0;
                         feature < headSize;
                         feature++)
                    {
                        output[
                            outputHeadOffset +
                            feature] +=
                            probability *
                            value[
                                valueHeadOffset +
                                feature];
                    }
                }
            }
        }

        return output;
    }

    private static void AddInPlace(
        float[] destination,
        float[] addition)
    {
        if (destination.Length !=
            addition.Length)
        {
            throw new InvalidOperationException(
                "Residual tensors must have identical shapes.");
        }

        for (int index = 0;
             index < destination.Length;
             index++)
        {
            destination[index] +=
                addition[index];
        }
    }

    private static float Silu(
        float value)
    {
        return
            value /
            (1f +
                (float)Math.Exp(-value));
    }
}
