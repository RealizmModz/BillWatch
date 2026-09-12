using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class ExternalIdentityStatusAuthorizationTests
{
    [Fact]
    public async Task Status_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/auth/external");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_AuthenticatedUser_ReturnsOnlyLinkedProviderMetadata()
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var response = await client.GetAsync("/api/auth/external");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload.LinkedProviders);
    }

    private sealed record StatusPayload(IReadOnlyList<ProviderPayload> LinkedProviders);
    private sealed record ProviderPayload(string Provider, string DisplayName);
}
