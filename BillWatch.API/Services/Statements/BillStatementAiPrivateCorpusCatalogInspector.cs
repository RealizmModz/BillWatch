namespace BillWatch.API.Services.Statements;

/*
 * Offline-only preflight for a private statement corpus.
 *
 * Every discovered case is loaded through the bounded corpus loader, but the
 * result contains aggregate coverage only. It never calls an AI provider and
 * is intentionally not registered in the API runtime.
 */
public sealed class BillStatementAiPrivateCorpusCatalogInspector
{
    private const int MaxCorpusCases =
        1_000;

    private readonly BillStatementAiPrivateCorpusLoader _loader;

    public BillStatementAiPrivateCorpusCatalogInspector(
        BillStatementAiPrivateCorpusLoader loader)
    {
        ArgumentNullException.ThrowIfNull(
            loader);

        _loader =
            loader;
    }

    public async Task<BillStatementAiPrivateCorpusCatalogSummary> InspectAsync(
        string corpusRootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            corpusRootDirectory);

        if (!Path.IsPathFullyQualified(
                corpusRootDirectory))
        {
            throw new ArgumentException(
                "The private corpus root must be an absolute path.",
                nameof(corpusRootDirectory));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await InspectCoreAsync(
                Path.GetFullPath(
                    corpusRootDirectory),
                cancellationToken);
        }
        catch (BillStatementAiPrivateCorpusException)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus catalog could not be inspected.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus catalog could not be inspected.");
        }
    }

    private async Task<BillStatementAiPrivateCorpusCatalogSummary>
        InspectCoreAsync(
            string corpusRootDirectory,
            CancellationToken cancellationToken)
    {
        if (!Directory.Exists(
                corpusRootDirectory))
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus root directory is missing.");
        }

        var rootAttributes =
            File.GetAttributes(
                corpusRootDirectory);

        if ((rootAttributes &
                FileAttributes.ReparsePoint) !=
            0)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus root cannot be a link or reparse point.");
        }

        var caseDirectories =
            Directory.EnumerateDirectories(
                    corpusRootDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Take(
                    MaxCorpusCases +
                    1)
                .ToArray();

        if (caseDirectories.Length ==
            0)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus contains no cases.");
        }

        if (caseDirectories.Length >
            MaxCorpusCases)
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus contains more than {MaxCorpusCases} cases.");
        }

        var caseIds =
            new List<string>(
                caseDirectories.Length);

        foreach (var caseDirectory in
                 caseDirectories)
        {
            var attributes =
                File.GetAttributes(
                    caseDirectory);

            if ((attributes &
                    FileAttributes.ReparsePoint) !=
                0)
            {
                throw new BillStatementAiPrivateCorpusException(
                    "A private corpus case directory cannot be a link or reparse point.");
            }

            var caseId =
                Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(
                        caseDirectory));

            /*
             * Resolve through the existing policy before any case file read.
             * This rejects unsafe names and path traversal consistently.
             */
            BillStatementAiPrivateCorpusPathPolicy
                .ResolveStatementTextPath(
                    corpusRootDirectory,
                    caseId);

            caseIds.Add(
                caseId);
        }

        caseIds.Sort(
            StringComparer.Ordinal);

        var providerCounts =
            new Dictionary<string, long>(
                StringComparer.Ordinal);

        foreach (var caseId in
                 caseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var corpusCase =
                await _loader.LoadAsync(
                    corpusRootDirectory,
                    caseId,
                    cancellationToken);

            var providerKey =
                corpusCase.ProviderKey
                    .Trim()
                    .ToUpperInvariant();

            providerCounts[providerKey] =
                providerCounts.GetValueOrDefault(
                    providerKey) +
                1;
        }

        return new BillStatementAiPrivateCorpusCatalogSummary(
            CaseCount:
                caseIds.Count,
            DistinctProviderCount:
                providerCounts.Count,
            MinimumCasesForAnyProvider:
                providerCounts.Values.Min());
    }
}

public sealed record BillStatementAiPrivateCorpusCatalogSummary(
    long CaseCount,
    long DistinctProviderCount,
    long MinimumCasesForAnyProvider);
