using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class AccountPasswordAuthorizationTests
{
    [Fact] public async Task ChangePassword_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/account/security/password", new { currentPassword = "x", newPassword = "y", twoFactorCode = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
