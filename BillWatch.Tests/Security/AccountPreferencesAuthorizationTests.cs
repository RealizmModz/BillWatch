using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class AccountPreferencesAuthorizationTests
{
    [Fact]
    public async Task Get_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/account/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_AnonymousUser_IsRejected()
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new { timestampDisplayMode = "Utc" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ChangesOnlyAuthenticatedUsersPreference()
    {
        await using var factory = new BillWatchApiFactory();
        using var firstClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var first = await TestUserAuthentication.RegisterAndLoginAsync(firstClient);
        var second = await TestUserAuthentication.RegisterAndLoginAsync(secondClient);
        var firstId = await TestUserAuthentication.GetUserIdAsync(factory, first.Email);
        var secondId = await TestUserAuthentication.GetUserIdAsync(factory, second.Email);

        firstClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", first.AccessToken);

        using var response = await firstClient.PutAsJsonAsync(
            "/api/account/preferences",
            new { timestampDisplayMode = "Utc" });

        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillWatchDbContext>();
        var firstMode = await dbContext.Users
            .Where(user => user.Id == firstId)
            .Select(user => user.TimestampDisplayMode)
            .SingleAsync();
        var secondMode = await dbContext.Users
            .Where(user => user.Id == secondId)
            .Select(user => user.TimestampDisplayMode)
            .SingleAsync();

        Assert.Equal(TimestampDisplayMode.Utc, firstMode);
        Assert.Equal(TimestampDisplayMode.Local12Hour, secondMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-mode")]
    [InlineData("2")]
    public async Task Put_InvalidPreference_IsRejected(string mode)
    {
        await using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);

        using var response = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new { timestampDisplayMode = mode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
