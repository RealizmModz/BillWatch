using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BankTransactionQueryBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public async Task List_NonPositiveTake_IsClampedWithoutFailure(int take)
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var response = await client.GetAsync($"/api/bank-transactions?take={take}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<TransactionPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload);
    }

    [Theory]
    [InlineData(501)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public async Task List_OversizedTake_IsClampedWithoutFailure(int take)
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var response = await client.GetAsync($"/api/bank-transactions?take={take}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<TransactionPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload);
    }

    private sealed record TransactionPayload(Guid Id);
}
