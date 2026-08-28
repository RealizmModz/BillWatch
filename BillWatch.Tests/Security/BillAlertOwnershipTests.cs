using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class BillAlertOwnershipTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory
        _factory;

    public BillAlertOwnershipTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task OtherUser_CannotSeeAlert()
    {
        var owner =
            await CreateUserAsync(
                "owner-list");

        var otherUser =
            await CreateUserAsync(
                "other-list");

        await CreateAlertAsync(
            owner.UserId);

        using var client =
            _factory.CreateHttpsClient();

        await AuthenticateAsync(
            client,
            otherUser.Email,
            otherUser.Password);

        using var response =
            await client.GetAsync(
                "/api/alerts");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var alerts =
            await response.Content
                .ReadFromJsonAsync<
                    List<AlertPayload>>();

        Assert.NotNull(
            alerts);

        Assert.Empty(
            alerts);
    }

    [Fact]
    public async Task OtherUser_CannotMarkAlertRead()
    {
        var owner =
            await CreateUserAsync(
                "owner-read");

        var otherUser =
            await CreateUserAsync(
                "other-read");

        var alertId =
            await CreateAlertAsync(
                owner.UserId);

        using var client =
            _factory.CreateHttpsClient();

        await AuthenticateAsync(
            client,
            otherUser.Email,
            otherUser.Password);

        using var response =
            await client.PostAsync(
                $"/api/alerts/{alertId}/read",
                content:
                    null);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        await AssertAlertStateAsync(
            alertId,
            isRead:
                false,
            isDismissed:
                false);
    }

    [Fact]
    public async Task OtherUser_CannotDismissAlert()
    {
        var owner =
            await CreateUserAsync(
                "owner-dismiss");

        var otherUser =
            await CreateUserAsync(
                "other-dismiss");

        var alertId =
            await CreateAlertAsync(
                owner.UserId);

        using var client =
            _factory.CreateHttpsClient();

        await AuthenticateAsync(
            client,
            otherUser.Email,
            otherUser.Password);

        using var response =
            await client.PostAsync(
                $"/api/alerts/{alertId}/dismiss",
                content:
                    null);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        await AssertAlertStateAsync(
            alertId,
            isRead:
                false,
            isDismissed:
                false);
    }

    private async Task<TestUser>
        CreateUserAsync(
            string prefix)
    {
        var unique =
            Guid.NewGuid()
                .ToString(
                    "N");

        var email =
            $"{prefix}-{unique}@billwatch.test";

        var password =
            "BillWatch!Test12345";

        using var scope =
            _factory.Services
                .CreateScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var user =
            new ApplicationUser
            {
                Id =
                    Guid.NewGuid(),

                UserName =
                    email,

                Email =
                    email,

                EmailConfirmed =
                    true
            };

        var result =
            await userManager.CreateAsync(
                user,
                password);

        Assert.True(
            result.Succeeded,
            string.Join(
                Environment.NewLine,
                result.Errors.Select(
                    error =>
                        $"{error.Code}: {error.Description}")));

        return new TestUser(
            user.Id,
            email,
            password);
    }

    private async Task<Guid>
        CreateAlertAsync(
            Guid userId)
    {
        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var alert =
            new BillAlertEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    userId,

                BillStreamId =
                    null,

                BillChangeId =
                    null,

                AlertType =
                    BillAlertType.BillIncrease,

                Severity =
                    BillAlertSeverity.Warning,

                Title =
                    "Test bill increased",

                Message =
                    "Security ownership test.",

                IsRead =
                    false,

                IsDismissed =
                    false,

                CreatedAtUtc =
                    DateTimeOffset.UtcNow,

                UpdatedAtUtc =
                    DateTimeOffset.UtcNow
            };

        dbContext.BillAlerts.Add(
            alert);

        await dbContext.SaveChangesAsync();

        return alert.Id;
    }

    private async Task AssertAlertStateAsync(
        Guid alertId,
        bool isRead,
        bool isDismissed)
    {
        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var alert =
            await dbContext.BillAlerts
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                            alertId);

        Assert.Equal(
            isRead,
            alert.IsRead);

        Assert.Equal(
            isDismissed,
            alert.IsDismissed);
    }

    private static async Task AuthenticateAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password
                });

        response.EnsureSuccessStatusCode();

        var login =
            await response.Content
                .ReadFromJsonAsync<LoginPayload>();

        Assert.NotNull(
            login);

        Assert.False(
            string.IsNullOrWhiteSpace(
                login.AccessToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                login.AccessToken);
    }

    private sealed record TestUser(
        Guid UserId,
        string Email,
        string Password);

    private sealed record LoginPayload(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);

    private sealed record AlertPayload(
        Guid Id,
        Guid? BillStreamId,
        Guid? BillChangeId,
        string AlertType,
        string Severity,
        string Title,
        string Message,
        bool IsRead,
        bool IsDismissed,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}