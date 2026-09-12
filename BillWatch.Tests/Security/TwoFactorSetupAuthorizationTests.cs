using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class TwoFactorSetupAuthorizationTests
{
    [Fact] public async Task Setup_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/account/security/two-factor/setup", new { currentPassword = "x", twoFactorCode = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
