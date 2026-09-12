using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillAlertQueryBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-500)]
    [InlineData(101)]
    [InlineData(1000)]
    public async Task List_OutOfRangeTake_IsHandledWithoutFailure(int take)
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var response = await client.GetAsync($"/api/alerts?take={take}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<AlertPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload);
    }

    private sealed record AlertPayload(Guid Id);
}
