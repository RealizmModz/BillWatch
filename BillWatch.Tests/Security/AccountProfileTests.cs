using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class AccountProfileTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public AccountProfileTests(
        BillWatchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateProfile_PersistsOnlyForAuthenticatedUser()
    {
        using var firstClient =
            _factory.CreateHttpsClient();

        using var secondClient =
            _factory.CreateHttpsClient();

        var firstSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                firstClient);

        var secondSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                secondClient);

        TestUserAuthentication.Authorize(
            firstClient,
            firstSession);

        using var updateResponse =
            await firstClient.PostAsJsonAsync(
                "/api/account/security/profile",
                new
                {
                    displayName = "  Test Person  "
                });

        updateResponse.EnsureSuccessStatusCode();

        var updated =
            await updateResponse.Content.ReadFromJsonAsync<
                AccountSecurityTestResponse>();

        Assert.NotNull(updated);
        Assert.Equal("Test Person", updated.DisplayName);
        Assert.Equal(firstSession.Email, updated.Email);

        using var firstGetResponse =
            await firstClient.GetAsync(
                "/api/account/security");

        firstGetResponse.EnsureSuccessStatusCode();

        var firstProfile =
            await firstGetResponse.Content.ReadFromJsonAsync<
                AccountSecurityTestResponse>();

        Assert.NotNull(firstProfile);
        Assert.Equal("Test Person", firstProfile.DisplayName);

        TestUserAuthentication.Authorize(
            secondClient,
            secondSession);

        using var secondGetResponse =
            await secondClient.GetAsync(
                "/api/account/security");

        secondGetResponse.EnsureSuccessStatusCode();

        var secondProfile =
            await secondGetResponse.Content.ReadFromJsonAsync<
                AccountSecurityTestResponse>();

        Assert.NotNull(secondProfile);
        Assert.Equal(string.Empty, secondProfile.DisplayName);
        Assert.Equal(secondSession.Email, secondProfile.Email);
    }

    [Fact]
    public async Task UpdateProfile_RejectsDisplayNameLongerThanLimit()
    {
        using var client =
            _factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/account/security/profile",
                new
                {
                    displayName = new string('x', 81)
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    private sealed record AccountSecurityTestResponse(
        string DisplayName,
        string Email,
        bool EmailConfirmed,
        bool TwoFactorEnabled,
        bool HasAuthenticatorKey,
        int RecoveryCodesLeft);
}
