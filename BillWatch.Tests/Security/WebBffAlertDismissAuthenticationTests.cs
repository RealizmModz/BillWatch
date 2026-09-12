using System.Net;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffAlertDismissAuthenticationTests
{
    [Fact] public async Task Dismiss_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.PostAsync($"/bff/alerts/{Guid.NewGuid()}/dismiss", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
