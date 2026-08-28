using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using BillWatch.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class BillStatementPaymentDueAlertServiceTests
{
    [Fact]
    public async Task ExplicitFutureDueDate_CreatesAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new BillStatementPaymentDueAlertService(
                dbContext);

        await service.ReconcileAsync(
            userId,
            stream.Id,
            new DateOnly(
                2026,
                9,
                15),
            94.99m,
            "USD",
            new DateOnly(
                2026,
                8,
                27),
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .ToListAsync());

        Assert.Equal(
            BillAlertType.PaymentDue,
            alert.AlertType);

        Assert.Equal(
            BillAlertSeverity.Info,
            alert.Severity);

        Assert.Contains(
            "$94.99",
            alert.Message);

        Assert.Contains(
            "Sep 15, 2026",
            alert.Title);

        Assert.Contains(
            "directly on the provider statement",
            alert.Message);
    }

    [Fact]
    public async Task SameDueEvent_IsUpdatedInsteadOfDuplicated()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new BillStatementPaymentDueAlertService(
                dbContext);

        var dueDate =
            new DateOnly(
                2026,
                9,
                1);

        var today =
            new DateOnly(
                2026,
                8,
                27);

        await service.ReconcileAsync(
            userId,
            stream.Id,
            dueDate,
            79.99m,
            "USD",
            today,
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        var original =
            Assert.Single(
                await dbContext.BillAlerts
                    .ToListAsync());

        original.IsRead =
            true;

        original.IsDismissed =
            true;

        await dbContext.SaveChangesAsync();

        await service.ReconcileAsync(
            userId,
            stream.Id,
            dueDate,
            104.99m,
            "USD",
            today,
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        var updated =
            Assert.Single(
                await dbContext.BillAlerts
                    .ToListAsync());

        Assert.Equal(
            original.Id,
            updated.Id);

        Assert.Contains(
            "$104.99",
            updated.Message);

        Assert.False(
            updated.IsRead);

        Assert.False(
            updated.IsDismissed);
    }

    [Fact]
    public async Task MissingOrPastDueDate_DoesNotCreateAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        dbContext.BillStreams.Add(
            stream);

        await dbContext.SaveChangesAsync();

        var service =
            new BillStatementPaymentDueAlertService(
                dbContext);

        var today =
            new DateOnly(
                2026,
                8,
                27);

        await service.ReconcileAsync(
            userId,
            stream.Id,
            null,
            94.99m,
            "USD",
            today,
            DateTimeOffset.UtcNow);

        await service.ReconcileAsync(
            userId,
            stream.Id,
            new DateOnly(
                2026,
                8,
                20),
            94.99m,
            "USD",
            today,
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

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
        CreateBillStream(
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
                BillCategory.Unknown,

            Source =
                BillStreamSource.Manual,

            IsActive =
                true,

            CreatedAtUtc =
                DateTimeOffset.UtcNow,

            UpdatedAtUtc =
                DateTimeOffset.UtcNow
        };
    }
}