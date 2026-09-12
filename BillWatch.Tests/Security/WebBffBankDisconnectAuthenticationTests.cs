using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffBankDisconnectAuthenticationTests
{
    [Fact] public async Task Disconnect_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.DeleteAsync($"/bff/bank-connections/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
