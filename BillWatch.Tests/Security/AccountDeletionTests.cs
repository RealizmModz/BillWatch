using System.Net;
using System.Net.Http.Json;
using System.Text;
using BillWatch.API.Authorization;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using BillWatch.Core.Models;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class AccountDeletionTests
    : IClassFixture<BillWatchApiFactory>
{
    private const string TestPassword =
        "BillWatch!Tests123";

    private readonly BillWatchApiFactory _factory;

    public AccountDeletionTests(
        BillWatchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteAccount_RequiresLiteralConfirmation()
    {
        using var client = _factory.CreateHttpsClient();

        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);

        var userId = await TestUserAuthentication.GetUserIdAsync(
            _factory,
            session.Email);

        using var response = await SendDeleteAccountAsync(
            client,
            confirmation: "delete",
            currentPassword: TestPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await UserExistsAsync(_factory, userId));
    }

    [Fact]
    public async Task DeleteAccount_RequiresCurrentPassword()
    {
        using var client = _factory.CreateHttpsClient();

        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);

        var userId = await TestUserAuthentication.GetUserIdAsync(
            _factory,
            session.Email);

        using var response = await SendDeleteAccountAsync(
            client,
            confirmation: "DELETE",
            currentPassword: "BillWatch!Wrong123");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(await UserExistsAsync(_factory, userId));
    }

    [Fact]
    public async Task DeleteAccount_RequiresAuthenticatorCodeWhenTwoFactorIsEnabled()
    {
        using var client = _factory.CreateHttpsClient();

        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);

        Guid userId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(session.Email);
            Assert.NotNull(user);

            userId = user!.Id;

            var result = await userManager.SetTwoFactorEnabledAsync(
                user,
                true);

            Assert.True(result.Succeeded);
        }

        using var response = await SendDeleteAccountAsync(
            client,
            confirmation: "DELETE",
            currentPassword: TestPassword,
            twoFactorCode: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(await UserExistsAsync(_factory, userId));
    }

    [Fact]
    public async Task DeleteAccount_RemovesOwnedFinancialDataAndPreservesOtherUser()
    {
        using var deletingClient = _factory.CreateHttpsClient();
        using var remainingClient = _factory.CreateHttpsClient();

        var deletingSession =
            await TestUserAuthentication.RegisterAndLoginAsync(deletingClient);

        var remainingSession =
            await TestUserAuthentication.RegisterAndLoginAsync(remainingClient);

        TestUserAuthentication.Authorize(deletingClient, deletingSession);

        Guid deletingUserId;
        Guid remainingUserId;
        Guid deletingUploadId;
        Guid remainingUploadId;
        Guid deletingEvaluationId;
        Guid remainingEvaluationId;
        string deletingStorageKey;
        string remainingStorageKey;

        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                setupScope.ServiceProvider.GetRequiredService<BillWatchDbContext>();

            var statementStorage =
                setupScope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

            deletingUserId = await dbContext.Users
                .Where(user => user.Email == deletingSession.Email)
                .Select(user => user.Id)
                .SingleAsync();

            remainingUserId = await dbContext.Users
                .Where(user => user.Email == remainingSession.Email)
                .Select(user => user.Id)
                .SingleAsync();

            var deletingStream = CreateBillStream(
                deletingUserId,
                "Deleting user provider");

            var remainingStream = CreateBillStream(
                remainingUserId,
                "Remaining user provider");

            dbContext.BillStreams.AddRange(
                deletingStream,
                remainingStream);

            await dbContext.SaveChangesAsync();

            var deletingFile = await StoreTestPdfAsync(
                statementStorage,
                deletingUserId);

            var remainingStoredFile = await StoreTestPdfAsync(
                statementStorage,
                remainingUserId);

            deletingStorageKey = deletingFile.StorageKey;
            remainingStorageKey = remainingStoredFile.StorageKey;

            var deletingUpload = CreateUpload(
                deletingUserId,
                deletingStream.Id,
                deletingFile);

            var remainingUpload = CreateUpload(
                remainingUserId,
                remainingStream.Id,
                remainingStoredFile);

            dbContext.BillStatementUploads.AddRange(
                deletingUpload,
                remainingUpload);

            await dbContext.SaveChangesAsync();

            deletingUploadId = deletingUpload.Id;
            remainingUploadId = remainingUpload.Id;

            var deletingEvaluation = CreateAiEvaluation(
                deletingUserId,
                deletingUpload.Id);

            var remainingEvaluation = CreateAiEvaluation(
                remainingUserId,
                remainingUpload.Id);

            dbContext.BillStatementAiEvaluations.AddRange(
                deletingEvaluation,
                remainingEvaluation);

            await dbContext.SaveChangesAsync();

            deletingEvaluationId = deletingEvaluation.Id;
            remainingEvaluationId = remainingEvaluation.Id;
        }

        using var deleteResponse = await SendDeleteAccountAsync(
            deletingClient,
            confirmation: "DELETE",
            currentPassword: TestPassword);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();

        var verificationDbContext =
            verificationScope.ServiceProvider.GetRequiredService<BillWatchDbContext>();

        var verificationStorage =
            verificationScope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

        Assert.False(await verificationDbContext.Users.AnyAsync(
            user => user.Id == deletingUserId));

        Assert.False(await verificationDbContext.BillStreams.AnyAsync(
            stream => stream.UserId == deletingUserId));

        Assert.False(await verificationDbContext.BillStatementUploads.AnyAsync(
            upload => upload.Id == deletingUploadId));

        Assert.False(await verificationDbContext.BillStatementAiEvaluations.AnyAsync(
            evaluation => evaluation.Id == deletingEvaluationId));

        Assert.True(await verificationDbContext.Users.AnyAsync(
            user => user.Id == remainingUserId));

        Assert.True(await verificationDbContext.BillStatementUploads.AnyAsync(
            upload => upload.Id == remainingUploadId));

        Assert.True(await verificationDbContext.BillStatementAiEvaluations.AnyAsync(
            evaluation => evaluation.Id == remainingEvaluationId));

        Assert.Throws<FileNotFoundException>(
            () => verificationStorage.OpenRead(
                deletingUserId,
                deletingStorageKey));

        using var remainingFile = verificationStorage.OpenRead(
            remainingUserId,
            remainingStorageKey);

        Assert.True(remainingFile.Length > 0);
    }

    [Fact]
    public async Task DeleteAccount_RemovesSubscriptionStateButDoesNotRecycleAccessKey()
    {
        using var deletingClient = _factory.CreateHttpsClient();
        using var creatorClient = _factory.CreateHttpsClient();

        var deletingSession =
            await TestUserAuthentication.RegisterAndLoginAsync(deletingClient);

        var creatorSession =
            await TestUserAuthentication.RegisterAndLoginAsync(creatorClient);

        TestUserAuthentication.Authorize(deletingClient, deletingSession);

        Guid deletingUserId;
        Guid creatorUserId;
        Guid accessKeyId;
        Guid entitlementId;
        Guid redemptionId;
        Guid membershipId;

        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                setupScope.ServiceProvider.GetRequiredService<BillWatchDbContext>();

            deletingUserId = await dbContext.Users
                .Where(user => user.Email == deletingSession.Email)
                .Select(user => user.Id)
                .SingleAsync();

            creatorUserId = await dbContext.Users
                .Where(user => user.Email == creatorSession.Email)
                .Select(user => user.Id)
                .SingleAsync();

            var accessKey = new SubscriptionAccessKeyEntity
            {
                KeyHash = new string('a', 64),
                DisplayPrefix = "BW-TEST",
                Purpose = SubscriptionAccessKeyPurpose.Beta,
                Tier = BillWatchSubscriptionTier.Beta,
                DurationDays = 30,
                GrantsLifetimeAccess = false,
                MaxRedemptions = 1,
                RedemptionCount = 1,
                CreatedByUserId = creatorUserId
            };

            var entitlement = new SubscriptionEntitlementEntity
            {
                UserId = deletingUserId,
                Tier = BillWatchSubscriptionTier.Beta,
                Source = SubscriptionEntitlementSource.AccessKey,
                StartsAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                EndsAtUtc = DateTimeOffset.UtcNow.AddDays(30)
            };

            var membership = new UserProgramMembershipEntity
            {
                UserId = deletingUserId,
                Program = UserProgramType.BetaTester,
                StartsAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                IsActive = true,
                GrantedByUserId = creatorUserId
            };

            dbContext.SubscriptionAccessKeys.Add(accessKey);
            dbContext.SubscriptionEntitlements.Add(entitlement);
            dbContext.UserProgramMemberships.Add(membership);

            await dbContext.SaveChangesAsync();

            var redemption = new SubscriptionAccessKeyRedemptionEntity
            {
                AccessKeyId = accessKey.Id,
                UserId = deletingUserId,
                EntitlementId = entitlement.Id
            };

            dbContext.SubscriptionAccessKeyRedemptions.Add(redemption);
            await dbContext.SaveChangesAsync();

            accessKeyId = accessKey.Id;
            entitlementId = entitlement.Id;
            redemptionId = redemption.Id;
            membershipId = membership.Id;
        }

        using var response = await SendDeleteAccountAsync(
            deletingClient,
            confirmation: "DELETE",
            currentPassword: TestPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();

        var verificationDbContext =
            verificationScope.ServiceProvider.GetRequiredService<BillWatchDbContext>();

        Assert.False(await verificationDbContext.Users.AnyAsync(
            user => user.Id == deletingUserId));

        Assert.False(await verificationDbContext.SubscriptionEntitlements.AnyAsync(
            entitlement => entitlement.Id == entitlementId));

        Assert.False(await verificationDbContext.SubscriptionAccessKeyRedemptions.AnyAsync(
            redemption => redemption.Id == redemptionId));

        Assert.False(await verificationDbContext.UserProgramMemberships.AnyAsync(
            membership => membership.Id == membershipId));

        var retainedKey = await verificationDbContext.SubscriptionAccessKeys
            .SingleAsync(accessKey => accessKey.Id == accessKeyId);

        Assert.Equal(1, retainedKey.RedemptionCount);

        Assert.True(await verificationDbContext.Users.AnyAsync(
            user => user.Id == creatorUserId));
    }

    [Theory]
    [InlineData(BillWatchRoles.Owner)]
    [InlineData(BillWatchRoles.Admin)]
    [InlineData(BillWatchRoles.Moderator)]
    public async Task DeleteAccount_RejectsStaffIdentityWhileRoleIsAssigned(
        string roleName)
    {
        using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterWithRoleAndLoginAsync(
                factory,
                client,
                roleName);

        TestUserAuthentication.Authorize(client, session);

        var userId = await TestUserAuthentication.GetUserIdAsync(
            factory,
            session.Email);

        using var response = await SendDeleteAccountAsync(
            client,
            confirmation: "DELETE",
            currentPassword: TestPassword);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await UserExistsAsync(factory, userId));
    }

    private static async Task<HttpResponseMessage> SendDeleteAccountAsync(
        HttpClient client,
        string confirmation,
        string currentPassword,
        string? twoFactorCode = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/account")
        {
            Content = JsonContent.Create(
                new
                {
                    confirmation,
                    currentPassword,
                    twoFactorCode
                })
        };

        return await client.SendAsync(request);
    }

    private static async Task<bool> UserExistsAsync(
        BillWatchApiFactory factory,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<BillWatchDbContext>();

        return await dbContext.Users.AnyAsync(user => user.Id == userId);
    }

    private static BillStreamEntity CreateBillStream(
        Guid userId,
        string providerName)
    {
        return new BillStreamEntity
        {
            UserId = userId,
            ProviderName = providerName,
            Category = BillCategory.Internet,
            Source = BillStreamSource.Manual
        };
    }

    private static BillStatementUploadEntity CreateUpload(
        Guid userId,
        Guid billStreamId,
        StoredBillStatementFile file)
    {
        return new BillStatementUploadEntity
        {
            UserId = userId,
            BillStreamId = billStreamId,
            StorageKey = file.StorageKey,
            MediaType = file.MediaType,
            FileExtension = file.FileExtension,
            SizeBytes = file.SizeBytes,
            Status = BillStatementUploadStatus.Processed
        };
    }

    private static BillStatementAiEvaluationEntity CreateAiEvaluation(
        Guid userId,
        Guid uploadId)
    {
        return new BillStatementAiEvaluationEntity
        {
            UserId = userId,
            BillStatementUploadId = uploadId,
            Provider = "OpenAI",
            Model = "test-model",
            PromptVersion = "test-prompt-v1",
            Status = BillStatementAiEvaluationStatus.Rejected,
            AttemptCount = 1,
            LastAttemptedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task<StoredBillStatementFile> StoreTestPdfAsync(
        SecureBillStatementStorageService storage,
        Guid userId)
    {
        await using var stream = new MemoryStream(
            Encoding.ASCII.GetBytes(
                "%PDF-1.4\n%%EOF"));

        return await storage.StoreAsync(
            userId,
            stream,
            "statement.pdf");
    }
}
