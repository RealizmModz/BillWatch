using System.Net;
using System.Net.Http.Headers;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class HealthEndpointTests : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public HealthEndpointTests(BillWatchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Liveness_IsAnonymousAndReturnsOnlyStatus()
    {
        using var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "deliberately-invalid-token");

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"live\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertNoSensitiveCache(response);
    }

    [Fact]
    public async Task Readiness_VerifiesDependenciesWithoutExposingDetails()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"status\":\"ready\"}", body);
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DataProtection", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BillStatementStorage", body, StringComparison.OrdinalIgnoreCase);
        AssertNoSensitiveCache(response);
    }

    private static void AssertNoSensitiveCache(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains(cacheControl, value =>
            value.Contains("no-store", StringComparison.OrdinalIgnoreCase));
    }
}
