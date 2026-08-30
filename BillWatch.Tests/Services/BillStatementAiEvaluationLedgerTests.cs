using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiEvaluationLedgerTests
{
    [Fact]
    public async Task SameCostKey_CanBeginOnlyOnce()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var ledger =
            CreateLedger(
                dbContext);

        var first =
            await ledger.TryBeginAsync(
                userId,
                uploadId,
                "OpenAI",
                "gpt-4.1-mini",
                "bill-statement-extraction-v1");

        var second =
            await ledger.TryBeginAsync(
                userId,
                uploadId,
                "OpenAI",
                "gpt-4.1-mini",
                "bill-statement-extraction-v1");

        Assert.Equal(
            BillStatementAiEvaluationStartOutcome.Started,
            first.Outcome);

        Assert.Equal(
            BillStatementAiEvaluationStartOutcome.AlreadyExists,
            second.Outcome);

        Assert.Equal(
            first.EvaluationId,
            second.EvaluationId);

        var evaluation =
            await dbContext.BillStatementAiEvaluations
                .SingleAsync();

        Assert.Equal(
            1,
            evaluation.AttemptCount);

        Assert.Equal(
            BillStatementAiEvaluationStatus.InProgress,
            evaluation.Status);
    }

    [Fact]
    public async Task OtherUsersUpload_IsIndistinguishableFromMissingUpload()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerUserId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                ownerUserId);

        var result =
            await CreateLedger(
                    dbContext)
                .TryBeginAsync(
                    Guid.NewGuid(),
                    uploadId,
                    "OpenAI",
                    "gpt-4.1-mini",
                    "bill-statement-extraction-v1");

        Assert.Equal(
            BillStatementAiEvaluationStartOutcome.UploadNotFound,
            result.Outcome);

        Assert.Empty(
            dbContext.BillStatementAiEvaluations);
    }

    [Fact]
    public async Task Completion_IsOwnershipScopedAndTerminal()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var ledger =
            CreateLedger(
                dbContext);

        var start =
            await ledger.TryBeginAsync(
                userId,
                uploadId,
                "OpenAI",
                "gpt-4.1-mini",
                "bill-statement-extraction-v1");

        var evaluationId =
            Assert.IsType<Guid>(
                start.EvaluationId);

        var otherUserCompleted =
            await ledger.CompleteAsync(
                Guid.NewGuid(),
                evaluationId,
                BillStatementAiEvaluationStatus.AcceptedForShadowReview,
                candidateReadyForValidation:
                    true);

        var ownerCompleted =
            await ledger.CompleteAsync(
                userId,
                evaluationId,
                BillStatementAiEvaluationStatus.AcceptedForShadowReview,
                candidateReadyForValidation:
                    true);

        var completedTwice =
            await ledger.CompleteAsync(
                userId,
                evaluationId,
                BillStatementAiEvaluationStatus.ProviderFailed,
                candidateReadyForValidation:
                    false);

        Assert.False(
            otherUserCompleted);

        Assert.True(
            ownerCompleted);

        Assert.False(
            completedTwice);

        var evaluation =
            await dbContext.BillStatementAiEvaluations
                .SingleAsync();

        Assert.Equal(
            BillStatementAiEvaluationStatus.AcceptedForShadowReview,
            evaluation.Status);

        Assert.True(
            evaluation.CandidateReadyForValidation);

        Assert.NotNull(
            evaluation.CompletedAtUtc);
    }

    [Fact]
    public async Task NewPromptVersion_HasIndependentCostKey()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var ledger =
            CreateLedger(
                dbContext);

        var first =
            await ledger.TryBeginAsync(
                userId,
                uploadId,
                "OpenAI",
                "gpt-4.1-mini",
                "bill-statement-extraction-v1");

        var nextVersion =
            await ledger.TryBeginAsync(
                userId,
                uploadId,
                "OpenAI",
                "gpt-4.1-mini",
                "bill-statement-extraction-v2");

        Assert.Equal(
            BillStatementAiEvaluationStartOutcome.Started,
            first.Outcome);

        Assert.Equal(
            BillStatementAiEvaluationStartOutcome.Started,
            nextVersion.Outcome);

        Assert.Equal(
            2,
            await dbContext.BillStatementAiEvaluations.CountAsync());
    }

    private static BillWatchDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(
                    $"ai-evaluation-ledger-{Guid.NewGuid():N}")
                .Options;

        return new BillWatchDbContext(
            options);
    }

    private static BillStatementAiEvaluationLedger CreateLedger(
        BillWatchDbContext dbContext)
    {
        return new BillStatementAiEvaluationLedger(
            dbContext,
            TimeProvider.System);
    }

    private static async Task<Guid> AddUploadAsync(
        BillWatchDbContext dbContext,
        Guid userId)
    {
        var upload =
            new BillStatementUploadEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    Guid.NewGuid(),

                StorageKey =
                    $"test/{Guid.NewGuid():N}",

                MediaType =
                    "application/pdf",

                FileExtension =
                    ".pdf",

                SizeBytes =
                    100
            };

        dbContext.BillStatementUploads.Add(
            upload);

        await dbContext.SaveChangesAsync();

        return upload.Id;
    }
}
