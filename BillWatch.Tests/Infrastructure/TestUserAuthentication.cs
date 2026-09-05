using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Legal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Infrastructure;

public static class TestUserAuthentication
{
    private const string DefaultPassword =
        "BillWatch!Tests123";

    public static async Task<TestUserSession> RegisterAndLoginAsync(
        HttpClient client,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        email ??=
            $"security-{Guid.NewGuid():N}@billwatch.local";

        await RegisterAsync(
            client,
            email,
            cancellationToken);

        return await LoginAsync(
            client,
            email,
            cancellationToken);
    }

    public static async Task<TestUserSession> RegisterWithRoleAndLoginAsync(
        BillWatchApiFactory factory,
        HttpClient client,
        string roleName,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        email ??=
            $"security-{Guid.NewGuid():N}@billwatch.local";

        await RegisterAsync(
            client,
            email,
            cancellationToken);

        await AssignRoleAsync(
            factory,
            email,
            roleName,
            cancellationToken);

        return await LoginAsync(
            client,
            email,
            cancellationToken);
    }

    public static async Task<TestUserSession> LoginAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken = default)
    {
        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password = DefaultPassword
                },
                cancellationToken);

        loginResponse.EnsureSuccessStatusCode();

        var loginResult =
            await loginResponse.Content
                .ReadFromJsonAsync<TestLoginResult>(
                    cancellationToken:
                        cancellationToken);

        if (loginResult is null ||
            string.IsNullOrWhiteSpace(loginResult.AccessToken))
        {
            throw new InvalidOperationException(
                "BillWatch did not return an access token for the test user.");
        }

        return new TestUserSession(
            Email: email,
            AccessToken: loginResult.AccessToken);
    }

    public static async Task<Guid> GetUserIdAsync(
        BillWatchApiFactory factory,
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<BillWatchDbContext>();

        return await dbContext.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);
    }

    public static async Task AssignRoleAsync(
        BillWatchApiFactory factory,
        string email,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new ArgumentException(
                "A role name is required.",
                nameof(roleName));
        }

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<BillWatchDbContext>();

        var user =
            await dbContext.Users.SingleAsync(
                candidate => candidate.Email == email,
                cancellationToken);

        var normalizedRole =
            roleName.Trim().ToUpperInvariant();

        var role =
            await dbContext.Roles.SingleOrDefaultAsync(
                candidate => candidate.NormalizedName == normalizedRole,
                cancellationToken);

        if (role is null)
        {
            role =
                new IdentityRole<Guid>
                {
                    Id = Guid.NewGuid(),
                    Name = roleName.Trim(),
                    NormalizedName = normalizedRole
                };

            dbContext.Roles.Add(role);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var alreadyAssigned =
            await dbContext.UserRoles.AnyAsync(
                candidate =>
                    candidate.UserId == user.Id &&
                    candidate.RoleId == role.Id,
                cancellationToken);

        if (!alreadyAssigned)
        {
            dbContext.UserRoles.Add(
                new IdentityUserRole<Guid>
                {
                    UserId = user.Id,
                    RoleId = role.Id
                });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
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

    private static async Task RegisterAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken)
    {
        using var registerResponse =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = DefaultPassword,
                    acceptedTermsAndPrivacy = true,
                    legalTermsVersion = BillWatchLegalDocuments.CurrentVersion
                },
                cancellationToken);

        registerResponse.EnsureSuccessStatusCode();
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