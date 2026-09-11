using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class WebBffIdentifierBoundaryTests
{
    [Theory]
    [InlineData("/bff/bill-streams/00000000-0000-0000-0000-000000000000")]
    [InlineData("/bff/bill-streams/00000000-0000-0000-0000-000000000000/statement-uploads/11111111-1111-1111-1111-111111111111")]
    [InlineData("/bff/bill-streams/11111111-1111-1111-1111-111111111111/statement-uploads/00000000-0000-0000-0000-000000000000")]
    [InlineData("/bff/bill-streams/00000000-0000-0000-0000-000000000000/statement-uploads/11111111-1111-1111-1111-111111111111/file")]
    [InlineData("/bff/bill-streams/11111111-1111-1111-1111-111111111111/statement-uploads/00000000-0000-0000-0000-000000000000/file")]
    public async Task EmptyResourceIdentifier_IsRejectedBeforeProxying(string route)
    {
        using var factory = new BillWatchWebFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/bff/bill-streams/not-a-guid")]
    [InlineData("/bff/bill-streams/not-a-guid/statement-uploads/not-a-guid")]
    [InlineData("/bff/bill-streams/not-a-guid/statement-uploads/not-a-guid/file")]
    public async Task MalformedResourceIdentifier_DoesNotMatchSensitiveRoute(string route)
    {
        using var factory = new BillWatchWebFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
