using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Tests.Infrastructure;

public static class TestUserAuthentication
{
    private const string
        DefaultPassword =
            "BillWatch!Tests123";

    public static async Task<TestUserSession>
        RegisterAndLoginAsync(
            HttpClient client,
            string? email = null,
            CancellationToken cancellationToken =
                default)
    {
        email ??=
            $"security-{Guid.NewGuid():N}@billwatch.local";

        using var registerResponse =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password =
                        DefaultPassword
                },
                cancellationToken);

        registerResponse
            .EnsureSuccessStatusCode();

        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password =
                        DefaultPassword
                },
                cancellationToken);

        loginResponse
            .EnsureSuccessStatusCode();

        var loginResult =
            await loginResponse.Content
                .ReadFromJsonAsync<TestLoginResult>(
                    cancellationToken:
                        cancellationToken);

        if (loginResult is null ||
            string.IsNullOrWhiteSpace(
                loginResult.AccessToken))
        {
            throw new InvalidOperationException(
                "BillWatch did not return an access token for the test user.");
        }

        return new TestUserSession(
            Email:
                email,

            AccessToken:
                loginResult.AccessToken);
    }

    public static void Authorize(
        HttpClient client,
        TestUserSession session)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);
    }

    private sealed record TestLoginResult(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}

public sealed record TestUserSession(
    string Email,
    string AccessToken);