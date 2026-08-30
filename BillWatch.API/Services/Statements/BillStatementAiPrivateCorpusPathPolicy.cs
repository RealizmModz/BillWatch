using System.Text.RegularExpressions;

namespace BillWatch.API.Services.Statements;

/*
 * Path boundary for a future, offline-only ground-truth corpus runner.
 *
 * Corpus data may contain sensitive financial information. This policy
 * resolves only two fixed per-case file names and refuses traversal or
 * arbitrary user-controlled paths. It performs no file reads and logs no
 * corpus data. The corpus runner itself is not part of the API runtime.
 */
public static partial class BillStatementAiPrivateCorpusPathPolicy
{
    public const string StatementTextFileName =
        "statement.txt";

    public const string GroundTruthFileName =
        "ground-truth.json";

    public static string ResolveStatementTextPath(
        string corpusRootDirectory,
        string caseId)
    {
        return ResolveCaseFilePath(
            corpusRootDirectory,
            caseId,
            StatementTextFileName);
    }

    public static string ResolveGroundTruthPath(
        string corpusRootDirectory,
        string caseId)
    {
        return ResolveCaseFilePath(
            corpusRootDirectory,
            caseId,
            GroundTruthFileName);
    }

    private static string ResolveCaseFilePath(
        string corpusRootDirectory,
        string caseId,
        string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            corpusRootDirectory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            caseId);

        if (!Path.IsPathFullyQualified(
                corpusRootDirectory))
        {
            throw new ArgumentException(
                "The private corpus root must be an absolute path.",
                nameof(corpusRootDirectory));
        }

        if (!CaseIdRegex().IsMatch(
                caseId))
        {
            throw new ArgumentException(
                "Corpus case identifiers may contain only letters, digits, hyphens, and underscores.",
                nameof(caseId));
        }

        var root =
            Path.GetFullPath(
                corpusRootDirectory);

        var caseDirectory =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    caseId));

        var rootedPrefix =
            Path.EndsInDirectorySeparator(
                    root)
                ? root
                : root +
                    Path.DirectorySeparatorChar;

        if (!caseDirectory.StartsWith(
                rootedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The corpus case path escapes the configured private corpus root.",
                nameof(caseId));
        }

        return Path.Combine(
            caseDirectory,
            fileName);
    }

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CaseIdRegex();
}
