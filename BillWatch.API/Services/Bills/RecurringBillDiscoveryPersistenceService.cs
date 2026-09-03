using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Services;
using Microsoft.EntityFrameworkCore;

using BillCategory =
    BillWatch.Core.Models.BillCategory;

using CoreBankTransaction =
    BillWatch.Core.Models.BankTransaction;

namespace BillWatch.API.Services.Bills;

public sealed class RecurringBillDiscoveryPersistenceService
{
    private static readonly HashSet<string>
        StrongFallbackRejectedPrimaryCategories =
            new(
                StringComparer.OrdinalIgnoreCase)
            {
                "BANK_FEES",
                "FOOD_AND_DRINK",
                "GOVERNMENT_AND_NON_PROFIT",
                "HOME_IMPROVEMENT",
                "INCOME",
                "LOAN_DISBURSEMENTS",
                "LOAN_PAYMENTS",
                "MEDICAL",
                "TRANSPORTATION",
                "TRAVEL",
                "TRANSFER_IN",
                "TRANSFER_OUT"
            };

    private readonly BillWatchDbContext
        _dbContext;

    private readonly BillStreamDiscoveryService
        _discoveryService;

    private readonly SupportedBillCategoryClassifier
        _categoryClassifier;

    private readonly BillMerchantNormalizer
        _merchantNormalizer;

    private readonly RecurringBillDiscoveryAlertService
        _discoveryAlertService;

    public RecurringBillDiscoveryPersistenceService(
        BillWatchDbContext dbContext)
    {
        _dbContext =
            dbContext;

        _discoveryService =
            new BillStreamDiscoveryService();

        _categoryClassifier =
            new SupportedBillCategoryClassifier();

        _merchantNormalizer =
            new BillMerchantNormalizer();

        _discoveryAlertService =
            new RecurringBillDiscoveryAlertService(
                dbContext);
    }

    public async Task<RecurringBillDiscoveryPersistenceResult>
        DiscoverAndSaveAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        var persistedTransactions =
            await _dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.UserId ==
                            userId &&
                        !transaction.IsRemoved)
                .OrderBy(
                    transaction =>
                        transaction.PostedDate)
                .ToListAsync(
                    cancellationToken);

        /*
         * Do not discard a transaction merely because Plaid placed it in a
         * category BillWatch does not currently recognize.
         *
         * Cadence is primary evidence that a charge is recurring. Category
         * evidence is evaluated after recurrence is established so a stable
         * subscription is not invisible just because its upstream category
         * is broad or missing.
         */
        var candidateTransactions =
            persistedTransactions
                .Where(
                    IsRecurringCandidateTransaction)
                .ToList();

        var coreTransactions =
            candidateTransactions
                .Select(
                    ToCoreTransaction)
                .ToList();

        var discoveredStreams =
            _discoveryService.Discover(
                coreTransactions);

        var existingStreams =
            await _dbContext.BillStreams
                .Where(
                    stream =>
                        stream.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var discoveredProviderNames =
            discoveredStreams
                .Select(
                    stream =>
                        stream.ProviderName)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var createdCount =
            0;

        var updatedCount =
            0;

        var deactivatedCount =
            0;

        var linkedTransactionCount =
            0;

        var unlinkedTransactionCount =
            0;

        var newBillAlertCount =
            0;

        var now =
            DateTimeOffset.UtcNow;

        foreach (var existingStream in
                 existingStreams)
        {
            if (existingStream.Source !=
                BillStreamSource.AutomaticDiscovery)
            {
                continue;
            }

            var normalizedExistingProvider =
                _merchantNormalizer.Normalize(
                    existingStream.ProviderName);

            if (discoveredProviderNames.Contains(
                    normalizedExistingProvider))
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

            foreach (var transaction in
                     persistedTransactions)
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

        foreach (var discoveredStream in
                 discoveredStreams)
        {
            var matchingTransactions =
                candidateTransactions
                    .Where(
                        transaction =>
                            string.Equals(
                                GetNormalizedMerchantName(
                                    transaction),
                                discoveredStream.ProviderName,
                                StringComparison.OrdinalIgnoreCase))
                    .OrderBy(
                        transaction =>
                            transaction.PostedDate)
                    .ToList();

            if (matchingTransactions.Count ==
                0)
            {
                continue;
            }

            var resolvedCategory =
                ResolveBillCategory(
                    matchingTransactions);

            if (resolvedCategory ==
                    BillCategory.Unknown &&
                IsStrongUnclassifiedRecurringBill(
                    matchingTransactions))
            {
                resolvedCategory =
                    BillCategory.Other;
            }

            if (resolvedCategory ==
                BillCategory.Unknown)
            {
                continue;
            }

            var persistedStream =
                existingStreams
                    .FirstOrDefault(
                        existing =>
                            string.Equals(
                                _merchantNormalizer.Normalize(
                                    existing.ProviderName),
                                discoveredStream.ProviderName,
                                StringComparison.OrdinalIgnoreCase));

            if (persistedStream is
                null)
            {
                persistedStream =
                    new BillStreamEntity
                    {
                        UserId =
                            userId,

                        ProviderName =
                            GetMerchantName(
                                matchingTransactions[0]),

                        Category =
                            resolvedCategory,

                        Source =
                            BillStreamSource.AutomaticDiscovery,

                        IsActive =
                            true,

                        CreatedAtUtc =
                            now,

                        UpdatedAtUtc =
                            now
                    };

                _dbContext.BillStreams.Add(
                    persistedStream);

                existingStreams.Add(
                    persistedStream);

                createdCount++;

                /*
                 * The alert is generated only for a genuinely new
                 * automatically discovered Bill Stream.
                 *
                 * Reactivation or normal rediscovery of an existing
                 * stream does not create another notification.
                 */
                var alertCreated =
                    await _discoveryAlertService
                        .EnsureNewBillAlertAsync(
                            userId,
                            persistedStream,
                            matchingTransactions.Count,
                            now,
                            cancellationToken);

                if (alertCreated)
                {
                    newBillAlertCount++;
                }
            }
            else
            {
                var changed =
                    false;

                var wasAlreadyLinked =
                    matchingTransactions
                        .Any(
                            transaction =>
                                transaction.BillStreamId ==
                                persistedStream.Id);

                if (persistedStream.Source ==
                        BillStreamSource.Unknown &&
                    wasAlreadyLinked)
                {
                    persistedStream.Source =
                        BillStreamSource.AutomaticDiscovery;

                    changed =
                        true;
                }

                if (persistedStream.Category ==
                        BillCategory.Unknown &&
                    resolvedCategory !=
                        BillCategory.Unknown)
                {
                    persistedStream.Category =
                        resolvedCategory;

                    changed =
                        true;
                }

                if (!persistedStream.IsActive)
                {
                    persistedStream.IsActive =
                        true;

                    changed =
                        true;
                }

                if (changed)
                {
                    persistedStream.UpdatedAtUtc =
                        now;

                    updatedCount++;
                }
            }

            foreach (var transaction in
                     matchingTransactions)
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

        /*
         * Bill Streams, transaction links, and discovery alerts commit
         * together.
         */
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
                unlinkedTransactionCount,

            NewBillAlertsCreated:
                newBillAlertCount);
    }

    private bool IsRecurringCandidateTransaction(
        BankTransactionEntity transaction)
    {
        if (transaction.IsPending)
        {
            return false;
        }

        if (transaction.Amount <=
            0m)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(
            GetNormalizedMerchantName(
                transaction));
    }

    private BillCategory ResolveBillCategory(
        IReadOnlyCollection<BankTransactionEntity>
            transactions)
    {
        foreach (var transaction in
                 transactions)
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

    private static bool IsStrongUnclassifiedRecurringBill(
        IReadOnlyCollection<BankTransactionEntity>
            transactions)
    {
        if (transactions.Count < 3)
        {
            return false;
        }

        foreach (var transaction in
                 transactions)
        {
            if (IsExplicitlyRejectedFallbackCategory(
                    transaction))
            {
                return false;
            }
        }

        var averageAmount =
            transactions.Average(
                transaction =>
                    transaction.Amount);

        if (averageAmount <= 0m)
        {
            return false;
        }

        var minimumAmount =
            transactions.Min(
                transaction =>
                    transaction.Amount);

        var maximumAmount =
            transactions.Max(
                transaction =>
                    transaction.Amount);

        var allowedVariation =
            Math.Max(
                1.00m,
                decimal.Round(
                    averageAmount * 0.05m,
                    2,
                    MidpointRounding.AwayFromZero));

        return maximumAmount - minimumAmount <=
            allowedVariation;
    }

    private static bool IsExplicitlyRejectedFallbackCategory(
        BankTransactionEntity transaction)
    {
        var primary =
            transaction.CategoryPrimary?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                primary))
        {
            return false;
        }

        if (StrongFallbackRejectedPrimaryCategories.Contains(
                primary))
        {
            return true;
        }

        if (string.Equals(
                primary,
                "GENERAL_MERCHANDISE",
                StringComparison.OrdinalIgnoreCase))
        {
            return !HasSubscriptionEvidence(
                transaction);
        }

        if (string.Equals(
                primary,
                "PERSONAL_CARE",
                StringComparison.OrdinalIgnoreCase))
        {
            return !ContainsAny(
                transaction.CategoryDetailed,
                "GYM",
                "FITNESS",
                "MEMBERSHIP",
                "SUBSCRIPTION");
        }

        if (string.Equals(
                primary,
                "GENERAL_SERVICES",
                StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(
                transaction.CategoryDetailed,
                "AUTOMOTIVE"))
        {
            return true;
        }

        return false;
    }

    private static bool HasSubscriptionEvidence(
        BankTransactionEntity transaction)
    {
        return ContainsAny(
                   transaction.CategoryDetailed,
                   "SUBSCRIPTION",
                   "MEMBERSHIP",
                   "DIGITAL",
                   "SOFTWARE",
                   "CLOUD") ||
               ContainsAny(
                   transaction.MerchantName,
                   "SUBSCRIPTION",
                   "MEMBERSHIP",
                   " BILL",
                   "BILL ") ||
               ContainsAny(
                   transaction.Name,
                   "SUBSCRIPTION",
                   "MEMBERSHIP",
                   " BILL",
                   "BILL ");
    }

    private static bool ContainsAny(
        string? value,
        params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        return candidates.Any(
            candidate =>
                value.Contains(
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
    }

    private CoreBankTransaction ToCoreTransaction(
        BankTransactionEntity transaction)
    {
        return new CoreBankTransaction(
            merchantName:
                GetNormalizedMerchantName(
                    transaction),

            postedDate:
                transaction.PostedDate,

            amount:
                transaction.Amount,

            isPending:
                transaction.IsPending);
    }

    private string GetNormalizedMerchantName(
        BankTransactionEntity transaction)
    {
        return _merchantNormalizer.Normalize(
            GetMerchantName(
                transaction));
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
    int TransactionsUnlinked,
    int NewBillAlertsCreated);