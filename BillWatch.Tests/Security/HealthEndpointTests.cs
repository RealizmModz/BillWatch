using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class HealthEndpointTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public HealthEndpointTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task Liveness_IsAnonymousAndReturnsOnlyStatus()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/health/live");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "{\"status\":\"live\"}",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_VerifiesDependenciesWithoutExposingDetails()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/health/ready");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "{\"status\":\"ready\"}",
            await response.Content.ReadAsStringAsync());
    }
}
