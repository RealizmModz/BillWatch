using System.Diagnostics.Metrics;

namespace BillWatch.API.Authorization;

public sealed class SubscriptionAuthorizationTelemetry
{
    private readonly Counter<long> _denials =
        new Meter("BillWatch.Authorization")
            .CreateCounter<long>("billwatch.subscription.denials");

    public void RecordDenial(string reason)
    {
        _denials.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }
}
