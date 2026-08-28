using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Bills;

public sealed class BillMonitoringBackgroundOptions
{
    public const string SectionName =
        "BillMonitoring:BackgroundRefresh";

    public bool Enabled
    {
        get;
        set;
    } = true;

    public TimeSpan StartupDelay
    {
        get;
        set;
    } = TimeSpan.FromSeconds(
        30);

    public TimeSpan PollInterval
    {
        get;
        set;
    } = TimeSpan.FromMinutes(
        30);

    public TimeSpan RefreshCadence
    {
        get;
        set;
    } = TimeSpan.FromHours(
        6);

    public int BatchSize
    {
        get;
        set;
    } = 25;
}

public sealed class BillMonitoringRefreshScheduler
{
    private readonly BillWatchDbContext
        _dbContext;

    public BillMonitoringRefreshScheduler(
        BillWatchDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task<IReadOnlyList<Guid>>
        GetDueUserIdsAsync(
            DateTimeOffset now,
            TimeSpan refreshCadence,
            int maxUsers,
            CancellationToken cancellationToken = default)
    {
        if (refreshCadence <=
            TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshCadence),
                "Refresh cadence must be greater than zero.");
        }

        if (maxUsers <=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxUsers),
                "Maximum users must be greater than zero.");
        }

        var cutoff =
            now -
            refreshCadence;

        /*
         * Only Active connections are eligible.
         *
         * RequiresAttention needs user action and should not be
         * repeatedly hammered in the background.
         *
         * Disconnected connections are intentionally ignored.
         */
        return await _dbContext.BankConnections
            .AsNoTracking()
            .Where(
                connection =>
                    connection.Status ==
                        BankConnectionStatus.Active &&
                    (
                        !connection.LastSuccessfulSyncAtUtc
                            .HasValue ||
                        connection.LastSuccessfulSyncAtUtc
                            .Value <=
                            cutoff
                    ))
            .Select(
                connection =>
                    connection.UserId)
            .Distinct()
            .OrderBy(
                userId =>
                    userId)
            .Take(
                maxUsers)
            .ToListAsync(
                cancellationToken);
    }
}

public sealed class BillMonitoringBackgroundService
    : BackgroundService
{
    private static readonly TimeSpan MinimumPollInterval =
        TimeSpan.FromMinutes(
            5);

    private static readonly TimeSpan MinimumRefreshCadence =
        TimeSpan.FromHours(
            1);

    private static readonly TimeSpan BacklogDelay =
        TimeSpan.FromSeconds(
            10);

    private readonly IServiceScopeFactory
        _scopeFactory;

    private readonly BillMonitoringBackgroundOptions
        _options;

    private readonly ILogger<BillMonitoringBackgroundService>
        _logger;

    public BillMonitoringBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<BillMonitoringBackgroundOptions> options,
        ILogger<BillMonitoringBackgroundService> logger)
    {
        _scopeFactory =
            scopeFactory;

        _options =
            options.Value;

        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var startupDelay =
            _options.StartupDelay <
            TimeSpan.Zero
                ? TimeSpan.Zero
                : _options.StartupDelay;

        var pollInterval =
            _options.PollInterval <
            MinimumPollInterval
                ? MinimumPollInterval
                : _options.PollInterval;

        var refreshCadence =
            _options.RefreshCadence <
            MinimumRefreshCadence
                ? MinimumRefreshCadence
                : _options.RefreshCadence;

        var batchSize =
            Math.Clamp(
                _options.BatchSize,
                1,
                100);

        if (startupDelay >
            TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(
                    startupDelay,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken
                    .IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken
               .IsCancellationRequested)
        {
            int candidateCount;

            try
            {
                candidateCount =
                    await RunPassAsync(
                        refreshCadence,
                        batchSize,
                        stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken
                    .IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                /*
                 * Never log tokens, account information, transaction
                 * data, institution names, or user identifiers.
                 */
                _logger.LogError(
                    "The scheduled BillWatch monitoring pass failed with {ExceptionType}.",
                    ex.GetType().Name);

                candidateCount =
                    0;
            }

            var nextDelay =
                candidateCount >=
                batchSize
                    ? BacklogDelay
                    : pollInterval;

            try
            {
                await Task.Delay(
                    nextDelay,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken
                    .IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<int> RunPassAsync(
        TimeSpan refreshCadence,
        int batchSize,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> userIds;

        /*
         * Candidate discovery uses a short-lived DbContext.
         */
        using (var scope =
               _scopeFactory.CreateScope())
        {
            var scheduler =
                scope.ServiceProvider
                    .GetRequiredService<
                        BillMonitoringRefreshScheduler>();

            userIds =
                await scheduler
                    .GetDueUserIdsAsync(
                        DateTimeOffset.UtcNow,
                        refreshCadence,
                        batchSize,
                        cancellationToken);
        }

        foreach (var userId in
                 userIds)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            /*
             * Give each user's refresh its own service scope so EF
             * tracking state cannot leak between users.
             */
            using var scope =
                _scopeFactory.CreateScope();

            var refreshService =
                scope.ServiceProvider
                    .GetRequiredService<
                        BillMonitoringRefreshService>();

            try
            {
                await refreshService.RefreshAsync(
                    userId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken
                    .IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                /*
                 * One connection/user failure must not stop monitoring
                 * for everyone else.
                 */
                _logger.LogWarning(
                    "A scheduled BillWatch bank refresh failed with {ExceptionType}.",
                    ex.GetType().Name);
            }
        }

        return userIds.Count;
    }
}