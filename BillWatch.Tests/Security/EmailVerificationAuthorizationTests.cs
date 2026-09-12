using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class EmailVerificationAuthorizationTests
{
    [Fact] public async Task ResendVerification_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsync("/api/account/security/email/verification", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
