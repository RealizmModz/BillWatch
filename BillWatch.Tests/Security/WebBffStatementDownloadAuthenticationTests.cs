using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffStatementDownloadAuthenticationTests
{
    [Fact] public async Task Download_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.GetAsync($"/bff/bill-streams/{Guid.NewGuid()}/statement-uploads/{Guid.NewGuid()}/file");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
