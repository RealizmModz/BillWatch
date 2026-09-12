using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillStreamAuthorizationTests : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public BillStreamAuthorizationTests(BillWatchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BillDetail_RequiresAuthentication()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.GetAsync($"/api/bill-streams/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BillCreation_RequiresAuthentication()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName = "Private Provider", category = "Internet" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BillDetail_OwnerCanReadButAnotherUserCannot()
    {
        using var client = _factory.CreateHttpsClient();
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var attacker = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, owner);
        var providerName = $"Security Provider {Guid.NewGuid():N}";

        using var createResponse = await client.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName, category = "Internet" });
        createResponse.EnsureSuccessStatusCode();
        var createdBill = await createResponse.Content.ReadFromJsonAsync<BillStreamPayload>();
        Assert.NotNull(createdBill);
        Assert.NotEqual(Guid.Empty, createdBill.Id);

        using var ownerResponse = await client.GetAsync($"/api/bill-streams/{createdBill.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var ownerDetail = await ownerResponse.Content.ReadFromJsonAsync<BillStreamPayload>();
        Assert.NotNull(ownerDetail);
        Assert.Equal(createdBill.Id, ownerDetail.Id);
        Assert.Equal(providerName, ownerDetail.ProviderName);

        TestUserAuthentication.Authorize(client, attacker);
        using var attackerResponse = await client.GetAsync($"/api/bill-streams/{createdBill.Id}");
        Assert.Equal(HttpStatusCode.NotFound, attackerResponse.StatusCode);
    }

    [Fact]
    public async Task BillDetail_EmptyIdReturnsNotFound()
    {
        using var client = _factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);
        using var response = await client.GetAsync($"/api/bill-streams/{Guid.Empty}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BillDetail_NonexistentIdReturnsNotFound()
    {
        using var client = _factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);
        using var response = await client.GetAsync($"/api/bill-streams/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BillCreation_RejectsMissingProviderName(string providerName)
    {
        using var client = _factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);
        using var response = await client.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName, category = "Internet" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BillCreation_RejectsOversizedProviderName()
    {
        using var client = _factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);
        using var response = await client.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName = new string('x', 201), category = "Internet" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BillCreation_RejectsInvalidCategory()
    {
        using var client = _factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);
        using var response = await client.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName = "Provider", category = "not-a-category" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class BillStreamPayload
    {
        public Guid Id { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public decimal CurrentAmount { get; set; }
        public decimal PreviousAverage { get; set; }
    }
}
