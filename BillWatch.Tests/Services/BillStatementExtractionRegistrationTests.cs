using BillWatch.API.Services.Statements;
using BillWatch.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Services;

public sealed class BillStatementExtractionRegistrationTests
{
    [Fact]
    public void RuntimeExtraction_RemainsDeterministicAndShadowIsInactive()
    {
        using var factory =
            new BillWatchApiFactory();

        using var scope =
            factory.Services.CreateScope();

        var extractionService =
            scope.ServiceProvider.GetRequiredService<
                IBillStatementExtractionService>();

        Assert.IsType<
            DeterministicBillStatementExtractionService>(
            extractionService);

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiShadowEvaluationService>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiShadowEvaluationCoordinator>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiEvaluationLedger>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiShadowReadinessEvaluator>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiGroundTruthScorer>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiShadowActivationPolicy>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiPrivateCorpusLoader>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiPrivateCorpusCatalogInspector>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementAiPrivateCorpusCoverageGate>());

        Assert.Null(
            scope.ServiceProvider.GetService<
                BillStatementDeterministicPrivateCorpusEvaluator>());
    }
}
