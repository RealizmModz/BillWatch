using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class BillAnalysisService
{
    private readonly BillStatementComparisonService _comparisonService;
    private readonly BillExplanationService _explanationService;
    private readonly BillTransactionReconciliationService _reconciliationService;

    public BillAnalysisService()
    {
        _comparisonService = new BillStatementComparisonService();
        _explanationService = new BillExplanationService();
        _reconciliationService = new BillTransactionReconciliationService();
    }

    public BillAnalysisResult Analyze(
        BillStatement previousStatement,
        BillStatement currentStatement)
    {
        ArgumentNullException.ThrowIfNull(previousStatement);
        ArgumentNullException.ThrowIfNull(currentStatement);

        var comparison = _comparisonService.Compare(
            previousStatement,
            currentStatement);

        var explanation = _explanationService.CreateExplanation(
            comparison);

        return new BillAnalysisResult(
            PreviousStatement: previousStatement,
            CurrentStatement: currentStatement,
            Comparison: comparison,
            Explanation: explanation,
            Reconciliation: null);
    }

    public BillAnalysisResult Analyze(
        BankTransaction transaction,
        BillStatement previousStatement,
        BillStatement currentStatement)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(previousStatement);
        ArgumentNullException.ThrowIfNull(currentStatement);

        var comparison = _comparisonService.Compare(
            previousStatement,
            currentStatement);

        var explanation = _explanationService.CreateExplanation(
            comparison);

        var reconciliation = _reconciliationService.Reconcile(
            transaction,
            currentStatement);

        return new BillAnalysisResult(
            PreviousStatement: previousStatement,
            CurrentStatement: currentStatement,
            Comparison: comparison,
            Explanation: explanation,
            Reconciliation: reconciliation);
    }
}

public sealed record BillAnalysisResult(
    BillStatement PreviousStatement,
    BillStatement CurrentStatement,
    BillStatementComparisonResult Comparison,
    BillExplanation Explanation,
    BillTransactionReconciliationResult? Reconciliation);