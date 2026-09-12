using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffStatementMetadataAuthenticationTests
{
    [Fact] public async Task Metadata_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.GetAsync($"/bff/bill-streams/{Guid.NewGuid()}/statement-uploads/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
