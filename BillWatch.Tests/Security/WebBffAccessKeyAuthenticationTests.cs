using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffAccessKeyAuthenticationTests
{
    [Fact] public async Task Redeem_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.PostAsJsonAsync("/bff/subscription/access-keys/redeem", new { accessKey = "invalid" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
