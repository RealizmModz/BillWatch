using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class TwoFactorDisableAuthorizationTests
{
    [Fact] public async Task Disable_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/account/security/two-factor/disable", new { currentPassword = "x", twoFactorCode = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
