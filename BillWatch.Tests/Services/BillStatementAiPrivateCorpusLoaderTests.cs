using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiPrivateCorpusLoaderTests
{
    [Fact]
    public async Task ValidPrivateCase_LoadsIntoMemoryWithoutPathExposure()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            "case-001",
            "Total due $104.99",
            """
            {
              "providerKey": "provider-a",
              "totalAmount": 104.99,
              "billingPeriodStart": "2026-08-01",
              "billingPeriodEnd": "2026-08-31",
              "statementDate": null,
              "dueDate": null,
              "currencyCode": "usd",
              "lineItems": [
                {
                  "description": "Internet service",
                  "amount": 104.99,
                  "category": "service"
                }
              ]
            }
            """);

        var result =
            await new BillStatementAiPrivateCorpusLoader()
                .LoadAsync(
                    directory.Path,
                    "case-001");

        Assert.Equal(
            "case-001",
            result.CaseId);

        Assert.Equal(
            "provider-a",
            result.ProviderKey);

        Assert.Equal(
            "Total due $104.99",
            result.StatementText);

        Assert.Equal(
            "USD",
            result.ExpectedStatement.CurrencyCode);

        Assert.True(
            result.ExpectedStatement.IsReadyForPersistence);

        Assert.Single(
            result.ExpectedLineItems);
    }

    [Fact]
    public async Task UnknownGroundTruthProperty_IsRejectedWithSanitizedMessage()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        const string sensitiveValue =
            "sensitive-value-that-must-not-escape";

        directory.WriteCase(
            "case-001",
            "Statement text",
            $$"""
            {
              "providerKey": "provider-a",
              "totalAmount": 1.00,
              "billingPeriodStart": null,
              "billingPeriodEnd": null,
              "statementDate": null,
              "dueDate": null,
              "currencyCode": "USD",
              "lineItems": [],
              "unexpected": "{{sensitiveValue}}"
            }
            """);

        var exception =
            await Assert.ThrowsAsync<
                BillStatementAiPrivateCorpusException>(
                () =>
                    new BillStatementAiPrivateCorpusLoader()
                        .LoadAsync(
                            directory.Path,
                            "case-001"));

        Assert.DoesNotContain(
            sensitiveValue,
            exception.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            directory.Path,
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            sensitiveValue,
            exception.ToString(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            directory.Path,
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedStatementText_IsRejected()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            "case-001",
            new string(
                'x',
                200_001),
            MinimalGroundTruth());

        await Assert.ThrowsAsync<
            BillStatementAiPrivateCorpusException>(
            () =>
                new BillStatementAiPrivateCorpusLoader()
                    .LoadAsync(
                        directory.Path,
                        "case-001"));
    }

    [Fact]
    public async Task GroundTruthWithoutScoredFacts_IsRejected()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            "case-001",
            "Statement text",
            """
            {
              "providerKey": "provider-a",
              "totalAmount": null,
              "billingPeriodStart": null,
              "billingPeriodEnd": null,
              "statementDate": null,
              "dueDate": null,
              "currencyCode": null,
              "lineItems": []
            }
            """);

        await Assert.ThrowsAsync<
            BillStatementAiPrivateCorpusException>(
            () =>
                new BillStatementAiPrivateCorpusLoader()
                    .LoadAsync(
                        directory.Path,
                        "case-001"));
    }

    [Fact]
    public async Task MoneyBeyondCentPrecision_IsRejected()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            "case-001",
            "Statement text",
            """
            {
              "providerKey": "provider-a",
              "totalAmount": 1.001,
              "billingPeriodStart": null,
              "billingPeriodEnd": null,
              "statementDate": null,
              "dueDate": null,
              "currencyCode": "USD",
              "lineItems": []
            }
            """);

        await Assert.ThrowsAsync<
            BillStatementAiPrivateCorpusException>(
            () =>
                new BillStatementAiPrivateCorpusLoader()
                    .LoadAsync(
                        directory.Path,
                        "case-001"));
    }

    private static string MinimalGroundTruth()
    {
        return """
            {
              "providerKey": "provider-a",
              "totalAmount": 1.00,
              "billingPeriodStart": null,
              "billingPeriodEnd": null,
              "statementDate": null,
              "dueDate": null,
              "currencyCode": "USD",
              "lineItems": []
            }
            """;
    }

    private sealed class TemporaryCorpusDirectory
        : IDisposable
    {
        public TemporaryCorpusDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"billwatch-ai-corpus-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                Path);
        }

        public string Path { get; }

        public void WriteCase(
            string caseId,
            string statementText,
            string groundTruthJson)
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
                statementText);

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .GroundTruthFileName),
                groundTruthJson);
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
