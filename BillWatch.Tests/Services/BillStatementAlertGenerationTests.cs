using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using BillWatch.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAlertGenerationTests
{
    [Fact]
    public async Task Increase_CreatesSingleOwnershipScopedAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        var previous =
            CreateStatement(
                userId,
                stream.Id,
                new DateOnly(
                    2026,
                    6,
                    1),
                new DateOnly(
                    2026,
                    6,
                    30),
                79.99m);

        var current =
            CreateStatement(
                userId,
                stream.Id,
                new DateOnly(
                    2026,
                    7,
                    1),
                new DateOnly(
                    2026,
                    7,
                    31),
                104.99m);

        dbContext.BillStreams.Add(
            stream);

        dbContext.BillStatements.AddRange(
            previous,
            current);

        await dbContext.SaveChangesAsync();

        var service =
            new BillStatementChangeDetectionService(
                dbContext);

        await service.ReconcileAsync(
            userId,
            stream.Id);

        await dbContext.SaveChangesAsync();

        var change =
            Assert.Single(
                await dbContext.BillChanges
                    .Where(
                        item =>
                            item.UserId ==
                                userId)
                    .ToListAsync());

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .Where(
                        item =>
                            item.UserId ==
                                userId)
                    .ToListAsync());

        Assert.Equal(
            stream.Id,
            alert.BillStreamId);

        Assert.Equal(
            change.Id,
            alert.BillChangeId);

        Assert.Equal(
            BillAlertType.BillIncrease,
            alert.AlertType);

        Assert.Equal(
            BillAlertSeverity.Warning,
            alert.Severity);

        Assert.False(
            alert.IsRead);

        Assert.False(
            alert.IsDismissed);

        Assert.Contains(
            "$25.00/month",
            alert.Title);

        Assert.Contains(
            "$300.00/year",
            alert.Message);

        /*
         * Run reconciliation again.
         *
         * The same BillChange must not produce another alert.
         */
        await service.ReconcileAsync(
            userId,
            stream.Id);

        await dbContext.SaveChangesAsync();

        Assert.Single(
            await dbContext.BillAlerts
                .Where(
                    item =>
                        item.UserId ==
                            userId)
                .ToListAsync());
    }

    [Fact]
    public async Task Decrease_CreatesInformationalAlert()
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

        dbContext.BillStatements.AddRange(
            CreateStatement(
                userId,
                stream.Id,
                new DateOnly(
                    2026,
                    6,
                    1),
                new DateOnly(
                    2026,
                    6,
                    30),
                104.99m),

            CreateStatement(
                userId,
                stream.Id,
                new DateOnly(
                    2026,
                    7,
                    1),
                new DateOnly(
                    2026,
                    7,
                    31),
                79.99m));

        await dbContext.SaveChangesAsync();

        var service =
            new BillStatementChangeDetectionService(
                dbContext);

        await service.ReconcileAsync(
            userId,
            stream.Id);

        await dbContext.SaveChangesAsync();

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .Where(
                        item =>
                            item.UserId ==
                                userId)
                    .ToListAsync());

        Assert.Equal(
            BillAlertType.BillDecrease,
            alert.AlertType);

        Assert.Equal(
            BillAlertSeverity.Info,
            alert.Severity);

        Assert.Contains(
            "$25.00/month",
            alert.Title);

        Assert.Contains(
            "$300.00/year",
            alert.Message);
    }

    [Fact]
    public async Task ObsoleteChange_RemovesItsAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        var previous =
            CreateStatement(
                userId,
                stream.Id,
                new DateOnly(
                    2026,
                    6,
                    1),
                new DateOnly(
                    2026,
                    6,
                    30),
                79.99m);

        var current =
            CreateStatement(
                userId,
                stream.Id,
                new DateOnly(
                    2026,
                    7,
                    1),
                new DateOnly(
                    2026,
                    7,
                    31),
                104.99m);

        dbContext.BillStreams.Add(
            stream);

        dbContext.BillStatements.AddRange(
            previous,
            current);

        await dbContext.SaveChangesAsync();

        var service =
            new BillStatementChangeDetectionService(
                dbContext);

        await service.ReconcileAsync(
            userId,
            stream.Id);

        await dbContext.SaveChangesAsync();

        Assert.Single(
            await dbContext.BillChanges
                .ToListAsync());

        Assert.Single(
            await dbContext.BillAlerts
                .ToListAsync());

        /*
         * A corrected statement now says there was no price change.
         *
         * The old change and its alert are no longer valid.
         */
        current.TotalAmount =
            79.99m;

        current.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        await service.ReconcileAsync(
            userId,
            stream.Id);

        await dbContext.SaveChangesAsync();

        Assert.Empty(
            await dbContext.BillChanges
                .ToListAsync());

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

    private static BillStatementEntity
        CreateStatement(
            Guid userId,
            Guid billStreamId,
            DateOnly periodStart,
            DateOnly periodEnd,
            decimal totalAmount)
    {
        return new BillStatementEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            BillStreamId =
                billStreamId,

            PeriodStart =
                periodStart,

            PeriodEnd =
                periodEnd,

            StatementDate =
                periodEnd.AddDays(
                    1),

            DueDate =
                periodEnd.AddDays(
                    21),

            TotalAmount =
                totalAmount,

            CurrencyCode =
                "USD",

            RetrievedAtUtc =
                DateTimeOffset.UtcNow,

            CreatedAtUtc =
                DateTimeOffset.UtcNow,

            UpdatedAtUtc =
                DateTimeOffset.UtcNow
        };
    }
}