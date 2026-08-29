using System.Net;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class FinancialDataOwnershipTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory
        _factory;

    public FinancialDataOwnershipTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task FinancialLists_ReturnOnlyAuthenticatedUsersData()
    {
        using var ownerClient =
            _factory.CreateHttpsClient();

        using var otherClient =
            _factory.CreateHttpsClient();

        var ownerSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    ownerClient);

        var otherSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    otherClient);

        var ownerUserId =
            await GetUserIdAsync(
                ownerSession.Email);

        var otherUserId =
            await GetUserIdAsync(
                otherSession.Email);

        var seeded =
            await SeedFinancialDataAsync(
                ownerUserId,
                otherUserId);

        TestUserAuthentication.Authorize(
            ownerClient,
            ownerSession);

        using var connectionsResponse =
            await ownerClient.GetAsync(
                "/api/bank-connections");

        connectionsResponse
            .EnsureSuccessStatusCode();

        var connectionIds =
            await ReadIdsAsync(
                connectionsResponse);

        Assert.Contains(
            seeded.OwnerConnectionId,
            connectionIds);

        Assert.DoesNotContain(
            seeded.OtherConnectionId,
            connectionIds);

        using var accountsResponse =
            await ownerClient.GetAsync(
                "/api/bank-accounts");

        accountsResponse
            .EnsureSuccessStatusCode();

        var accountIds =
            await ReadIdsAsync(
                accountsResponse);

        Assert.Contains(
            seeded.OwnerAccountId,
            accountIds);

        Assert.DoesNotContain(
            seeded.OtherAccountId,
            accountIds);

        using var transactionsResponse =
            await ownerClient.GetAsync(
                "/api/bank-transactions?take=500");

        transactionsResponse
            .EnsureSuccessStatusCode();

        var transactionIds =
            await ReadIdsAsync(
                transactionsResponse);

        Assert.Contains(
            seeded.OwnerTransactionId,
            transactionIds);

        Assert.DoesNotContain(
            seeded.OtherTransactionId,
            transactionIds);
    }

    [Fact]
    public async Task DisconnectingAnotherUsersConnection_ReturnsNotFoundAndDoesNotModifyIt()
    {
        using var ownerClient =
            _factory.CreateHttpsClient();

        using var otherClient =
            _factory.CreateHttpsClient();

        var ownerSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    ownerClient);

        var otherSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    otherClient);

        var ownerUserId =
            await GetUserIdAsync(
                ownerSession.Email);

        var otherUserId =
            await GetUserIdAsync(
                otherSession.Email);

        var seeded =
            await SeedFinancialDataAsync(
                ownerUserId,
                otherUserId);

        TestUserAuthentication.Authorize(
            ownerClient,
            ownerSession);

        using var response =
            await ownerClient.DeleteAsync(
                $"/api/bank-connections/{seeded.OtherConnectionId}");

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

        var connection =
            await dbContext.BankConnections
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        seeded.OtherConnectionId);

        Assert.Equal(
            otherUserId,
            connection.UserId);

        Assert.Equal(
            BankConnectionStatus.Active,
            connection.Status);
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

    private async Task<SeededFinancialData>
        SeedFinancialDataAsync(
            Guid ownerUserId,
            Guid otherUserId)
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

        var ownerConnection =
            new BankConnectionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    ownerUserId,

                InstitutionName =
                    "Owner Bank",

                PlaidItemId =
                    $"owner-item-{Guid.NewGuid():N}",

                Status =
                    BankConnectionStatus.Active,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        var otherConnection =
            new BankConnectionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    otherUserId,

                InstitutionName =
                    "Other User Bank",

                PlaidItemId =
                    $"other-item-{Guid.NewGuid():N}",

                Status =
                    BankConnectionStatus.Active,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        var ownerAccount =
            new BankAccountEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    ownerUserId,

                BankConnectionId =
                    ownerConnection.Id,

                PlaidAccountId =
                    $"owner-account-{Guid.NewGuid():N}",

                Name =
                    "Owner Checking",

                Mask =
                    "1111",

                AccountType =
                    BankAccountType.Checking,

                IsActive =
                    true,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        var otherAccount =
            new BankAccountEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    otherUserId,

                BankConnectionId =
                    otherConnection.Id,

                PlaidAccountId =
                    $"other-account-{Guid.NewGuid():N}",

                Name =
                    "Other Checking",

                Mask =
                    "9999",

                AccountType =
                    BankAccountType.Checking,

                IsActive =
                    true,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        var ownerTransaction =
            new BankTransactionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    ownerUserId,

                BankAccountId =
                    ownerAccount.Id,

                PlaidTransactionId =
                    $"owner-transaction-{Guid.NewGuid():N}",

                Name =
                    "Owner Utility",

                MerchantName =
                    "Owner Utility",

                Amount =
                    75m,

                IsoCurrencyCode =
                    "USD",

                PostedDate =
                    DateOnly.FromDateTime(
                        DateTime.UtcNow),

                IsPending =
                    false,

                IsRemoved =
                    false,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        var otherTransaction =
            new BankTransactionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    otherUserId,

                BankAccountId =
                    otherAccount.Id,

                PlaidTransactionId =
                    $"other-transaction-{Guid.NewGuid():N}",

                Name =
                    "Other Utility",

                MerchantName =
                    "Other Utility",

                Amount =
                    999m,

                IsoCurrencyCode =
                    "USD",

                PostedDate =
                    DateOnly.FromDateTime(
                        DateTime.UtcNow),

                IsPending =
                    false,

                IsRemoved =
                    false,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        dbContext.BankConnections.AddRange(
            ownerConnection,
            otherConnection);

        dbContext.BankAccounts.AddRange(
            ownerAccount,
            otherAccount);

        dbContext.BankTransactions.AddRange(
            ownerTransaction,
            otherTransaction);

        await dbContext.SaveChangesAsync();

        return new SeededFinancialData(
            OwnerConnectionId:
                ownerConnection.Id,

            OtherConnectionId:
                otherConnection.Id,

            OwnerAccountId:
                ownerAccount.Id,

            OtherAccountId:
                otherAccount.Id,

            OwnerTransactionId:
                ownerTransaction.Id,

            OtherTransactionId:
                otherTransaction.Id);
    }

    private static async Task<IReadOnlyList<Guid>>
        ReadIdsAsync(
            HttpResponseMessage response)
    {
        await using var stream =
            await response.Content
                .ReadAsStreamAsync();

        using var document =
            await JsonDocument.ParseAsync(
                stream);

        return document.RootElement
            .EnumerateArray()
            .Select(
                element =>
                    element
                        .GetProperty(
                            "id")
                        .GetGuid())
            .ToList();
    }

    private sealed record SeededFinancialData(
        Guid OwnerConnectionId,
        Guid OtherConnectionId,
        Guid OwnerAccountId,
        Guid OtherAccountId,
        Guid OwnerTransactionId,
        Guid OtherTransactionId);
}