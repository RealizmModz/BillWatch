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

public sealed class PlaidTransactionSyncServiceTests
{
    [Fact]
    public async Task SyncConnectionAsync_PersistsValidatedTransactionAndCursorTogether()
    {
        await using var dbContext =
            CreateDbContext();

        var setup =
            await SeedConnectionAsync(
                dbContext);

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    TransactionPage(
                        setup.PlaidAccountId,
                        "transaction-1",
                        "cursor-1",
                        hasMore:
                            false)));

        using var httpClient =
            new HttpClient(
                handler);

        var service =
            CreateService(
                dbContext,
                setup.TokenProtector,
                httpClient);

        var result =
            await service.SyncConnectionAsync(
                setup.UserId,
                setup.ConnectionId);

        Assert.Equal(
            1,
            result.Added);

        Assert.Equal(
            0,
            result.Modified);

        Assert.Equal(
            0,
            result.Removed);

        var transaction =
            await dbContext.BankTransactions
                .SingleAsync();

        Assert.Equal(
            setup.UserId,
            transaction.UserId);

        Assert.Equal(
            setup.AccountId,
            transaction.BankAccountId);

        Assert.Equal(
            "transaction-1",
            transaction.PlaidTransactionId);

        Assert.Equal(
            79.99m,
            transaction.Amount);

        Assert.Equal(
            "USD",
            transaction.IsoCurrencyCode);

        Assert.False(
            transaction.IsRemoved);

        var connection =
            await dbContext.BankConnections
                .SingleAsync();

        Assert.Equal(
            "cursor-1",
            connection.TransactionsCursor);

        Assert.NotNull(
            connection.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public async Task MutationDuringPagination_RestartsFromOriginalCursor()
    {
        await using var dbContext =
            CreateDbContext();

        var setup =
            await SeedConnectionAsync(
                dbContext);

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    EmptyTransactionPage(
                        "cursor-page-1",
                        hasMore:
                            true)),
                JsonResponse(
                    HttpStatusCode.BadRequest,
                    """
                    {
                      "error_type": "TRANSACTIONS_ERROR",
                      "error_code": "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION",
                      "request_id": "request-mutation"
                    }
                    """),
                JsonResponse(
                    HttpStatusCode.OK,
                    TransactionPage(
                        setup.PlaidAccountId,
                        "transaction-after-restart",
                        "cursor-final",
                        hasMore:
                            false)));

        using var httpClient =
            new HttpClient(
                handler);

        var result =
            await CreateService(
                    dbContext,
                    setup.TokenProtector,
                    httpClient)
                .SyncConnectionAsync(
                    setup.UserId,
                    setup.ConnectionId);

        Assert.Equal(
            1,
            result.Added);

        Assert.Collection(
            handler.Requests,
            request =>
                Assert.DoesNotContain(
                    "\"cursor\"",
                    request.Body,
                    StringComparison.Ordinal),
            request =>
                Assert.Contains(
                    "\"cursor\":\"cursor-page-1\"",
                    request.Body,
                    StringComparison.Ordinal),
            request =>
                Assert.DoesNotContain(
                    "\"cursor\"",
                    request.Body,
                    StringComparison.Ordinal));

        Assert.Equal(
            "transaction-after-restart",
            (await dbContext.BankTransactions
                .SingleAsync())
                .PlaidTransactionId);

        Assert.Equal(
            "cursor-final",
            (await dbContext.BankConnections
                .SingleAsync())
                .TransactionsCursor);
    }

    private static PlaidTransactionSyncService CreateService(
        BillWatchDbContext dbContext,
        PlaidTokenProtector tokenProtector,
        HttpClient httpClient)
    {
        return new PlaidTransactionSyncService(
            dbContext,
            new PlaidApiClient(
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
                    })),
            tokenProtector);
    }

    private static async Task<ConnectionSetup> SeedConnectionAsync(
        BillWatchDbContext dbContext)
    {
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

                PlaidItemId =
                    "item-test",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-sandbox-test"),

                Status =
                    BankConnectionStatus.Active
            };

        var account =
            new BankAccountEntity
            {
                UserId =
                    userId,

                BankConnectionId =
                    connection.Id,

                PlaidAccountId =
                    "account-test",

                Name =
                    "Checking",

                AccountType =
                    BankAccountType.Checking,

                IsActive =
                    true
            };

        dbContext.BankConnections.Add(
            connection);

        dbContext.BankAccounts.Add(
            account);

        await dbContext.SaveChangesAsync();

        return new ConnectionSetup(
            userId,
            connection.Id,
            account.Id,
            account.PlaidAccountId,
            tokenProtector);
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

    private static string TransactionPage(
        string accountId,
        string transactionId,
        string nextCursor,
        bool hasMore)
    {
        return $$"""
                 {
                   "added": [
                     {
                       "transaction_id": "{{transactionId}}",
                       "account_id": "{{accountId}}",
                       "name": "Internet Service",
                       "merchant_name": "Example Telecom",
                       "amount": 79.99,
                       "iso_currency_code": "usd",
                       "date": "2026-08-30",
                       "authorized_date": null,
                       "pending": false,
                       "personal_finance_category": {
                         "primary": "GENERAL_SERVICES",
                         "detailed": "GENERAL_SERVICES_TELECOM"
                       }
                     }
                   ],
                   "modified": [],
                   "removed": [],
                   "next_cursor": "{{nextCursor}}",
                   "has_more": {{hasMore.ToString().ToLowerInvariant()}}
                 }
                 """;
    }

    private static string EmptyTransactionPage(
        string nextCursor,
        bool hasMore)
    {
        return $$"""
                 {
                   "added": [],
                   "modified": [],
                   "removed": [],
                   "next_cursor": "{{nextCursor}}",
                   "has_more": {{hasMore.ToString().ToLowerInvariant()}}
                 }
                 """;
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

    private sealed record ConnectionSetup(
        Guid UserId,
        Guid ConnectionId,
        Guid AccountId,
        string PlaidAccountId,
        PlaidTokenProtector TokenProtector);
}
