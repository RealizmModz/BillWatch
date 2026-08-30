using System.Net;
using System.Text;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using BillWatch.Core.Models;
using BillWatch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class AccountDeletionTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public AccountDeletionTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task DeleteAccount_RemovesOwnedFinancialDataAndPreservesOtherUser()
    {
        using var deletingClient =
            _factory.CreateHttpsClient();

        using var remainingClient =
            _factory.CreateHttpsClient();

        var deletingSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    deletingClient);

        var remainingSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    remainingClient);

        TestUserAuthentication.Authorize(
            deletingClient,
            deletingSession);

        Guid deletingUserId;
        Guid remainingUserId;
        Guid deletingUploadId;
        Guid remainingUploadId;
        Guid deletingEvaluationId;
        Guid remainingEvaluationId;
        string deletingStorageKey;
        string remainingStorageKey;

        await using (var setupScope =
                     _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                setupScope.ServiceProvider
                    .GetRequiredService<
                        BillWatchDbContext>();

            var statementStorage =
                setupScope.ServiceProvider
                    .GetRequiredService<
                        SecureBillStatementStorageService>();

            deletingUserId =
                await dbContext.Users
                    .Where(
                        user =>
                            user.Email ==
                                deletingSession.Email)
                    .Select(
                        user =>
                            user.Id)
                    .SingleAsync();

            remainingUserId =
                await dbContext.Users
                    .Where(
                        user =>
                            user.Email ==
                                remainingSession.Email)
                    .Select(
                        user =>
                            user.Id)
                    .SingleAsync();

            var deletingStream =
                CreateBillStream(
                    deletingUserId,
                    "Deleting user provider");

            var remainingStream =
                CreateBillStream(
                    remainingUserId,
                    "Remaining user provider");

            dbContext.BillStreams.AddRange(
                deletingStream,
                remainingStream);

            await dbContext.SaveChangesAsync();

            var deletingFile =
                await StoreTestPdfAsync(
                    statementStorage,
                    deletingUserId);

            var remainingStoredFile =
                await StoreTestPdfAsync(
                    statementStorage,
                    remainingUserId);

            deletingStorageKey =
                deletingFile.StorageKey;

            remainingStorageKey =
                remainingStoredFile.StorageKey;

            var deletingUpload =
                CreateUpload(
                    deletingUserId,
                    deletingStream.Id,
                    deletingFile);

            var remainingUpload =
                CreateUpload(
                    remainingUserId,
                    remainingStream.Id,
                    remainingStoredFile);

            dbContext.BillStatementUploads.AddRange(
                deletingUpload,
                remainingUpload);

            await dbContext.SaveChangesAsync();

            deletingUploadId =
                deletingUpload.Id;

            remainingUploadId =
                remainingUpload.Id;

            var deletingEvaluation =
                CreateAiEvaluation(
                    deletingUserId,
                    deletingUpload.Id);

            var remainingEvaluation =
                CreateAiEvaluation(
                    remainingUserId,
                    remainingUpload.Id);

            dbContext.BillStatementAiEvaluations.AddRange(
                deletingEvaluation,
                remainingEvaluation);

            await dbContext.SaveChangesAsync();

            deletingEvaluationId =
                deletingEvaluation.Id;

            remainingEvaluationId =
                remainingEvaluation.Id;
        }

        using var deleteResponse =
            await deletingClient.DeleteAsync(
                "/api/account");

        Assert.Equal(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode);

        await using var verificationScope =
            _factory.Services.CreateAsyncScope();

        var verificationDbContext =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var verificationStorage =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    SecureBillStatementStorageService>();

        Assert.False(
            await verificationDbContext.Users
                .AnyAsync(
                    user =>
                        user.Id ==
                            deletingUserId));

        Assert.False(
            await verificationDbContext.BillStreams
                .AnyAsync(
                    stream =>
                        stream.UserId ==
                            deletingUserId));

        Assert.False(
            await verificationDbContext.BillStatementUploads
                .AnyAsync(
                    upload =>
                        upload.Id ==
                            deletingUploadId));

        Assert.False(
            await verificationDbContext.BillStatementAiEvaluations
                .AnyAsync(
                    evaluation =>
                        evaluation.Id ==
                            deletingEvaluationId));

        Assert.True(
            await verificationDbContext.Users
                .AnyAsync(
                    user =>
                        user.Id ==
                            remainingUserId));

        Assert.True(
            await verificationDbContext.BillStatementUploads
                .AnyAsync(
                    upload =>
                        upload.Id ==
                            remainingUploadId));

        Assert.True(
            await verificationDbContext.BillStatementAiEvaluations
                .AnyAsync(
                    evaluation =>
                        evaluation.Id ==
                            remainingEvaluationId));

        Assert.Throws<FileNotFoundException>(
            () =>
                verificationStorage.OpenRead(
                    deletingUserId,
                    deletingStorageKey));

        using var remainingFile =
            verificationStorage.OpenRead(
                remainingUserId,
                remainingStorageKey);

        Assert.True(
            remainingFile.Length >
                0);
    }

    private static BillStreamEntity CreateBillStream(
        Guid userId,
        string providerName)
    {
        return new BillStreamEntity
        {
            UserId =
                userId,
            ProviderName =
                providerName,
            Category =
                BillCategory.Internet,
            Source =
                BillStreamSource.Manual
        };
    }

    private static BillStatementUploadEntity CreateUpload(
        Guid userId,
        Guid billStreamId,
        StoredBillStatementFile file)
    {
        return new BillStatementUploadEntity
        {
            UserId =
                userId,
            BillStreamId =
                billStreamId,
            StorageKey =
                file.StorageKey,
            MediaType =
                file.MediaType,
            FileExtension =
                file.FileExtension,
            SizeBytes =
                file.SizeBytes,
            Status =
                BillStatementUploadStatus.Processed
        };
    }

    private static BillStatementAiEvaluationEntity CreateAiEvaluation(
        Guid userId,
        Guid uploadId)
    {
        return new BillStatementAiEvaluationEntity
        {
            UserId =
                userId,
            BillStatementUploadId =
                uploadId,
            Provider =
                "OpenAI",
            Model =
                "test-model",
            PromptVersion =
                "test-prompt-v1",
            Status =
                BillStatementAiEvaluationStatus.Rejected,
            AttemptCount =
                1,
            LastAttemptedAtUtc =
                DateTimeOffset.UtcNow,
            CompletedAtUtc =
                DateTimeOffset.UtcNow
        };
    }

    private static async Task<StoredBillStatementFile> StoreTestPdfAsync(
        SecureBillStatementStorageService storage,
        Guid userId)
    {
        await using var stream =
            new MemoryStream(
                Encoding.ASCII.GetBytes(
                    "%PDF-1.4\n%%EOF"));

        return await storage.StoreAsync(
            userId,
            stream,
            "statement.pdf");
    }
}
