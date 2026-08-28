using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using BillWatch.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class RecurringBillDiscoveryAlertServiceTests
{
    [Fact]
    public async Task NewAutomaticBill_CreatesInformationalAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateStream(
                userId);

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryAlertService(
                dbContext);

        var created =
            await service
                .EnsureNewBillAlertAsync(
                    userId,
                    stream,
                    matchingTransactionCount:
                        4,
                    DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        Assert.True(
            created);

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .ToListAsync());

        Assert.Equal(
            userId,
            alert.UserId);

        Assert.Equal(
            stream.Id,
            alert.BillStreamId);

        Assert.Null(
            alert.BillChangeId);

        Assert.Equal(
            BillAlertType.NewBill,
            alert.AlertType);

        Assert.Equal(
            BillAlertSeverity.Info,
            alert.Severity);

        Assert.False(
            alert.IsRead);

        Assert.False(
            alert.IsDismissed);

        Assert.Contains(
            "Midco",
            alert.Title);

        Assert.Contains(
            "4 posted bank transactions",
            alert.Message);

        Assert.Contains(
            "transaction-based discovery",
            alert.Message);

        Assert.Contains(
            "provider statement has not yet",
            alert.Message);
    }

    [Fact]
    public async Task SameBill_DoesNotCreateDuplicateAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateStream(
                userId);

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryAlertService(
                dbContext);

        var firstCreated =
            await service
                .EnsureNewBillAlertAsync(
                    userId,
                    stream,
                    4,
                    DateTimeOffset.UtcNow);

        /*
         * Intentionally call again before SaveChangesAsync.
         * The Local tracking guard must stop this duplicate too.
         */
        var secondCreated =
            await service
                .EnsureNewBillAlertAsync(
                    userId,
                    stream,
                    4,
                    DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        var thirdCreated =
            await service
                .EnsureNewBillAlertAsync(
                    userId,
                    stream,
                    4,
                    DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        Assert.True(
            firstCreated);

        Assert.False(
            secondCreated);

        Assert.False(
            thirdCreated);

        Assert.Single(
            await dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            stream.Id &&
                        alert.AlertType ==
                            BillAlertType.NewBill)
                .ToListAsync());
    }

    [Fact]
    public async Task CrossUserBill_IsRejected()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerUserId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var stream =
            CreateStream(
                ownerUserId);

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryAlertService(
                dbContext);

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                service.EnsureNewBillAlertAsync(
                    otherUserId,
                    stream,
                    4,
                    DateTimeOffset.UtcNow));

        Assert.Empty(
            await dbContext.BillAlerts
                .ToListAsync());
    }

    [Fact]
    public async Task ManualBill_IsNotEligibleForDiscoveryAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateStream(
                userId);

        stream.Source =
            BillStreamSource.Manual;

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryAlertService(
                dbContext);

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                service.EnsureNewBillAlertAsync(
                    userId,
                    stream,
                    4,
                    DateTimeOffset.UtcNow));

        Assert.Empty(
            await dbContext.BillAlerts
                .ToListAsync());
    }

    private static BillWatchDbContext
        CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<
                    BillWatchDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid()
                        .ToString(
                            "N"))
                .Options;

        return new BillWatchDbContext(
            options);
    }

    private static BillStreamEntity
        CreateStream(
            Guid userId)
    {
        return new BillStreamEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            ProviderName =
                "Midco",

            Category =
                BillCategory.Internet,

            Source =
                BillStreamSource.AutomaticDiscovery,

            IsActive =
                true,

            CreatedAtUtc =
                DateTimeOffset.UtcNow,

            UpdatedAtUtc =
                DateTimeOffset.UtcNow
        };
    }
}