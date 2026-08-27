using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillStreamAuthorizationTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory
        _factory;

    public BillStreamAuthorizationTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task
        BillDetail_RequiresAuthentication()
    {
        using var client =
            _factory.CreateHttpsClient();

        var billStreamId =
            Guid.NewGuid();

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{billStreamId}");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task
        BillDetail_OwnerCanReadButAnotherUserCannot()
    {
        using var client =
            _factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        var attacker =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            owner);

        var providerName =
            $"Security Provider {Guid.NewGuid():N}";

        using var createResponse =
            await client.PostAsJsonAsync(
                "/api/bill-streams",
                new
                {
                    providerName,
                    category =
                        "Internet"
                });

        createResponse
            .EnsureSuccessStatusCode();

        var createdBill =
            await createResponse.Content
                .ReadFromJsonAsync<
                    BillStreamPayload>();

        Assert.NotNull(
            createdBill);

        Assert.NotEqual(
            Guid.Empty,
            createdBill.Id);

        TestUserAuthentication.Authorize(
            client,
            owner);

        using var ownerResponse =
            await client.GetAsync(
                $"/api/bill-streams/{createdBill.Id}");

        Assert.Equal(
            HttpStatusCode.OK,
            ownerResponse.StatusCode);

        var ownerDetail =
            await ownerResponse.Content
                .ReadFromJsonAsync<
                    BillStreamPayload>();

        Assert.NotNull(
            ownerDetail);

        Assert.Equal(
            createdBill.Id,
            ownerDetail.Id);

        Assert.Equal(
            providerName,
            ownerDetail.ProviderName);

        TestUserAuthentication.Authorize(
            client,
            attacker);

        using var attackerResponse =
            await client.GetAsync(
                $"/api/bill-streams/{createdBill.Id}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            attackerResponse.StatusCode);
    }

    [Fact]
    public async Task
        BillDetail_NonexistentIdReturnsNotFound()
    {
        using var client =
            _factory.CreateHttpsClient();

        var user =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            user);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{Guid.NewGuid()}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    private sealed class BillStreamPayload
    {
        public Guid Id
        {
            get;
            set;
        }

        public string ProviderName
        {
            get;
            set;
        } =
            string.Empty;

        public string Category
        {
            get;
            set;
        } =
            string.Empty;

        public bool IsActive
        {
            get;
            set;
        }

        public decimal CurrentAmount
        {
            get;
            set;
        }

        public decimal PreviousAverage
        {
            get;
            set;
        }
    }
}