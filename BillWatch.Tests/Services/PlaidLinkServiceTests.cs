using System.Net;
using System.Text;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Plaid;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Services;

public sealed class PlaidLinkServiceTests
{
    [Fact]
    public async Task UpdateMode_IsOwnershipScopedAndUsesExistingProtectedAccessToken()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var connection =
            new BankConnectionEntity
            {
                UserId =
                    userId,

                InstitutionName =
                    "Test Bank",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-sandbox-update"),

                Status =
                    BankConnectionStatus.RequiresAttention
            };

        dbContext.BankConnections.Add(
            connection);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_token": "link-sandbox-update",
                      "hosted_link_url": "https://secure.plaid.com/update-test",
                      "expiration": "2099-08-30T18:00:00Z"
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var service =
            new PlaidLinkService(
                CreateApiClient(
                    httpClient),
                tokenProtector,
                dbContext);

        var result =
            await service.CreateLinkSessionAsync(
                userId,
                connection.Id);

        Assert.NotEqual(
            Guid.Empty,
            result.SessionId);

        var request =
            Assert.Single(
                handler.Requests);

        using var requestJson =
            JsonDocument.Parse(
                request.Body);

        Assert.Equal(
            "access-sandbox-update",
            requestJson.RootElement
                .GetProperty(
                    "access_token")
                .GetString());

        Assert.False(
            requestJson.RootElement.TryGetProperty(
                "products",
                out _));

        var session =
            await dbContext.PlaidLinkSessions
                .SingleAsync();

        Assert.Equal(
            connection.Id,
            session.BankConnectionId);

        Assert.NotEqual(
            "link-sandbox-update",
            session.ProtectedLinkToken);

        Assert.Equal(
            "link-sandbox-update",
            tokenProtector.UnprotectLinkToken(
                session.ProtectedLinkToken));
    }

    [Fact]
    public async Task UpdateMode_OtherUsersConnection_IsNotFoundBeforeProviderCall()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var connection =
            new BankConnectionEntity
            {
                UserId =
                    ownerId,

                InstitutionName =
                    "Owner Bank",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-owner"),

                Status =
                    BankConnectionStatus.RequiresAttention
            };

        dbContext.BankConnections.Add(
            connection);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler();

        using var httpClient =
            new HttpClient(
                handler);

        var service =
            new PlaidLinkService(
                CreateApiClient(
                    httpClient),
                tokenProtector,
                dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.CreateLinkSessionAsync(
                Guid.NewGuid(),
                connection.Id));

        Assert.Empty(
            handler.Requests);

        Assert.Empty(
            dbContext.PlaidLinkSessions);
    }

    private static PlaidApiClient CreateApiClient(
        HttpClient httpClient)
    {
        return new PlaidApiClient(
            httpClient,
            Options.Create(
                new PlaidOptions
                {
                    ClientId =
                        "test-client-id",

                    Secret =
                        "test-secret",

                    Environment =
                        PlaidOptions.SandboxEnvironment
                }));
    }

    private static BillWatchDbContext CreateDbContext()
    {
        return new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid()
                        .ToString(
                            "N"))
                .Options);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(
            statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }
}
