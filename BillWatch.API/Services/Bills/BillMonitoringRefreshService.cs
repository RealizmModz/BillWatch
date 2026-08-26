using BillWatch.API.Services.Plaid;

namespace BillWatch.API.Services.Bills;

public sealed class BillMonitoringRefreshService
{
    private readonly PlaidAccountSyncService
        _accountSyncService;

    private readonly PlaidTransactionSyncService
        _transactionSyncService;

    private readonly RecurringBillDiscoveryPersistenceService
        _billDiscoveryService;

    public BillMonitoringRefreshService(
        PlaidAccountSyncService accountSyncService,
        PlaidTransactionSyncService transactionSyncService,
        RecurringBillDiscoveryPersistenceService billDiscoveryService)
    {
        _accountSyncService =
            accountSyncService;

        _transactionSyncService =
            transactionSyncService;

        _billDiscoveryService =
            billDiscoveryService;
    }

    public async Task<RecurringBillDiscoveryPersistenceResult>
        RefreshAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        /*
         * Always synchronize accounts first.
         *
         * A brand-new Plaid connection may exist before BillWatch
         * has persisted its checking/credit/etc. accounts. The
         * transaction sync depends on those local BankAccount rows.
         */
        await _accountSyncService.SyncAllAccountsAsync(
            userId,
            cancellationToken);

        /*
         * Pull new/modified/removed Plaid transactions using the
         * connection's persisted cursor.
         */
        await _transactionSyncService.SyncAllAsync(
            userId,
            cancellationToken);

        /*
         * Re-run deterministic recurring-bill discovery against
         * the newly synchronized transaction history.
         */
        return await _billDiscoveryService.DiscoverAndSaveAsync(
            userId,
            cancellationToken);
    }
}