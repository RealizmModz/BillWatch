using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillMonitoringAuthorizationTests
{
    [Fact]
    public async Task Refresh_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/api/bill-monitoring/refresh",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
