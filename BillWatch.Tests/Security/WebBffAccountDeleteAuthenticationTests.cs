using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffAccountDeleteAuthenticationTests
{
    [Fact] public async Task DeleteAccount_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.DeleteAsync("/bff/account");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
