using System.Net;
using System.Net.Http.Json;
using BillWatch.API.Authorization;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class AdminAuthorizationIntegrationTests
{
    [Theory]
    [InlineData(BillWatchRoles.Owner)]
    [InlineData(BillWatchRoles.Admin)]
    public async Task AdminEndpoints_AllowOwnerAndAdmin(
        string roleName)
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterWithRoleAndLoginAsync(
                factory,
                client,
                roleName);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var usersResponse =
            await client.GetAsync(
                "/api/admin/users");

        Assert.Equal(
            HttpStatusCode.OK,
            usersResponse.StatusCode);

        using var accessKeysResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.OK,
            accessKeysResponse.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(BillWatchRoles.Moderator)]
    public async Task AdminEndpoints_RejectNonPrivilegedUsers(
        string? roleName)
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            roleName is null
                ? await TestUserAuthentication.RegisterAndLoginAsync(client)
                : await TestUserAuthentication.RegisterWithRoleAndLoginAsync(
                    factory,
                    client,
                    roleName);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var usersResponse =
            await client.GetAsync(
                "/api/admin/users");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            usersResponse.StatusCode);

        using var createKeyResponse =
            await client.PostAsJsonAsync(
                "/api/admin/subscription/access-keys",
                CreateAccessKeyRequest());

        Assert.Equal(
            HttpStatusCode.Forbidden,
            createKeyResponse.StatusCode);
    }

    [Fact]
    public async Task RoleHierarchy_RequiresFreshTokenAndBlocksPrivilegeEscalation()
    {
        using var factory =
            new BillWatchApiFactory();

        using var ownerClient =
            factory.CreateHttpsClient();

        using var adminClient =
            factory.CreateHttpsClient();

        using var targetClient =
            factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterWithRoleAndLoginAsync(
                factory,
                ownerClient,
                BillWatchRoles.Owner);

        var admin =
            await TestUserAuthentication.RegisterWithRoleAndLoginAsync(
                factory,
                adminClient,
                BillWatchRoles.Admin);

        var target =
            await TestUserAuthentication.RegisterAndLoginAsync(
                targetClient);

        var targetUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                target.Email);

        var ownerUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                owner.Email);

        TestUserAuthentication.Authorize(
            targetClient,
            target);

        using var beforePromotion =
            await targetClient.GetAsync(
                "/api/admin/users");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            beforePromotion.StatusCode);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var promoteResponse =
            await ownerClient.PostAsync(
                $"/api/admin/users/{targetUserId:D}/roles/Admin",
                content: null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            promoteResponse.StatusCode);

        using var staleTokenResponse =
            await targetClient.GetAsync(
                "/api/admin/users");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            staleTokenResponse.StatusCode);

        var refreshedTarget =
            await TestUserAuthentication.LoginAsync(
                targetClient,
                target.Email);

        TestUserAuthentication.Authorize(
            targetClient,
            refreshedTarget);

        using var freshTokenResponse =
            await targetClient.GetAsync(
                "/api/admin/users");

        Assert.Equal(
            HttpStatusCode.OK,
            freshTokenResponse.StatusCode);

        TestUserAuthentication.Authorize(
            adminClient,
            admin);

        using var adminEscalationAttempt =
            await adminClient.PostAsync(
                $"/api/admin/users/{targetUserId:D}/roles/Admin",
                content: null);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            adminEscalationAttempt.StatusCode);

        using var adminManageOwnerAttempt =
            await adminClient.PostAsJsonAsync(
                $"/api/admin/users/{ownerUserId:D}/entitlements",
                new
                {
                    tier = "Standard",
                    durationDays = 30,
                    grantsLifetimeAccess = false
                });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            adminManageOwnerAttempt.StatusCode);
    }

    [Fact]
    public async Task AccessKeyLifecycle_CreateRedeemExhaustAndRevoke_IsEnforcedEndToEnd()
    {
        using var factory =
            new BillWatchApiFactory();

        using var ownerClient =
            factory.CreateHttpsClient();

        using var userClient =
            factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterWithRoleAndLoginAsync(
                factory,
                ownerClient,
                BillWatchRoles.Owner);

        var user =
            await TestUserAuthentication.RegisterAndLoginAsync(
                userClient);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var createResponse =
            await ownerClient.PostAsJsonAsync(
                "/api/admin/subscription/access-keys",
                CreateAccessKeyRequest());

        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);

        var created =
            await createResponse.Content
                .ReadFromJsonAsync<CreatedAccessKeyPayload>();

        Assert.NotNull(created);
        Assert.False(
            string.IsNullOrWhiteSpace(
                created!.PlaintextKey));

        using var listResponse =
            await ownerClient.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.OK,
            listResponse.StatusCode);

        var listBody =
            await listResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            created.PlaintextKey,
            listBody,
            StringComparison.Ordinal);

        TestUserAuthentication.Authorize(
            userClient,
            user);

        using var redeemResponse =
            await userClient.PostAsJsonAsync(
                "/api/subscription/access-keys/redeem",
                new
                {
                    accessKey = created.PlaintextKey
                });

        Assert.Equal(
            HttpStatusCode.OK,
            redeemResponse.StatusCode);

        using var exhaustedResponse =
            await userClient.PostAsJsonAsync(
                "/api/subscription/access-keys/redeem",
                new
                {
                    accessKey = created.PlaintextKey
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            exhaustedResponse.StatusCode);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var createRevokedResponse =
            await ownerClient.PostAsJsonAsync(
                "/api/admin/subscription/access-keys",
                CreateAccessKeyRequest());

        Assert.Equal(
            HttpStatusCode.Created,
            createRevokedResponse.StatusCode);

        var revokedKey =
            await createRevokedResponse.Content
                .ReadFromJsonAsync<CreatedAccessKeyPayload>();

        Assert.NotNull(revokedKey);

        using var revokeResponse =
            await ownerClient.PostAsync(
                $"/api/admin/subscription/access-keys/{revokedKey!.Id:D}/revoke",
                content: null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            revokeResponse.StatusCode);

        TestUserAuthentication.Authorize(
            userClient,
            user);

        using var revokedRedeemResponse =
            await userClient.PostAsJsonAsync(
                "/api/subscription/access-keys/redeem",
                new
                {
                    accessKey = revokedKey.PlaintextKey
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            revokedRedeemResponse.StatusCode);
    }

    private static object CreateAccessKeyRequest() =>
        new
        {
            purpose = "Beta",
            tier = "Beta",
            durationDays = 30,
            grantsLifetimeAccess = false,
            maxRedemptions = 1,
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
            label = (string?)null
        };

    private sealed record CreatedAccessKeyPayload(
        Guid Id,
        string PlaintextKey);
}
