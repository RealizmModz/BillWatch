using BillWatch.API.Data;
using BillWatch.API.Services.Plaid;

namespace BillWatch.API.Services.Bills;

public sealed class BillMonitoringRefreshService
{
    private readonly PlaidAccountSyncService _accountSyncService;
    private readonly PlaidTransactionSyncService _transactionSyncService;
    private readonly PlaidConnectionSyncCoordinator? _syncCoordinator;
    private readonly RecurringBillDiscoveryPersistenceService _billDiscoveryService;
    private readonly BankConnectionHealthAlertService? _connectionHealthAlertService;
    private readonly ILogger<BillMonitoringRefreshService>? _logger;

    /*
     * Preserve existing direct test construction.
     */
    public BillMonitoringRefreshService(
        PlaidAccountSyncService accountSyncService,
        PlaidTransactionSyncService transactionSyncService,
        RecurringBillDiscoveryPersistenceService billDiscoveryService)
    {
        _accountSyncService = accountSyncService;
        _transactionSyncService = transactionSyncService;
        _billDiscoveryService = billDiscoveryService;
    }

    /*
     * ASP.NET Core DI uses this fuller constructor.
     *
     * Production refreshes use the coordinator so a Plaid Item error that
     * explicitly requires user action is persisted as RequiresAttention
     * before the original provider exception is propagated. Existing direct
     * tests retain the smaller constructor above.
     */
    public BillMonitoringRefreshService(
        PlaidAccountSyncService accountSyncService,
        PlaidTransactionSyncService transactionSyncService,
        RecurringBillDiscoveryPersistenceService billDiscoveryService,
        BillWatchDbContext dbContext,
        ILogger<BillMonitoringRefreshService> logger)
        : this(
            accountSyncService,
            transactionSyncService,
            billDiscoveryService)
    {
        _syncCoordinator =
            new PlaidConnectionSyncCoordinator(
                dbContext,
                accountSyncService,
                transactionSyncService);

        _connectionHealthAlertService =
            new BankConnectionHealthAlertService(dbContext);

        _logger = logger;
    }

    public async Task<RecurringBillDiscoveryPersistenceResult> RefreshAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        try
        {
            /*
             * Always synchronize accounts first.
             *
             * A brand-new Plaid connection may exist before BillWatch has
             * persisted its checking/credit/etc. accounts. Transaction sync
             * depends on those local BankAccount rows.
             */
            if (_syncCoordinator is not null)
            {
                await _syncCoordinator.SyncAllAccountsAsync(
                    userId,
                    cancellationToken);
            }
            else
            {
                await _accountSyncService.SyncAllAccountsAsync(
                    userId,
                    cancellationToken);
            }

            /*
             * Pull new/modified/removed Plaid transactions using each
             * connection's persisted cursor.
             */
            if (_syncCoordinator is not null)
            {
                await _syncCoordinator.SyncAllTransactionsAsync(
                    userId,
                    cancellationToken);
            }
            else
            {
                await _transactionSyncService.SyncAllAsync(
                    userId,
                    cancellationToken);
            }

            /*
             * Re-run deterministic recurring-bill discovery against the
             * newly synchronized transaction history.
             */
            var result =
                await _billDiscoveryService.DiscoverAndSaveAsync(
                    userId,
                    cancellationToken);

            /*
             * Connection alerts are secondary to the core refresh.
             * Failure to create an Activity alert must never cause a
             * successful financial-data refresh to be reported as failed.
             */
            await TryReconcileConnectionHealthAsync(
                userId,
                cancellationToken);

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            /*
             * The coordinator may persist RequiresAttention before
             * propagating a Plaid error. Reconcile that state into the
             * Activity feed, but preserve the original refresh exception.
             */
            await TryReconcileConnectionHealthAsync(
                userId,
                CancellationToken.None);

            throw;
        }
    }

    private async Task TryReconcileConnectionHealthAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_connectionHealthAlertService is null)
        {
            return;
        }

        try
        {
            await _connectionHealthAlertService.ReconcileAsync(
                userId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            /*
             * Never log account data, tokens, connection IDs, institution
             * lists, or financial information here.
             */
            _logger?.LogWarning(
                "Bank connection health alert reconciliation failed with {ExceptionType}.",
                ex.GetType().Name);
        }
    }
}
