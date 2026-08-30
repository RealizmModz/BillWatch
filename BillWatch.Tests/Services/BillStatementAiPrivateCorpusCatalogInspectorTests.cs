using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiPrivateCorpusCatalogInspectorTests
{
    [Fact]
    public async Task ValidCatalog_ReturnsAggregateCoverageOnly()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            "provider-a-001",
            "provider-a");

        directory.WriteCase(
            "provider-a-002",
            "PROVIDER-A");

        directory.WriteCase(
            "provider-b-001",
            "provider-b");

        var summary =
            await CreateInspector()
                .InspectAsync(
                    directory.Path);

        Assert.Equal(
            3,
            summary.CaseCount);

        Assert.Equal(
            2,
            summary.DistinctProviderCount);

        Assert.Equal(
            1,
            summary.MinimumCasesForAnyProvider);

        var resultProperties =
            typeof(
                    BillStatementAiPrivateCorpusCatalogSummary)
                .GetProperties()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        Assert.DoesNotContain(
            resultProperties,
            name =>
                name.Contains(
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Path",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "ProviderKey",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnsafeCaseDirectoryName_IsRejectedBeforeFileRead()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        Directory.CreateDirectory(
            System.IO.Path.Combine(
                directory.Path,
                "unsafe case name"));

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                CreateInspector()
                    .InspectAsync(
                        directory.Path));
    }

    [Fact]
    public async Task EmptyCatalog_IsRejectedWithoutPhysicalPathInFailure()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        var exception =
            await Assert.ThrowsAsync<
                BillStatementAiPrivateCorpusException>(
                () =>
                    CreateInspector()
                        .InspectAsync(
                            directory.Path));

        Assert.DoesNotContain(
            directory.Path,
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static BillStatementAiPrivateCorpusCatalogInspector
        CreateInspector()
    {
        return new BillStatementAiPrivateCorpusCatalogInspector(
            new BillStatementAiPrivateCorpusLoader());
    }

    private sealed class TemporaryCorpusDirectory
        : IDisposable
    {
        public TemporaryCorpusDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"billwatch-ai-catalog-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                Path);
        }

        public string Path { get; }

        public void WriteCase(
            string caseId,
            string providerKey)
        {
            var caseDirectory =
                System.IO.Path.Combine(
                    Path,
                    caseId);

            Directory.CreateDirectory(
                caseDirectory);

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .StatementTextFileName),
                "Total due $1.00 USD");

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .GroundTruthFileName),
                $$"""
                {
                  "providerKey": "{{providerKey}}",
                  "totalAmount": 1.00,
                  "billingPeriodStart": null,
                  "billingPeriodEnd": null,
                  "statementDate": null,
                  "dueDate": null,
                  "currencyCode": "USD",
                  "lineItems": []
                }
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    Path))
            {
                Directory.Delete(
                    Path,
                    recursive:
                        true);
            }
        }
    }
}
