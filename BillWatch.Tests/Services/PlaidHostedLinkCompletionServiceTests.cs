using System.Net;
using System.Text;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Plaid;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Services;

public sealed class PlaidHostedLinkCompletionServiceTests
{
    [Fact]
    public async Task CompletedHostedLink_ExchangesTokenServerSideAndClearsSessionCredential()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var session =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                ProtectedLinkToken =
                    tokenProtector.ProtectLinkToken(
                        "link-sandbox-test"),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    DateTimeOffset.UtcNow.AddHours(
                        1)
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_sessions": [
                        {
                          "finished_at": "2026-08-30T18:00:00Z",
                          "results": {
                            "item_add_results": [
                              { "public_token": "public-sandbox-test" }
                            ]
                          }
                        }
                      ]
                    }
                    """),
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "access_token": "access-sandbox-test",
                      "item_id": "item-test-1"
                    }
                    """),
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "item": {
                        "item_id": "item-test-1",
                        "institution_id": "ins_test",
                        "institution_name": "Test Bank"
                      }
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var plaidApiClient =
            CreateApiClient(
                httpClient);

        var exchangeService =
            new PlaidConnectionExchangeService(
                plaidApiClient,
                tokenProtector,
                dbContext);

        var service =
            new PlaidHostedLinkCompletionService(
                dbContext,
                plaidApiClient,
                tokenProtector,
                exchangeService);

        var result =
            await service.CheckAndCompleteAsync(
                userId,
                session.Id);

        Assert.Equal(
            "Completed",
            result.Status);

        Assert.NotNull(
            result.Connection);

        Assert.Equal(
            "Test Bank",
            result.Connection.InstitutionName);

        Assert.Equal(
            string.Empty,
            session.ProtectedLinkToken);

        var connection =
            await dbContext.BankConnections
                .SingleAsync();

        Assert.NotEqual(
            "access-sandbox-test",
            connection.ProtectedPlaidAccessToken);

        Assert.Equal(
            "access-sandbox-test",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));

        Assert.Collection(
            handler.Requests,
            request =>
                Assert.Equal(
                    "/link/token/get",
                    request.Uri.AbsolutePath),
            request =>
                Assert.Equal(
                    "/item/public_token/exchange",
                    request.Uri.AbsolutePath),
            request =>
                Assert.Equal(
                    "/item/get",
                    request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task ExitedHostedLink_BecomesTerminalWithoutExchangingToken()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var session =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                ProtectedLinkToken =
                    tokenProtector.ProtectLinkToken(
                        "link-sandbox-exited"),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    DateTimeOffset.UtcNow.AddHours(
                        1)
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_sessions": [
                        {
                          "finished_at": "2026-08-30T18:00:00Z",
                          "exit": {}
                        }
                      ]
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var plaidApiClient =
            CreateApiClient(
                httpClient);

        var service =
            new PlaidHostedLinkCompletionService(
                dbContext,
                plaidApiClient,
                tokenProtector,
                new PlaidConnectionExchangeService(
                    plaidApiClient,
                    tokenProtector,
                    dbContext));

        var result =
            await service.CheckAndCompleteAsync(
                userId,
                session.Id);

        Assert.Equal(
            "Exited",
            result.Status);

        Assert.Null(
            result.Connection);

        Assert.Equal(
            string.Empty,
            session.ProtectedLinkToken);

        Assert.Single(
            handler.Requests);

        Assert.Empty(
            dbContext.BankConnections);
    }

    [Fact]
    public async Task CompletedUpdateMode_ReactivatesOwnedConnectionWithoutExchangingToken()
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
                    "Update Bank",

                PlaidItemId =
                    "item-update-1",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-update-1"),

                Status =
                    BankConnectionStatus.RequiresAttention
            };

        dbContext.BankConnections.Add(
            connection);

        var session =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                BankConnectionId =
                    connection.Id,

                ProtectedLinkToken =
                    tokenProtector.ProtectLinkToken(
                        "link-sandbox-update"),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    DateTimeOffset.UtcNow.AddMinutes(
                        30)
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_sessions": [
                        {
                          "finished_at": "2026-08-30T18:00:00Z",
                          "results": {}
                        }
                      ]
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var plaidApiClient =
            CreateApiClient(
                httpClient);

        var service =
            new PlaidHostedLinkCompletionService(
                dbContext,
                plaidApiClient,
                tokenProtector,
                new PlaidConnectionExchangeService(
                    plaidApiClient,
                    tokenProtector,
                    dbContext));

        var result =
            await service.CheckAndCompleteAsync(
                userId,
                session.Id);

        Assert.Equal(
            "Completed",
            result.Status);

        Assert.Equal(
            "Active",
            result.Connection?.Status);

        Assert.Equal(
            BankConnectionStatus.Active,
            connection.Status);

        Assert.Equal(
            string.Empty,
            session.ProtectedLinkToken);

        Assert.Single(
            handler.Requests);

        Assert.Equal(
            "access-update-1",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));
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
