using System.Net;
using System.Text;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Plaid;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Services;

public sealed class PlaidConnectionDisconnectServiceTests
{
    [Fact]
    public async Task OwnedConnection_RevokesProviderItemAndAtomicallyClearsLocalSyncState()
    {
        await using var dbContext =
            CreateDbContext();

        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            BankConnectionStatus.RequiresAttention,
            "access-disconnect-owned");

        dbContext.BankConnections.Add(connection);
        dbContext.BankAccounts.AddRange(
            CreateAccount(userId, connection.Id, "checking-1"),
            CreateAccount(userId, connection.Id, "savings-1"));

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(HttpStatusCode.OK, "{}"));

        using var httpClient = new HttpClient(handler);

        var service = CreateService(
            dbContext,
            httpClient,
            tokenProtector);

        var disconnected =
            await service.DisconnectAsync(
                userId,
                connection.Id);

        Assert.True(disconnected);
        Assert.Equal(
            BankConnectionStatus.Disconnected,
            connection.Status);
        Assert.Null(connection.ProtectedPlaidAccessToken);
        Assert.Null(connection.TransactionsCursor);
        Assert.All(
            dbContext.BankAccounts,
            account => Assert.False(account.IsActive));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "/item/remove",
            request.Uri.AbsolutePath);

        using var requestJson =
            JsonDocument.Parse(request.Body);

        Assert.Equal(
            "access-disconnect-owned",
            requestJson.RootElement
                .GetProperty("access_token")
                .GetString());
    }

    [Fact]
    public async Task OtherUsersConnection_IsIndistinguishableFromMissingAndNeverCallsPlaid()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            ownerId,
            tokenProtector,
            BankConnectionStatus.Active,
            "access-owner-only");

        dbContext.BankConnections.Add(connection);
        dbContext.BankAccounts.Add(
            CreateAccount(ownerId, connection.Id, "owner-checking"));

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler();

        using var httpClient = new HttpClient(handler);

        var service = CreateService(
            dbContext,
            httpClient,
            tokenProtector);

        var disconnected =
            await service.DisconnectAsync(
                attackerId,
                connection.Id);

        Assert.False(disconnected);
        Assert.Empty(handler.Requests);
        Assert.Equal(
            BankConnectionStatus.Active,
            connection.Status);
        Assert.NotNull(connection.ProtectedPlaidAccessToken);
        Assert.Equal(
            "cursor-before-disconnect",
            connection.TransactionsCursor);
        Assert.True(
            await dbContext.BankAccounts
                .Select(account => account.IsActive)
                .SingleAsync());
    }

    [Fact]
    public async Task ProviderItemAlreadyMissing_IsTreatedAsEquivalentSuccessAndClearsCredential()
    {
        await using var dbContext =
            CreateDbContext();

        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            BankConnectionStatus.RequiresAttention,
            "access-provider-missing");

        dbContext.BankConnections.Add(connection);
        dbContext.BankAccounts.Add(
            CreateAccount(userId, connection.Id, "missing-item-account"));

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.BadRequest,
                    """
                    {
                      "error_type": "INVALID_INPUT",
                      "error_code": "ITEM_NOT_FOUND",
                      "request_id": "safe-test-request-id"
                    }
                    """));

        using var httpClient = new HttpClient(handler);

        var service = CreateService(
            dbContext,
            httpClient,
            tokenProtector);

        var disconnected =
            await service.DisconnectAsync(
                userId,
                connection.Id);

        Assert.True(disconnected);
        Assert.Equal(
            BankConnectionStatus.Disconnected,
            connection.Status);
        Assert.Null(connection.ProtectedPlaidAccessToken);
        Assert.Null(connection.TransactionsCursor);
        Assert.False(
            await dbContext.BankAccounts
                .Select(account => account.IsActive)
                .SingleAsync());
    }

    [Fact]
    public async Task ProviderFailureOtherThanItemNotFound_PreservesLocalCredentialAndActiveState()
    {
        await using var dbContext =
            CreateDbContext();

        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            BankConnectionStatus.RequiresAttention,
            "access-provider-failure");

        dbContext.BankConnections.Add(connection);
        dbContext.BankAccounts.Add(
            CreateAccount(userId, connection.Id, "failure-account"));

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.ServiceUnavailable,
                    """
                    {
                      "error_type": "API_ERROR",
                      "error_code": "INTERNAL_SERVER_ERROR",
                      "request_id": "safe-test-request-id"
                    }
                    """));

        using var httpClient = new HttpClient(handler);

        var service = CreateService(
            dbContext,
            httpClient,
            tokenProtector);

        await Assert.ThrowsAsync<PlaidApiException>(
            () => service.DisconnectAsync(
                userId,
                connection.Id));

        Assert.Equal(
            BankConnectionStatus.RequiresAttention,
            connection.Status);
        Assert.Equal(
            "access-provider-failure",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));
        Assert.Equal(
            "cursor-before-disconnect",
            connection.TransactionsCursor);
        Assert.True(
            await dbContext.BankAccounts
                .Select(account => account.IsActive)
                .SingleAsync());
    }

    private static PlaidConnectionDisconnectService CreateService(
        BillWatchDbContext dbContext,
        HttpClient httpClient,
        PlaidTokenProtector tokenProtector)
    {
        return new PlaidConnectionDisconnectService(
            dbContext,
            new PlaidApiClient(
                httpClient,
                Options.Create(
                    new PlaidOptions
                    {
                        ClientId = "test-client-id",
                        Secret = "test-secret",
                        Environment = PlaidOptions.SandboxEnvironment
                    })),
            tokenProtector);
    }

    private static PlaidTokenProtector CreateTokenProtector() =>
        new(
            new EphemeralDataProtectionProvider());

    private static BankConnectionEntity CreateConnection(
        Guid userId,
        PlaidTokenProtector tokenProtector,
        BankConnectionStatus status,
        string plaintextAccessToken)
    {
        return new BankConnectionEntity
        {
            UserId = userId,
            InstitutionName = "Disconnect Test Bank",
            PlaidItemId = "item-disconnect-test",
            ProtectedPlaidAccessToken =
                tokenProtector.Protect(plaintextAccessToken),
            TransactionsCursor = "cursor-before-disconnect",
            Status = status
        };
    }

    private static BankAccountEntity CreateAccount(
        Guid userId,
        Guid connectionId,
        string plaidAccountId)
    {
        return new BankAccountEntity
        {
            UserId = userId,
            BankConnectionId = connectionId,
            PlaidAccountId = plaidAccountId,
            Name = plaidAccountId,
            IsActive = true
        };
    }

    private static BillWatchDbContext CreateDbContext()
    {
        return new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }
}
