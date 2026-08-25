using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class DashboardSummaryService
{
    public DashboardSummary CreateSummary(
        IEnumerable<BillStream> billStreams,
        IEnumerable<BillAlert> alerts)
    {
        ArgumentNullException.ThrowIfNull(billStreams);
        ArgumentNullException.ThrowIfNull(alerts);

        var streamList = billStreams.ToList();
        var alertList = alerts.ToList();

        decimal monthlyBills = decimal.Round(
            streamList.Sum(stream =>
                stream.LatestAmount ?? 0m),
            2,
            MidpointRounding.AwayFromZero);

        decimal annualBills = decimal.Round(
            monthlyBills * 12m,
            2,
            MidpointRounding.AwayFromZero);

        decimal addedAnnualCost = decimal.Round(
            alertList
                .Where(alert => alert.AnnualImpact > 0m)
                .Sum(alert => alert.AnnualImpact),
            2,
            MidpointRounding.AwayFromZero);

        decimal reducedAnnualCost = decimal.Round(
            Math.Abs(
                alertList
                    .Where(alert => alert.AnnualImpact < 0m)
                    .Sum(alert => alert.AnnualImpact)),
            2,
            MidpointRounding.AwayFromZero);

        return new DashboardSummary(
            BillsMonitored: streamList.Count,
            MonthlyBills: monthlyBills,
            AnnualBills: annualBills,
            ChangesDetected: alertList.Count,
            AddedAnnualCost: addedAnnualCost,
            ReducedAnnualCost: reducedAnnualCost);
    }
}

public sealed record DashboardSummary(
    int BillsMonitored,
    decimal MonthlyBills,
    decimal AnnualBills,
    int ChangesDetected,
    decimal AddedAnnualCost,
    decimal ReducedAnnualCost);