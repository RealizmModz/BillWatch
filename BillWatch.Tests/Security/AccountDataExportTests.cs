using System.Net;
using System.Net.Http.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Accounts;
using BillWatch.Core.Models;
using BillWatch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class AccountDataExportTests
    : IClassFixture<BillWatchApiFactory>
{
    private const string ProtectedAccessToken =
        "PROTECTED_ACCESS_TOKEN_MUST_NOT_EXPORT";

    private const string TransactionsCursor =
        "TRANSACTIONS_CURSOR_MUST_NOT_EXPORT";

    private const string ProtectedLinkToken =
        "PROTECTED_LINK_TOKEN_MUST_NOT_EXPORT";

    private const string StatementStorageKey =
        "STATEMENT_STORAGE_KEY_MUST_NOT_EXPORT";

    private const string PlaidItemId =
        "PLAID_ITEM_ID_MUST_NOT_EXPORT";

    private const string PlaidInstitutionId =
        "PLAID_INSTITUTION_ID_MUST_NOT_EXPORT";

    private const string PlaidAccountId =
        "PLAID_ACCOUNT_ID_MUST_NOT_EXPORT";

    private const string PlaidTransactionId =
        "PLAID_TRANSACTION_ID_MUST_NOT_EXPORT";

    private const string OtherUserMarker =
        "OTHER_USER_DATA_MUST_NOT_EXPORT";

    private readonly BillWatchApiFactory _factory;

    public AccountDataExportTests(
        BillWatchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExportAccountData_RequiresAuthentication()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/api/account/export");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExportAccountData_ReturnsOnlyOwnedSafeData()
    {
        using var exportingClient =
            _factory.CreateHttpsClient();

        using var otherClient =
            _factory.CreateHttpsClient();

        var exportingSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                exportingClient);

        var otherSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                otherClient);

        TestUserAuthentication.Authorize(
            exportingClient,
            exportingSession);

        Guid streamId;
        Guid connectionId;
        Guid accountId;
        Guid transactionId;
        Guid statementId;
        Guid lineItemId;
        Guid changeId;
        Guid alertId;
        Guid uploadId;
        Guid evaluationId;
        Guid linkSessionId;

        await using (var setupScope =
                     _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                setupScope.ServiceProvider.GetRequiredService<
                    BillWatchDbContext>();

            var exportingUserId =
                await GetUserIdAsync(
                    dbContext,
                    exportingSession.Email);

            var otherUserId =
                await GetUserIdAsync(
                    dbContext,
                    otherSession.Email);

            var now =
                new DateTimeOffset(
                    2026,
                    8,
                    29,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);

            var stream =
                new BillStreamEntity
                {
                    UserId = exportingUserId,
                    ProviderName = "Exported Internet Provider",
                    Category = BillCategory.Internet,
                    Source = BillStreamSource.Manual,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var connection =
                new BankConnectionEntity
                {
                    UserId = exportingUserId,
                    InstitutionName = "Exported Bank",
                    PlaidInstitutionId = PlaidInstitutionId,
                    PlaidItemId = PlaidItemId,
                    ProtectedPlaidAccessToken = ProtectedAccessToken,
                    TransactionsCursor = TransactionsCursor,
                    Status = BankConnectionStatus.Active,
                    LastSuccessfulSyncAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var account =
                new BankAccountEntity
                {
                    UserId = exportingUserId,
                    BankConnectionId = connection.Id,
                    PlaidAccountId = PlaidAccountId,
                    Name = "Exported Checking",
                    OfficialName = "Exported Checking Account",
                    Mask = "1234",
                    AccountType = BankAccountType.Checking,
                    AccountSubtype = "checking",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var transaction =
                new BankTransactionEntity
                {
                    UserId = exportingUserId,
                    BankAccountId = account.Id,
                    BillStreamId = stream.Id,
                    PlaidTransactionId = PlaidTransactionId,
                    Name = "Exported Payment",
                    MerchantName = "Exported Internet Provider",
                    Amount = 89.99m,
                    IsoCurrencyCode = "USD",
                    PostedDate = new DateOnly(2026, 8, 20),
                    AuthorizedDate = new DateOnly(2026, 8, 19),
                    CategoryPrimary = "RENT_AND_UTILITIES",
                    CategoryDetailed = "INTERNET_AND_CABLE",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var statement =
                new BillStatementEntity
                {
                    UserId = exportingUserId,
                    BillStreamId = stream.Id,
                    PeriodStart = new DateOnly(2026, 7, 1),
                    PeriodEnd = new DateOnly(2026, 7, 31),
                    StatementDate = new DateOnly(2026, 8, 1),
                    DueDate = new DateOnly(2026, 8, 20),
                    TotalAmount = 89.99m,
                    CurrencyCode = "USD",
                    ProviderStatementId = "statement-2026-07",
                    RetrievedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var lineItem =
                new BillLineItemEntity
                {
                    UserId = exportingUserId,
                    BillStatementId = statement.Id,
                    Description = "Internet service",
                    Amount = 89.99m,
                    Category = "Service",
                    SortOrder = 0,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var change =
                new BillChangeEntity
                {
                    UserId = exportingUserId,
                    BillStreamId = stream.Id,
                    CurrentStatementId = statement.Id,
                    ChangeType = BillChangeType.TotalIncrease,
                    Confidence = BillChangeConfidence.Confirmed,
                    Description = "Monthly service increased.",
                    PreviousAmount = 79.99m,
                    CurrentAmount = 89.99m,
                    AmountDifference = 10m,
                    AnnualizedImpact = 120m,
                    DetectedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var alert =
                new BillAlertEntity
                {
                    UserId = exportingUserId,
                    BillStreamId = stream.Id,
                    BillChangeId = change.Id,
                    AlertType = BillAlertType.BillIncrease,
                    Severity = BillAlertSeverity.Warning,
                    Title = "Bill increased",
                    Message = "Your monthly bill increased by $10.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var upload =
                new BillStatementUploadEntity
                {
                    UserId = exportingUserId,
                    BillStreamId = stream.Id,
                    BillStatementId = statement.Id,
                    StorageKey = StatementStorageKey,
                    MediaType = "application/pdf",
                    FileExtension = ".pdf",
                    SizeBytes = 12345,
                    Status = BillStatementUploadStatus.Processed,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var evaluation =
                new BillStatementAiEvaluationEntity
                {
                    UserId = exportingUserId,
                    BillStatementUploadId = upload.Id,
                    Provider = "openai",
                    Model = "export-test-model",
                    PromptVersion = "export-test-prompt",
                    Status = BillStatementAiEvaluationStatus.Rejected,
                    AttemptCount = 1,
                    LastAttemptedAtUtc = now,
                    CompletedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var linkSession =
                new PlaidLinkSessionEntity
                {
                    UserId = exportingUserId,
                    ProtectedLinkToken = ProtectedLinkToken,
                    Status = PlaidLinkSessionStatus.Completed,
                    ExpiresAtUtc = now.AddMinutes(30),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CompletedAtUtc = now
                };

            dbContext.AddRange(
                stream,
                connection,
                account,
                transaction,
                statement,
                lineItem,
                change,
                alert,
                upload,
                evaluation,
                linkSession);

            dbContext.AddRange(
                CreateOtherUserGraph(
                    otherUserId,
                    now));

            await dbContext.SaveChangesAsync();

            streamId = stream.Id;
            connectionId = connection.Id;
            accountId = account.Id;
            transactionId = transaction.Id;
            statementId = statement.Id;
            lineItemId = lineItem.Id;
            changeId = change.Id;
            alertId = alert.Id;
            uploadId = upload.Id;
            evaluationId = evaluation.Id;
            linkSessionId = linkSession.Id;
        }

        using var response =
            await exportingClient.GetAsync(
                "/api/account/export");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        Assert.Contains(
            "billwatch-data-export.json",
            response.Content.Headers.ContentDisposition?.FileName ??
            response.Headers.GetValues("Content-Disposition").Single(),
            StringComparison.Ordinal);

        var export =
            await response.Content.ReadFromJsonAsync<
                AccountDataExportResult>();

        Assert.NotNull(export);
        Assert.Equal(
            AccountDataExportBuilder.CurrentSchemaVersion,
            export.SchemaVersion);
        Assert.Equal(
            exportingSession.Email,
            export.Profile.Email);

        Assert.Equal(
            connectionId,
            Assert.Single(export.BankConnections).Id);
        Assert.Equal(
            accountId,
            Assert.Single(export.BankAccounts).Id);
        Assert.Equal(
            transactionId,
            Assert.Single(export.BankTransactions).Id);
        Assert.Equal(
            streamId,
            Assert.Single(export.BillStreams).Id);
        Assert.Equal(
            statementId,
            Assert.Single(export.BillStatements).Id);
        Assert.Equal(
            lineItemId,
            Assert.Single(export.BillLineItems).Id);
        Assert.Equal(
            changeId,
            Assert.Single(export.BillChanges).Id);
        Assert.Equal(
            alertId,
            Assert.Single(export.BillAlerts).Id);
        Assert.Equal(
            uploadId,
            Assert.Single(export.StatementUploads).Id);
        Assert.Equal(
            evaluationId,
            Assert.Single(export.AiEvaluations).Id);
        Assert.Equal(
            linkSessionId,
            Assert.Single(export.PlaidLinkSessions).Id);

        var rawJson =
            await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            ProtectedAccessToken,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TransactionsCursor,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ProtectedLinkToken,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            StatementStorageKey,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaidItemId,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaidInstitutionId,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaidAccountId,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaidTransactionId,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            OtherUserMarker,
            rawJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "passwordHash",
            rawJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "securityStamp",
            rawJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "storageKey",
            rawJson,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Task<Guid> GetUserIdAsync(
        BillWatchDbContext dbContext,
        string email)
    {
        return dbContext.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
    }

    private static object[] CreateOtherUserGraph(
        Guid userId,
        DateTimeOffset now)
    {
        var stream =
            new BillStreamEntity
            {
                UserId = userId,
                ProviderName = OtherUserMarker,
                Category = BillCategory.Other,
                Source = BillStreamSource.Manual,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var connection =
            new BankConnectionEntity
            {
                UserId = userId,
                InstitutionName = OtherUserMarker,
                PlaidItemId = $"{OtherUserMarker}-item",
                Status = BankConnectionStatus.Active,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var account =
            new BankAccountEntity
            {
                UserId = userId,
                BankConnectionId = connection.Id,
                PlaidAccountId = $"{OtherUserMarker}-account",
                Name = OtherUserMarker,
                AccountType = BankAccountType.Checking,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var transaction =
            new BankTransactionEntity
            {
                UserId = userId,
                BankAccountId = account.Id,
                BillStreamId = stream.Id,
                PlaidTransactionId = $"{OtherUserMarker}-transaction",
                Name = OtherUserMarker,
                Amount = 1m,
                PostedDate = new DateOnly(2026, 8, 20),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var statement =
            new BillStatementEntity
            {
                UserId = userId,
                BillStreamId = stream.Id,
                PeriodStart = new DateOnly(2026, 7, 1),
                PeriodEnd = new DateOnly(2026, 7, 31),
                TotalAmount = 1m,
                CurrencyCode = "USD",
                ProviderStatementId = OtherUserMarker,
                RetrievedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var lineItem =
            new BillLineItemEntity
            {
                UserId = userId,
                BillStatementId = statement.Id,
                Description = OtherUserMarker,
                Amount = 1m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var change =
            new BillChangeEntity
            {
                UserId = userId,
                BillStreamId = stream.Id,
                CurrentStatementId = statement.Id,
                ChangeType = BillChangeType.TotalIncrease,
                Confidence = BillChangeConfidence.Confirmed,
                Description = OtherUserMarker,
                PreviousAmount = 0m,
                CurrentAmount = 1m,
                AmountDifference = 1m,
                AnnualizedImpact = 12m,
                DetectedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var alert =
            new BillAlertEntity
            {
                UserId = userId,
                BillStreamId = stream.Id,
                BillChangeId = change.Id,
                AlertType = BillAlertType.BillIncrease,
                Severity = BillAlertSeverity.Info,
                Title = OtherUserMarker,
                Message = OtherUserMarker,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var upload =
            new BillStatementUploadEntity
            {
                UserId = userId,
                BillStreamId = stream.Id,
                BillStatementId = statement.Id,
                StorageKey = $"{OtherUserMarker}-storage",
                MediaType = "application/pdf",
                FileExtension = ".pdf",
                SizeBytes = 1,
                Status = BillStatementUploadStatus.Processed,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var evaluation =
            new BillStatementAiEvaluationEntity
            {
                UserId = userId,
                BillStatementUploadId = upload.Id,
                Provider = OtherUserMarker,
                Model = OtherUserMarker,
                PromptVersion = OtherUserMarker,
                Status = BillStatementAiEvaluationStatus.Rejected,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        var linkSession =
            new PlaidLinkSessionEntity
            {
                UserId = userId,
                ProtectedLinkToken = $"{OtherUserMarker}-link",
                Status = PlaidLinkSessionStatus.Completed,
                ExpiresAtUtc = now.AddMinutes(30),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CompletedAtUtc = now
            };

        return
        [
            stream,
            connection,
            account,
            transaction,
            statement,
            lineItem,
            change,
            alert,
            upload,
            evaluation,
            linkSession
        ];
    }
}
