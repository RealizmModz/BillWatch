using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffPlaidUpdateAuthenticationTests
{
    [Fact] public async Task UpdateLinkSession_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.PostAsync($"/bff/plaid/connections/{Guid.NewGuid()}/update-link-session", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
