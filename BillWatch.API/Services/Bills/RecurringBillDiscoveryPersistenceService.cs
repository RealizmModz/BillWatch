using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Services;
using Microsoft.EntityFrameworkCore;

using BillCategory = BillWatch.Core.Models.BillCategory;
using CoreBankTransaction = BillWatch.Core.Models.BankTransaction;

namespace BillWatch.API.Services.Bills;

public sealed class RecurringBillDiscoveryPersistenceService
{
    private readonly BillWatchDbContext _dbContext;

    private readonly BillStreamDiscoveryService
        _discoveryService;

    private readonly SupportedBillCategoryClassifier
        _categoryClassifier;

    public RecurringBillDiscoveryPersistenceService(
        BillWatchDbContext dbContext)
    {
        _dbContext = dbContext;

        _discoveryService =
            new BillStreamDiscoveryService();

        _categoryClassifier =
            new SupportedBillCategoryClassifier();
    }

    public async Task<RecurringBillDiscoveryPersistenceResult>
        DiscoverAndSaveAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        var persistedTransactions =
            await _dbContext.BankTransactions
                .Where(transaction =>
                    transaction.UserId == userId &&
                    !transaction.IsRemoved)
                .OrderBy(transaction =>
                    transaction.PostedDate)
                .ToListAsync(
                    cancellationToken);

        var eligibleTransactions =
            persistedTransactions
                .Where(IsEligibleBillTransaction)
                .ToList();

        var coreTransactions =
            eligibleTransactions
                .Select(ToCoreTransaction)
                .ToList();

        var discoveredStreams =
            _discoveryService.Discover(
                coreTransactions);

        var existingStreams =
            await _dbContext.BillStreams
                .Where(stream =>
                    stream.UserId == userId)
                .ToListAsync(
                    cancellationToken);

        var discoveredProviderNames =
            discoveredStreams
                .Select(stream =>
                    stream.ProviderName)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var createdCount = 0;
        var updatedCount = 0;
        var deactivatedCount = 0;
        var linkedTransactionCount = 0;
        var unlinkedTransactionCount = 0;

        var now =
            DateTimeOffset.UtcNow;

        foreach (var existingStream in existingStreams)
        {
            if (existingStream.Category !=
                BillCategory.Unknown)
            {
                continue;
            }

            if (discoveredProviderNames.Contains(
                    existingStream.ProviderName))
            {
                continue;
            }

            if (existingStream.IsActive)
            {
                existingStream.IsActive =
                    false;

                existingStream.UpdatedAtUtc =
                    now;

                deactivatedCount++;
            }

            foreach (var transaction in persistedTransactions)
            {
                if (transaction.BillStreamId !=
                    existingStream.Id)
                {
                    continue;
                }

                transaction.BillStreamId =
                    null;

                transaction.UpdatedAtUtc =
                    now;

                unlinkedTransactionCount++;
            }
        }

        foreach (var discoveredStream in discoveredStreams)
        {
            var matchingTransactions =
                eligibleTransactions
                    .Where(transaction =>
                        string.Equals(
                            GetMerchantName(
                                transaction),
                            discoveredStream.ProviderName,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (matchingTransactions.Count == 0)
            {
                continue;
            }

            var resolvedCategory =
                ResolveBillCategory(
                    matchingTransactions);

            if (resolvedCategory ==
                BillCategory.Unknown)
            {
                continue;
            }

            var persistedStream =
                existingStreams.FirstOrDefault(
                    existing =>
                        string.Equals(
                            existing.ProviderName,
                            discoveredStream.ProviderName,
                            StringComparison.OrdinalIgnoreCase));

            if (persistedStream is null)
            {
                persistedStream =
                    new BillStreamEntity
                    {
                        UserId = userId,

                        ProviderName =
                            discoveredStream.ProviderName,

                        Category =
                            resolvedCategory,

                        IsActive = true,

                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                _dbContext.BillStreams.Add(
                    persistedStream);

                existingStreams.Add(
                    persistedStream);

                createdCount++;
            }
            else
            {
                var changed = false;

                if (persistedStream.Category ==
                        BillCategory.Unknown &&
                    resolvedCategory !=
                        BillCategory.Unknown)
                {
                    persistedStream.Category =
                        resolvedCategory;

                    changed = true;
                }

                if (!persistedStream.IsActive)
                {
                    persistedStream.IsActive =
                        true;

                    changed = true;
                }

                if (changed)
                {
                    persistedStream.UpdatedAtUtc =
                        now;

                    updatedCount++;
                }
            }

            foreach (var transaction in matchingTransactions)
            {
                if (transaction.BillStreamId ==
                    persistedStream.Id)
                {
                    continue;
                }

                transaction.BillStreamId =
                    persistedStream.Id;

                transaction.UpdatedAtUtc =
                    now;

                linkedTransactionCount++;
            }
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new RecurringBillDiscoveryPersistenceResult(
            TransactionsAnalyzed:
                coreTransactions.Count,

            BillsDiscovered:
                discoveredStreams.Count,

            BillStreamsCreated:
                createdCount,

            BillStreamsUpdated:
                updatedCount,

            BillStreamsDeactivated:
                deactivatedCount,

            TransactionsLinked:
                linkedTransactionCount,

            TransactionsUnlinked:
                unlinkedTransactionCount);
    }

    private bool IsEligibleBillTransaction(
        BankTransactionEntity transaction)
    {
        if (transaction.IsPending)
        {
            return false;
        }

        if (transaction.Amount <= 0m)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                GetMerchantName(
                    transaction)))
        {
            return false;
        }

        return _categoryClassifier.TryClassify(
            transaction.CategoryPrimary,
            transaction.CategoryDetailed,
            out _);
    }

    private BillCategory ResolveBillCategory(
        IReadOnlyCollection<BankTransactionEntity> transactions)
    {
        foreach (var transaction in transactions)
        {
            if (_categoryClassifier.TryClassify(
                    transaction.CategoryPrimary,
                    transaction.CategoryDetailed,
                    out var category))
            {
                return category;
            }
        }

        return BillCategory.Unknown;
    }

    private static CoreBankTransaction ToCoreTransaction(
        BankTransactionEntity transaction)
    {
        return new CoreBankTransaction(
            merchantName:
                GetMerchantName(
                    transaction),

            postedDate:
                transaction.PostedDate,

            amount:
                transaction.Amount,

            isPending:
                transaction.IsPending);
    }

    private static string GetMerchantName(
        BankTransactionEntity transaction)
    {
        var merchantName =
            string.IsNullOrWhiteSpace(
                transaction.MerchantName)
                ? transaction.Name
                : transaction.MerchantName;

        return merchantName.Trim();
    }
}

public sealed record RecurringBillDiscoveryPersistenceResult(
    int TransactionsAnalyzed,
    int BillsDiscovered,
    int BillStreamsCreated,
    int BillStreamsUpdated,
    int BillStreamsDeactivated,
    int TransactionsLinked,
    int TransactionsUnlinked);