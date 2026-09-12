using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;
namespace BillWatch.Tests.Security;
public sealed class WebBffCheckoutAuthenticationTests
{
    [Fact] public async Task Checkout_AnonymousSession_IsRejected()
    {
        using var factory = new BillWatchWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-BillWatch-Test-Anonymous", "true");
        using var response = await client.PostAsJsonAsync("/bff/subscription/checkout", new { billingInterval = "monthly" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
