using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class BillMonitoringRefreshSchedulerTests
{
    private static readonly DateTimeOffset Now =
        new(
            2026,
            8,
            27,
            22,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task StaleOrNeverSyncedActiveConnections_AreDue()
    {
        await using var dbContext =
            CreateDbContext();

        var neverSyncedUser =
            Guid.NewGuid();

        var staleUser =
            Guid.NewGuid();

        var freshUser =
            Guid.NewGuid();

        dbContext.BankConnections.AddRange(
            CreateConnection(
                neverSyncedUser,
                BankConnectionStatus.Active,
                lastSuccessfulSyncAtUtc:
                    null),

            CreateConnection(
                staleUser,
                BankConnectionStatus.Active,
                Now.AddHours(
                    -7)),

            CreateConnection(
                freshUser,
                BankConnectionStatus.Active,
                Now.AddHours(
                    -2)));

        await dbContext.SaveChangesAsync();

        var scheduler =
            new BillMonitoringRefreshScheduler(
                dbContext);

        var results =
            await scheduler.GetDueUserIdsAsync(
                Now,
                TimeSpan.FromHours(
                    6),
                maxUsers:
                    25);

        Assert.Contains(
            neverSyncedUser,
            results);

        Assert.Contains(
            staleUser,
            results);

        Assert.DoesNotContain(
            freshUser,
            results);
    }

    [Fact]
    public async Task RequiresAttentionAndDisconnectedConnections_AreNotScheduled()
    {
        await using var dbContext =
            CreateDbContext();

        var activeUser =
            Guid.NewGuid();

        var attentionUser =
            Guid.NewGuid();

        var disconnectedUser =
            Guid.NewGuid();

        dbContext.BankConnections.AddRange(
            CreateConnection(
                activeUser,
                BankConnectionStatus.Active,
                Now.AddDays(
                    -1)),

            CreateConnection(
                attentionUser,
                BankConnectionStatus.RequiresAttention,
                Now.AddDays(
                    -1)),

            CreateConnection(
                disconnectedUser,
                BankConnectionStatus.Disconnected,
                Now.AddDays(
                    -1)));

        await dbContext.SaveChangesAsync();

        var scheduler =
            new BillMonitoringRefreshScheduler(
                dbContext);

        var results =
            await scheduler.GetDueUserIdsAsync(
                Now,
                TimeSpan.FromHours(
                    6),
                25);

        Assert.Single(
            results);

        Assert.Contains(
            activeUser,
            results);

        Assert.DoesNotContain(
            attentionUser,
            results);

        Assert.DoesNotContain(
            disconnectedUser,
            results);
    }

    [Fact]
    public async Task MultipleStaleConnectionsForSameUser_ScheduleUserOnce()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        dbContext.BankConnections.AddRange(
            CreateConnection(
                userId,
                BankConnectionStatus.Active,
                Now.AddHours(
                    -12)),

            CreateConnection(
                userId,
                BankConnectionStatus.Active,
                Now.AddHours(
                    -24)));

        await dbContext.SaveChangesAsync();

        var scheduler =
            new BillMonitoringRefreshScheduler(
                dbContext);

        var results =
            await scheduler.GetDueUserIdsAsync(
                Now,
                TimeSpan.FromHours(
                    6),
                25);

        Assert.Single(
            results);

        Assert.Equal(
            userId,
            results[0]);
    }

    [Fact]
    public async Task BatchSize_IsEnforced()
    {
        await using var dbContext =
            CreateDbContext();

        for (var index = 0;
             index < 10;
             index++)
        {
            dbContext.BankConnections.Add(
                CreateConnection(
                    Guid.NewGuid(),
                    BankConnectionStatus.Active,
                    Now.AddDays(
                        -1)));
        }

        await dbContext.SaveChangesAsync();

        var scheduler =
            new BillMonitoringRefreshScheduler(
                dbContext);

        var results =
            await scheduler.GetDueUserIdsAsync(
                Now,
                TimeSpan.FromHours(
                    6),
                maxUsers:
                    4);

        Assert.Equal(
            4,
            results.Count);
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

    private static BankConnectionEntity
        CreateConnection(
            Guid userId,
            BankConnectionStatus status,
            DateTimeOffset? lastSuccessfulSyncAtUtc)
    {
        return new BankConnectionEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            InstitutionName =
                "Test Bank",

            Status =
                status,

            LastSuccessfulSyncAtUtc =
                lastSuccessfulSyncAtUtc,

            CreatedAtUtc =
                Now.AddDays(
                    -30),

            UpdatedAtUtc =
                Now
        };
    }
}