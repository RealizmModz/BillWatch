using BillWatch.API.Services.Statements;
using System.Globalization;

namespace BillWatch.Tests.Services;

public sealed class BillStatementDeterministicPrivateCorpusEvaluatorTests
{
    [Fact]
    public async Task Evaluate_ProducesAggregateDeterministicBaselineOnly()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            "complete-001",
            """
            Total Due: $104.99
            Billing Period: 08/01/2026 - 08/31/2026
            """,
            totalAmount:
                104.99m,
            billingPeriodStart:
                "2026-08-01",
            billingPeriodEnd:
                "2026-08-31");

        directory.WriteCase(
            "incomplete-001",
            "Amount payable $55.00",
            totalAmount:
                55m,
            billingPeriodStart:
                null,
            billingPeriodEnd:
                null);

        var baseline =
            await CreateEvaluator()
                .EvaluateAsync(
                    directory.Path,
                    [
                        "complete-001",
                        "incomplete-001"
                    ]);

        Assert.Equal(
            2,
            baseline.EvaluatedStatementCount);

        Assert.Equal(
            1,
            baseline.ReadyStatementCount);

        Assert.Equal(
            5,
            baseline.CorrectFactCount);

        Assert.Equal(
            0,
            baseline.IncorrectFactCount);

        Assert.Equal(
            1,
            baseline.MissedFactCount);

        Assert.Equal(
            0.5m,
            baseline.ReadyStatementRate);

        Assert.Equal(
            1m,
            baseline.FactPrecision);

        Assert.Equal(
            5m /
                6m,
            baseline.FactRecall);

        var propertyNames =
            typeof(
                    BillStatementDeterministicCorpusBaseline)
                .GetProperties()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name =>
                name.Contains(
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Path",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Provider",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "CaseId",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuplicateCaseIdentifiers_AreRejectedBeforeFileRead()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                CreateEvaluator()
                    .EvaluateAsync(
                        Path.GetTempPath(),
                        [
                            "case-001",
                            "CASE-001"
                        ]));
    }

    private static BillStatementDeterministicPrivateCorpusEvaluator
        CreateEvaluator()
    {
        return new BillStatementDeterministicPrivateCorpusEvaluator(
            new BillStatementAiPrivateCorpusLoader(),
            new DeterministicBillStatementExtractionService(
                new DeterministicBillStatementParser(),
                new DeterministicBillLineItemParser()));
    }

    private sealed class TemporaryCorpusDirectory
        : IDisposable
    {
        public TemporaryCorpusDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"billwatch-deterministic-corpus-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                Path);
        }

        public string Path { get; }

        public void WriteCase(
            string caseId,
            string statementText,
            decimal totalAmount,
            string? billingPeriodStart,
            string? billingPeriodEnd)
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
                $$"""
                {
                  "providerKey": "provider-a",
                  "totalAmount": {{totalAmount.ToString(CultureInfo.InvariantCulture)}},
                  "billingPeriodStart": {{JsonValue(billingPeriodStart)}},
                  "billingPeriodEnd": {{JsonValue(billingPeriodEnd)}},
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

        private static string JsonValue(
            string? value)
        {
            return value is null
                ? "null"
                : $"\"{value}\"";
        }
    }
}
