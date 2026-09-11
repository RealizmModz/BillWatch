using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class AccountEmailAuthorizationTests
{
    [Fact] public async Task ChangeEmail_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/account/security/email", new { currentPassword = "x", newEmail = "nobody@example.invalid", twoFactorCode = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
