using System.Net;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class PlaidOwnershipBoundaryTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory
        _factory;

    public PlaidOwnershipBoundaryTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task OtherUsersHostedLinkSession_CannotBeCompleted()
    {
        using var attackerClient =
            _factory.CreateHttpsClient();

        using var ownerClient =
            _factory.CreateHttpsClient();

        var attackerSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    attackerClient);

        var ownerSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    ownerClient);

        var ownerUserId =
            await GetUserIdAsync(
                ownerSession.Email);

        var linkSessionId =
            await CreateCompletedLinkSessionAsync(
                ownerUserId);

        TestUserAuthentication.Authorize(
            attackerClient,
            attackerSession);

        using var response =
            await attackerClient.PostAsync(
                $"/api/plaid/link-session/{linkSessionId}/complete",
                content:
                    null);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var linkSession =
            await dbContext.PlaidLinkSessions
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        linkSessionId);

        Assert.Equal(
            ownerUserId,
            linkSession.UserId);

        Assert.Equal(
            PlaidLinkSessionStatus.Completed,
            linkSession.Status);
    }

    [Fact]
    public async Task OtherUsersConnection_CannotBeUsedForAccountSync()
    {
        var result =
            await ExerciseOtherUsersConnectionAsync(
                connectionId =>
                    $"/api/plaid/connections/{connectionId}/accounts/sync");

        Assert.Equal(
            HttpStatusCode.NotFound,
            result.StatusCode);

        await AssertConnectionStillOwnedAndActiveAsync(
            result.OwnerUserId,
            result.ConnectionId);
    }

    [Fact]
    public async Task OtherUsersConnection_CannotBeUsedForTransactionSync()
    {
        var result =
            await ExerciseOtherUsersConnectionAsync(
                connectionId =>
                    $"/api/plaid/connections/{connectionId}/transactions/sync");

        Assert.Equal(
            HttpStatusCode.NotFound,
            result.StatusCode);

        await AssertConnectionStillOwnedAndActiveAsync(
            result.OwnerUserId,
            result.ConnectionId);
    }

    [Fact]
    public async Task OtherUsersConnection_CannotCreateUpdateModeLinkSession()
    {
        var result =
            await ExerciseOtherUsersConnectionAsync(
                connectionId =>
                    $"/api/plaid/connections/{connectionId}/update-link-token");

        Assert.Equal(
            HttpStatusCode.NotFound,
            result.StatusCode);

        await AssertConnectionStillOwnedAndActiveAsync(
            result.OwnerUserId,
            result.ConnectionId);
    }

    private async Task<ConnectionAttackResult>
        ExerciseOtherUsersConnectionAsync(
            Func<Guid, string> routeFactory)
    {
        using var attackerClient =
            _factory.CreateHttpsClient();

        using var ownerClient =
            _factory.CreateHttpsClient();

        var attackerSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    attackerClient);

        var ownerSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    ownerClient);

        var ownerUserId =
            await GetUserIdAsync(
                ownerSession.Email);

        var connectionId =
            await CreateConnectionAsync(
                ownerUserId);

        TestUserAuthentication.Authorize(
            attackerClient,
            attackerSession);

        using var response =
            await attackerClient.PostAsync(
                routeFactory(
                    connectionId),
                content:
                    null);

        return new ConnectionAttackResult(
            StatusCode:
                response.StatusCode,

            OwnerUserId:
                ownerUserId,

            ConnectionId:
                connectionId);
    }

    private async Task<Guid> GetUserIdAsync(
        string email)
    {
        using var scope =
            _factory.Services
                .CreateScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(
            user);

        return user.Id;
    }

    private async Task<Guid>
        CreateCompletedLinkSessionAsync(
            Guid userId)
    {
        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var now =
            DateTimeOffset.UtcNow;

        var session =
            new PlaidLinkSessionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    userId,

                /*
                 * This deliberately is not a real link token.
                 * Status is already terminal, so correct ownership
                 * behavior will never attempt to decrypt or use it.
                 */
                ProtectedLinkToken =
                    "ownership-test-token",

                Status =
                    PlaidLinkSessionStatus.Completed,

                ExpiresAtUtc =
                    now.AddHours(
                        1),

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now,

                CompletedAtUtc =
                    now
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        return session.Id;
    }

    private async Task<Guid>
        CreateConnectionAsync(
            Guid userId)
    {
        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var connection =
            new BankConnectionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    userId,

                InstitutionName =
                    "Ownership Test Bank",

                PlaidItemId =
                    $"ownership-item-{Guid.NewGuid():N}",

                /*
                 * Null is intentional. If ownership filtering were
                 * accidentally removed, the service would fail before
                 * making any external Plaid request.
                 */
                ProtectedPlaidAccessToken =
                    null,

                Status =
                    BankConnectionStatus.Active,

                CreatedAtUtc =
                    DateTimeOffset.UtcNow,

                UpdatedAtUtc =
                    DateTimeOffset.UtcNow
            };

        dbContext.BankConnections.Add(
            connection);

        await dbContext.SaveChangesAsync();

        return connection.Id;
    }

    private async Task
        AssertConnectionStillOwnedAndActiveAsync(
            Guid ownerUserId,
            Guid connectionId)
    {
        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var connection =
            await dbContext.BankConnections
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        connectionId);

        Assert.Equal(
            ownerUserId,
            connection.UserId);

        Assert.Equal(
            BankConnectionStatus.Active,
            connection.Status);
    }

    private sealed record ConnectionAttackResult(
        HttpStatusCode StatusCode,
        Guid OwnerUserId,
        Guid ConnectionId);
}
