using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class WebBffAuthenticationBoundaryTests
{
    [Theory]
    [InlineData("/bff/antiforgery")]
    [InlineData("/bff/subscription")]
    [InlineData("/bff/subscription/plans")]
    [InlineData("/bff/bill-streams")]
    [InlineData("/bff/bank-accounts")]
    [InlineData("/bff/bank-connections")]
    [InlineData("/bff/bank-transactions")]
    [InlineData("/bff/alerts")]
    [InlineData("/bff/account/export")]
    public async Task BffReads_RequireAuthenticatedSession(string route)
    {
        using var factory = new BillWatchWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
