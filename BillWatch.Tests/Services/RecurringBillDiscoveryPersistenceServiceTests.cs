using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using BillWatch.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BillWatch.Tests.Services;

public sealed class RecurringBillDiscoveryPersistenceServiceTests
{
    [Fact]
    public async Task StableMonthlyUnclassifiedService_IsPromotedAsOther()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud Service",
            9.99m,
            categoryPrimary: null,
            categoryDetailed: null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        Assert.Equal(
            1,
            result.BillStreamsCreated);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Other,
            billStream.Category);

        Assert.True(
            billStream.IsActive);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Equal(
                    billStream.Id,
                    transaction.BillStreamId));
    }

    [Fact]
    public async Task StableMonthlyRestaurant_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Restaurant",
            20m,
            "FOOD_AND_DRINK",
            "FOOD_AND_DRINK_RESTAURANT");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Null(
                    transaction.BillStreamId));
    }

    [Fact]
    public async Task StableMonthlyGeneralMerchandiseWithoutSubscriptionEvidence_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Store",
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Fact]
    public async Task VariableUnclassifiedMonthlySpending_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 1, 5),
            10m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 2, 5),
            20m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 3, 5),
            30m,
            null,
            null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    private static BillWatchDbContext CreateDbContext()
    {
        return new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(
                    $"recurring-discovery-{Guid.NewGuid():N}")
                .Options);
    }

    private static void AddMonthlyTransactions(
        BillWatchDbContext dbContext,
        Guid userId,
        string merchantName,
        decimal amount,
        string? categoryPrimary,
        string? categoryDetailed)
    {
        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 1, 5),
            amount,
            categoryPrimary,
            categoryDetailed);

        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 2, 5),
            amount,
            categoryPrimary,
            categoryDetailed);

        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 3, 5),
            amount,
            categoryPrimary,
            categoryDetailed);
    }

    private static void AddTransaction(
        BillWatchDbContext dbContext,
        Guid userId,
        string merchantName,
        DateOnly postedDate,
        decimal amount,
        string? categoryPrimary,
        string? categoryDetailed)
    {
        dbContext.BankTransactions.Add(
            new BankTransactionEntity
            {
                UserId =
                    userId,

                BankAccountId =
                    Guid.NewGuid(),

                PlaidTransactionId =
                    Guid.NewGuid()
                        .ToString("N"),

                Name =
                    merchantName,

                MerchantName =
                    merchantName,

                Amount =
                    amount,

                IsoCurrencyCode =
                    "USD",

                PostedDate =
                    postedDate,

                AuthorizedDate =
                    postedDate,

                IsPending =
                    false,

                IsRemoved =
                    false,

                CategoryPrimary =
                    categoryPrimary,

                CategoryDetailed =
                    categoryDetailed
            });
    }
}
