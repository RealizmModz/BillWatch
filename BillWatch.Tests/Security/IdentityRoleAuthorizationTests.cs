using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.API.Authorization;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Legal;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class IdentityRoleAuthorizationTests
{
    private const string Password =
        "BillWatch!RoleTests123";

    [Fact]
    public async Task OwnerRoleAssignedBeforeLogin_AuthorizesAdminEndpoint()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"owner-role-{Guid.NewGuid():N}@billwatch.local";

        await RegisterAsync(
            client,
            email);

        await AssignOwnerRoleAsync(
            factory,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                loginResult.AccessToken);

        using var adminResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.OK,
            adminResponse.StatusCode);
    }

    [Fact]
    public async Task OwnerRefreshToken_IssuesRoleAwareAccessToken()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"owner-refresh-{Guid.NewGuid():N}@billwatch.local";

        await RegisterAsync(
            client,
            email);

        await AssignOwnerRoleAsync(
            factory,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        loginResult.RefreshToken
                });

        refreshResponse.EnsureSuccessStatusCode();

        var refreshResult =
            await refreshResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            refreshResult);

        Assert.False(
            string.IsNullOrWhiteSpace(
                refreshResult!.AccessToken));

        Assert.False(
            string.IsNullOrWhiteSpace(
                refreshResult.RefreshToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                refreshResult.AccessToken);

        using var adminResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.OK,
            adminResponse.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutStaffRole_RemainsForbidden()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var adminResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            adminResponse.StatusCode);
    }

    private static async Task RegisterAsync(
        HttpClient client,
        string email)
    {
        using var registerResponse =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = Password,
                    acceptedTermsAndPrivacy = true,
                    legalTermsVersion = BillWatchLegalDocuments.CurrentVersion
                });

        registerResponse.EnsureSuccessStatusCode();
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        string email)
    {
        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password = Password
                });

        loginResponse.EnsureSuccessStatusCode();

        var loginResult =
            await loginResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            loginResult);

        Assert.False(
            string.IsNullOrWhiteSpace(
                loginResult!.AccessToken));

        Assert.False(
            string.IsNullOrWhiteSpace(
                loginResult.RefreshToken));

        return loginResult;
    }

    private static async Task AssignOwnerRoleAsync(
        BillWatchApiFactory factory,
        string email)
    {
        using var scope =
            factory.Services.CreateScope();

        var roleManager =
            scope.ServiceProvider
                .GetRequiredService<
                    RoleManager<IdentityRole<Guid>>>();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(
                BillWatchRoles.Owner))
        {
            var createRoleResult =
                await roleManager.CreateAsync(
                    new IdentityRole<Guid>(
                        BillWatchRoles.Owner));

            Assert.True(
                createRoleResult.Succeeded,
                FormatErrors(
                    createRoleResult));
        }

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(
            user);

        var addRoleResult =
            await userManager.AddToRoleAsync(
                user!,
                BillWatchRoles.Owner);

        Assert.True(
            addRoleResult.Succeeded,
            FormatErrors(
                addRoleResult));
    }

    private static string FormatErrors(
        IdentityResult result)
    {
        return string.Join(
            "; ",
            result.Errors.Select(
                error =>
                    $"{error.Code}: {error.Description}"));
    }

    private sealed record LoginResult(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}
