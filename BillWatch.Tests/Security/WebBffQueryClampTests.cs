using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class WebBffQueryClampTests
{
    [Theory]
    [InlineData("/bff/bank-transactions?take=-2147483648")]
    [InlineData("/bff/bank-transactions?take=2147483647")]
    [InlineData("/bff/alerts?take=-2147483648")]
    [InlineData("/bff/alerts?take=2147483647")]
    public async Task ExtremeTakeValues_DoNotCauseServerError(string route)
    {
        using var factory = new BillWatchWebFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(route);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
