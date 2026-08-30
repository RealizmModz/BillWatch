using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Accounts;

public static class AccountDataExportBuilder
{
    public const string CurrentSchemaVersion = "1.0";

    public static async Task<AccountDataExportResult> CreateAsync(
        BillWatchDbContext dbContext,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(user);

        var userId = user.Id;

        var bankConnections =
            await dbContext.BankConnections
                .AsNoTracking()
                .Where(connection => connection.UserId == userId)
                .OrderBy(connection => connection.CreatedAtUtc)
                .ThenBy(connection => connection.Id)
                .ToListAsync(cancellationToken);

        var bankAccounts =
            await dbContext.BankAccounts
                .AsNoTracking()
                .Where(account => account.UserId == userId)
                .OrderBy(account => account.CreatedAtUtc)
                .ThenBy(account => account.Id)
                .ToListAsync(cancellationToken);

        var bankTransactions =
            await dbContext.BankTransactions
                .AsNoTracking()
                .Where(transaction => transaction.UserId == userId)
                .OrderBy(transaction => transaction.PostedDate)
                .ThenBy(transaction => transaction.Id)
                .ToListAsync(cancellationToken);

        var billStreams =
            await dbContext.BillStreams
                .AsNoTracking()
                .Where(stream => stream.UserId == userId)
                .OrderBy(stream => stream.CreatedAtUtc)
                .ThenBy(stream => stream.Id)
                .ToListAsync(cancellationToken);

        var billStatements =
            await dbContext.BillStatements
                .AsNoTracking()
                .Where(statement => statement.UserId == userId)
                .OrderBy(statement => statement.PeriodStart)
                .ThenBy(statement => statement.Id)
                .ToListAsync(cancellationToken);

        var billLineItems =
            await dbContext.BillLineItems
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.BillStatementId)
                .ThenBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var billChanges =
            await dbContext.BillChanges
                .AsNoTracking()
                .Where(change => change.UserId == userId)
                .OrderBy(change => change.DetectedAtUtc)
                .ThenBy(change => change.Id)
                .ToListAsync(cancellationToken);

        var billAlerts =
            await dbContext.BillAlerts
                .AsNoTracking()
                .Where(alert => alert.UserId == userId)
                .OrderBy(alert => alert.CreatedAtUtc)
                .ThenBy(alert => alert.Id)
                .ToListAsync(cancellationToken);

        var statementUploads =
            await dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(upload => upload.UserId == userId)
                .OrderBy(upload => upload.CreatedAtUtc)
                .ThenBy(upload => upload.Id)
                .ToListAsync(cancellationToken);

        var aiEvaluations =
            await dbContext.BillStatementAiEvaluations
                .AsNoTracking()
                .Where(evaluation => evaluation.UserId == userId)
                .OrderBy(evaluation => evaluation.CreatedAtUtc)
                .ThenBy(evaluation => evaluation.Id)
                .ToListAsync(cancellationToken);

        var plaidLinkSessions =
            await dbContext.PlaidLinkSessions
                .AsNoTracking()
                .Where(session => session.UserId == userId)
                .OrderBy(session => session.CreatedAtUtc)
                .ThenBy(session => session.Id)
                .ToListAsync(cancellationToken);

        return new AccountDataExportResult(
            SchemaVersion: CurrentSchemaVersion,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Profile: new AccountProfileExport(
                Email: user.Email ?? string.Empty,
                CreatedAtUtc: user.CreatedAtUtc,
                LastLoginAtUtc: user.LastLoginAtUtc,
                IsActive: user.IsActive),
            BankConnections: bankConnections
                .Select(connection => new BankConnectionExport(
                    connection.Id,
                    connection.InstitutionName,
                    connection.Status.ToString(),
                    connection.LastSuccessfulSyncAtUtc,
                    connection.CreatedAtUtc,
                    connection.UpdatedAtUtc))
                .ToArray(),
            BankAccounts: bankAccounts
                .Select(account => new BankAccountExport(
                    account.Id,
                    account.BankConnectionId,
                    account.Name,
                    account.OfficialName,
                    account.Mask,
                    account.AccountType.ToString(),
                    account.AccountSubtype,
                    account.IsActive,
                    account.CreatedAtUtc,
                    account.UpdatedAtUtc))
                .ToArray(),
            BankTransactions: bankTransactions
                .Select(transaction => new BankTransactionExport(
                    transaction.Id,
                    transaction.BankAccountId,
                    transaction.BillStreamId,
                    transaction.Name,
                    transaction.MerchantName,
                    transaction.Amount,
                    transaction.IsoCurrencyCode,
                    transaction.PostedDate,
                    transaction.AuthorizedDate,
                    transaction.IsPending,
                    transaction.IsRemoved,
                    transaction.CategoryPrimary,
                    transaction.CategoryDetailed,
                    transaction.CreatedAtUtc,
                    transaction.UpdatedAtUtc))
                .ToArray(),
            BillStreams: billStreams
                .Select(stream => new BillStreamExport(
                    stream.Id,
                    stream.ProviderName,
                    stream.Category.ToString(),
                    stream.Source.ToString(),
                    stream.IsActive,
                    stream.CreatedAtUtc,
                    stream.UpdatedAtUtc))
                .ToArray(),
            BillStatements: billStatements
                .Select(statement => new BillStatementExport(
                    statement.Id,
                    statement.BillStreamId,
                    statement.PeriodStart,
                    statement.PeriodEnd,
                    statement.StatementDate,
                    statement.DueDate,
                    statement.TotalAmount,
                    statement.CurrencyCode,
                    statement.ProviderStatementId,
                    statement.RetrievedAtUtc,
                    statement.CreatedAtUtc,
                    statement.UpdatedAtUtc))
                .ToArray(),
            BillLineItems: billLineItems
                .Select(item => new BillLineItemExport(
                    item.Id,
                    item.BillStatementId,
                    item.Description,
                    item.Amount,
                    item.Category,
                    item.SortOrder,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            BillChanges: billChanges
                .Select(change => new BillChangeExport(
                    change.Id,
                    change.BillStreamId,
                    change.PreviousStatementId,
                    change.CurrentStatementId,
                    change.ChangeType.ToString(),
                    change.Confidence.ToString(),
                    change.Description,
                    change.PreviousAmount,
                    change.CurrentAmount,
                    change.AmountDifference,
                    change.AnnualizedImpact,
                    change.IsAcknowledged,
                    change.DetectedAtUtc,
                    change.CreatedAtUtc,
                    change.UpdatedAtUtc))
                .ToArray(),
            BillAlerts: billAlerts
                .Select(alert => new BillAlertExport(
                    alert.Id,
                    alert.BillStreamId,
                    alert.BillChangeId,
                    alert.AlertType.ToString(),
                    alert.Severity.ToString(),
                    alert.Title,
                    alert.Message,
                    alert.IsRead,
                    alert.IsDismissed,
                    alert.CreatedAtUtc,
                    alert.UpdatedAtUtc))
                .ToArray(),
            StatementUploads: statementUploads
                .Select(upload => new StatementUploadExport(
                    upload.Id,
                    upload.BillStreamId,
                    upload.BillStatementId,
                    upload.MediaType,
                    upload.FileExtension,
                    upload.SizeBytes,
                    upload.Status.ToString(),
                    $"/api/bill-streams/{upload.BillStreamId}/statement-uploads/{upload.Id}/file",
                    upload.CreatedAtUtc,
                    upload.UpdatedAtUtc))
                .ToArray(),
            AiEvaluations: aiEvaluations
                .Select(evaluation => new AiEvaluationExport(
                    evaluation.Id,
                    evaluation.BillStatementUploadId,
                    evaluation.Provider,
                    evaluation.Model,
                    evaluation.PromptVersion,
                    evaluation.Status.ToString(),
                    evaluation.AttemptCount,
                    evaluation.CandidateReadyForValidation,
                    evaluation.LastAttemptedAtUtc,
                    evaluation.CompletedAtUtc,
                    evaluation.CreatedAtUtc,
                    evaluation.UpdatedAtUtc))
                .ToArray(),
            PlaidLinkSessions: plaidLinkSessions
                .Select(session => new PlaidLinkSessionExport(
                    session.Id,
                    session.Status.ToString(),
                    session.ExpiresAtUtc,
                    session.CreatedAtUtc,
                    session.UpdatedAtUtc,
                    session.CompletedAtUtc))
                .ToArray());
    }
}

public sealed record AccountDataExportResult(
    string SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    AccountProfileExport Profile,
    IReadOnlyList<BankConnectionExport> BankConnections,
    IReadOnlyList<BankAccountExport> BankAccounts,
    IReadOnlyList<BankTransactionExport> BankTransactions,
    IReadOnlyList<BillStreamExport> BillStreams,
    IReadOnlyList<BillStatementExport> BillStatements,
    IReadOnlyList<BillLineItemExport> BillLineItems,
    IReadOnlyList<BillChangeExport> BillChanges,
    IReadOnlyList<BillAlertExport> BillAlerts,
    IReadOnlyList<StatementUploadExport> StatementUploads,
    IReadOnlyList<AiEvaluationExport> AiEvaluations,
    IReadOnlyList<PlaidLinkSessionExport> PlaidLinkSessions);

public sealed record AccountProfileExport(
    string Email,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    bool IsActive);

public sealed record BankConnectionExport(
    Guid Id,
    string InstitutionName,
    string Status,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BankAccountExport(
    Guid Id,
    Guid BankConnectionId,
    string Name,
    string? OfficialName,
    string? Mask,
    string AccountType,
    string? AccountSubtype,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BankTransactionExport(
    Guid Id,
    Guid BankAccountId,
    Guid? BillStreamId,
    string Name,
    string? MerchantName,
    decimal Amount,
    string? IsoCurrencyCode,
    DateOnly PostedDate,
    DateOnly? AuthorizedDate,
    bool IsPending,
    bool IsRemoved,
    string? CategoryPrimary,
    string? CategoryDetailed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillStreamExport(
    Guid Id,
    string ProviderName,
    string Category,
    string Source,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillStatementExport(
    Guid Id,
    Guid BillStreamId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    decimal TotalAmount,
    string CurrencyCode,
    string? ProviderStatementId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillLineItemExport(
    Guid Id,
    Guid BillStatementId,
    string Description,
    decimal Amount,
    string? Category,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillChangeExport(
    Guid Id,
    Guid BillStreamId,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    string ChangeType,
    string Confidence,
    string Description,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    bool IsAcknowledged,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillAlertExport(
    Guid Id,
    Guid? BillStreamId,
    Guid? BillChangeId,
    string AlertType,
    string Severity,
    string Title,
    string Message,
    bool IsRead,
    bool IsDismissed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record StatementUploadExport(
    Guid Id,
    Guid BillStreamId,
    Guid? BillStatementId,
    string MediaType,
    string FileExtension,
    long SizeBytes,
    string Status,
    string DownloadPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AiEvaluationExport(
    Guid Id,
    Guid BillStatementUploadId,
    string Provider,
    string Model,
    string PromptVersion,
    string Status,
    int AttemptCount,
    bool CandidateReadyForValidation,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PlaidLinkSessionExport(
    Guid Id,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
