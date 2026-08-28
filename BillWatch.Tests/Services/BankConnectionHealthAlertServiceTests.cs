using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class BankConnectionHealthAlertServiceTests
{
    [Fact]
    public async Task RequiresAttention_CreatesWarningAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        dbContext.BankConnections.Add(
            CreateConnection(
                userId,
                "First Platypus Bank",
                "RequiresAttention"));

        await dbContext.SaveChangesAsync();

        var service =
            new BankConnectionHealthAlertService(
                dbContext);

        await service.ReconcileAsync(
            userId);

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .ToListAsync());

        Assert.Equal(
            userId,
            alert.UserId);

        Assert.Equal(
            BillAlertType.ConnectionIssue,
            alert.AlertType);

        Assert.Equal(
            BillAlertSeverity.Warning,
            alert.Severity);

        Assert.Null(
            alert.BillStreamId);

        Assert.Null(
            alert.BillChangeId);

        Assert.Contains(
            "First Platypus Bank",
            alert.Message);

        Assert.False(
            alert.IsRead);

        Assert.False(
            alert.IsDismissed);
    }

    [Fact]
    public async Task MultipleAttentionConnections_UseOneAggregateAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        dbContext.BankConnections.AddRange(
            CreateConnection(
                userId,
                "Bank A",
                "RequiresAttention"),

            CreateConnection(
                userId,
                "Bank B",
                "RequiresAttention"));

        await dbContext.SaveChangesAsync();

        var service =
            new BankConnectionHealthAlertService(
                dbContext);

        await service.ReconcileAsync(
            userId);

        await service.ReconcileAsync(
            userId);

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .ToListAsync());

        Assert.Contains(
            "2 bank connections",
            alert.Message);

        Assert.Contains(
            "Bank A",
            alert.Message);

        Assert.Contains(
            "Bank B",
            alert.Message);
    }

    [Fact]
    public async Task RecoveredConnection_RemovesStaleAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var connection =
            CreateConnection(
                userId,
                "First Platypus Bank",
                "RequiresAttention");

        dbContext.BankConnections.Add(
            connection);

        await dbContext.SaveChangesAsync();

        var service =
            new BankConnectionHealthAlertService(
                dbContext);

        await service.ReconcileAsync(
            userId);

        Assert.Single(
            await dbContext.BillAlerts
                .ToListAsync());

        SetStatus(
            connection,
            "Active");

        connection.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        await service.ReconcileAsync(
            userId);

        Assert.Empty(
            await dbContext.BillAlerts
                .ToListAsync());
    }

    [Fact]
    public async Task DisconnectedAndOtherUsers_DoNotCreateFalseAlert()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        dbContext.BankConnections.AddRange(
            CreateConnection(
                userId,
                "Intentionally Disconnected Bank",
                "Disconnected"),

            CreateConnection(
                otherUserId,
                "Other User Bank",
                "RequiresAttention"));

        await dbContext.SaveChangesAsync();

        var service =
            new BankConnectionHealthAlertService(
                dbContext);

        await service.ReconcileAsync(
            userId);

        Assert.Empty(
            await dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId)
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

    private static BankConnectionEntity
        CreateConnection(
            Guid userId,
            string institutionName,
            string statusName)
    {
        var connection =
            new BankConnectionEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    userId,

                InstitutionName =
                    institutionName,

                CreatedAtUtc =
                    DateTimeOffset.UtcNow,

                UpdatedAtUtc =
                    DateTimeOffset.UtcNow
            };

        /*
         * Keep this test resilient to the concrete enum namespace.
         */
        SetStatus(
            connection,
            statusName);

        /*
         * Populate any additional public string properties with
         * harmless test-only values. This avoids coupling the test to
         * Plaid persistence fields that are irrelevant to health-alert
         * behavior.
         */
        foreach (var property in
                 typeof(BankConnectionEntity)
                     .GetProperties())
        {
            if (property.PropertyType !=
                    typeof(string) ||
                property.SetMethod is
                    null ||
                !property.SetMethod.IsPublic ||
                property.GetValue(
                    connection) is
                    not null)
            {
                continue;
            }

            property.SetValue(
                connection,
                property.Name ==
                    "PlaidItemId"
                    ? $"test-item-{connection.Id:N}"
                    : $"test-{property.Name}");
        }

        return connection;
    }

    private static void SetStatus(
        BankConnectionEntity connection,
        string statusName)
    {
        var property =
            typeof(BankConnectionEntity)
                .GetProperty(
                    "Status")
            ?? throw new InvalidOperationException(
                "BankConnectionEntity.Status was not found.");

        if (!property.PropertyType
            .IsEnum)
        {
            throw new InvalidOperationException(
                "BankConnectionEntity.Status is not an enum.");
        }

        var value =
            Enum.Parse(
                property.PropertyType,
                statusName,
                ignoreCase:
                    false);

        property.SetValue(
            connection,
            value);
    }
}