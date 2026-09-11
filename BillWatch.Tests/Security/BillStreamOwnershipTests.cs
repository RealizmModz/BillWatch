using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Core.Models;
using BillWatch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class BillStreamOwnershipTests
{
    [Fact]
    public async Task Detail_ForAnotherUsersStream_ReturnsNotFound()
    {
        await using var factory = new BillWatchApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var attackerClient = factory.CreateHttpsClient();

        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var attacker = await TestUserAuthentication.RegisterAndLoginAsync(attackerClient);
        var ownerUserId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        var streamId = await SeedStreamAsync(factory, ownerUserId);

        attackerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", attacker.AccessToken);

        using var response = await attackerClient.GetAsync($"/api/bill-streams/{streamId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_DoesNotReturnAnotherUsersStreams()
    {
        await using var factory = new BillWatchApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var attackerClient = factory.CreateHttpsClient();

        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var attacker = await TestUserAuthentication.RegisterAndLoginAsync(attackerClient);
        var ownerUserId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        var attackerUserId = await TestUserAuthentication.GetUserIdAsync(factory, attacker.Email);
        var ownerStreamId = await SeedStreamAsync(factory, ownerUserId, "Owner Utility");
        var attackerStreamId = await SeedStreamAsync(factory, attackerUserId, "Attacker Utility");

        attackerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", attacker.AccessToken);

        using var response = await attackerClient.GetAsync("/api/bill-streams?includeInactive=true");
        response.EnsureSuccessStatusCode();
        var streams = await response.Content.ReadFromJsonAsync<List<BillStreamResultDto>>();

        Assert.NotNull(streams);
        Assert.Contains(streams, stream => stream.Id == attackerStreamId);
        Assert.DoesNotContain(streams, stream => stream.Id == ownerStreamId);
    }

    [Fact]
    public async Task Create_DuplicateProviderName_IsScopedToCurrentUser()
    {
        await using var factory = new BillWatchApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var second = await TestUserAuthentication.RegisterAndLoginAsync(secondClient);
        var ownerUserId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        await SeedStreamAsync(factory, ownerUserId, "Shared Provider");

        secondClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", second.AccessToken);

        using var response = await secondClient.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName = "Shared Provider", category = "Utilities" });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<BillStreamResultDto>();
        Assert.NotNull(created);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillWatchDbContext>();
        var matchingStreams = await dbContext.BillStreams
            .Where(stream => stream.ProviderName == "Shared Provider")
            .ToListAsync();

        Assert.Equal(2, matchingStreams.Count);
        Assert.Contains(matchingStreams, stream => stream.UserId == ownerUserId);
        Assert.Contains(matchingStreams, stream => stream.Id == created.Id && stream.UserId != ownerUserId);
    }

    private static async Task<Guid> SeedStreamAsync(
        BillWatchApiFactory factory,
        Guid userId,
        string providerName = "Private Utility")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillWatchDbContext>();
        var now = DateTimeOffset.UtcNow;
        var stream = new BillStreamEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProviderName = providerName,
            Category = BillCategory.Utilities,
            Source = BillStreamSource.Manual,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.BillStreams.Add(stream);
        await dbContext.SaveChangesAsync();
        return stream.Id;
    }

    private sealed record BillStreamResultDto(
        Guid Id,
        string ProviderName,
        string Category,
        bool IsActive,
        decimal CurrentAmount,
        decimal PreviousAverage);
}
