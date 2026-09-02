using System.Security.Cryptography;
using System.Text;

namespace BillWatch.API.Services.Subscriptions;

public sealed class SubscriptionAccessKeyGenerator
{
    private const string Alphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int GroupCount =
        6;

    private const int GroupLength =
        4;

    private const int RandomCharacterCount =
        GroupCount * GroupLength;

    public GeneratedSubscriptionAccessKey Generate()
    {
        Span<char> randomCharacters =
            stackalloc char[RandomCharacterCount];

        for (var index = 0;
             index < randomCharacters.Length;
             index++)
        {
            randomCharacters[index] =
                Alphabet[
                    RandomNumberGenerator.GetInt32(
                        Alphabet.Length)];
        }

        var builder =
            new StringBuilder(
                3 +
                RandomCharacterCount +
                GroupCount);

        builder.Append("BW");

        for (var groupIndex = 0;
             groupIndex < GroupCount;
             groupIndex++)
        {
            builder.Append('-');

            builder.Append(
                randomCharacters.Slice(
                    groupIndex * GroupLength,
                    GroupLength));
        }

        var plaintextKey =
            builder.ToString();

        var hash =
            ComputeHash(
                plaintextKey);

        return new GeneratedSubscriptionAccessKey(
            plaintextKey,
            hash,
            plaintextKey[..7]);
    }

    public string ComputeHash(
        string plaintextKey)
    {
        var normalized =
            NormalizeAndValidate(
                plaintextKey);

        var bytes =
            Encoding.UTF8.GetBytes(
                normalized);

        var hash =
            SHA256.HashData(
                bytes);

        return Convert
            .ToHexString(
                hash)
            .ToLowerInvariant();
    }

    private static string NormalizeAndValidate(
        string plaintextKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            plaintextKey);

        if (plaintextKey.Length > 80)
        {
            throw new ArgumentException(
                "The access key format is invalid.",
                nameof(plaintextKey));
        }

        Span<char> normalizedBuffer =
            stackalloc char[2 + RandomCharacterCount];

        var normalizedLength =
            0;

        foreach (var character in
                 plaintextKey)
        {
            if (character == '-' ||
                char.IsWhiteSpace(
                    character))
            {
                continue;
            }

            if (normalizedLength >=
                normalizedBuffer.Length)
            {
                throw new ArgumentException(
                    "The access key format is invalid.",
                    nameof(plaintextKey));
            }

            normalizedBuffer[
                normalizedLength++] =
                char.ToUpperInvariant(
                    character);
        }

        if (normalizedLength !=
                normalizedBuffer.Length ||
            normalizedBuffer[0] != 'B' ||
            normalizedBuffer[1] != 'W')
        {
            throw new ArgumentException(
                "The access key format is invalid.",
                nameof(plaintextKey));
        }

        for (var index = 2;
             index < normalizedBuffer.Length;
             index++)
        {
            if (!Alphabet.Contains(
                    normalizedBuffer[index],
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The access key format is invalid.",
                    nameof(plaintextKey));
            }
        }

        return new string(
            normalizedBuffer);
    }
}

public sealed record GeneratedSubscriptionAccessKey(
    string PlaintextKey,
    string Hash,
    string DisplayPrefix);
