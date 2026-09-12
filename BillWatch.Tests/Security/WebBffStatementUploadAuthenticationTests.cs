using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffStatementUploadAuthenticationTests
{
    [Fact] public async Task Upload_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.PostAsync($"/bff/bill-streams/{Guid.NewGuid()}/statement-uploads", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
