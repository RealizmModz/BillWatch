using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidConnectionExchangeService
{
    private readonly PlaidApiClient _plaidApiClient;
    private readonly PlaidTokenProtector _tokenProtector;
    private readonly BillWatchDbContext _dbContext;

    public PlaidConnectionExchangeService(
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector,
        BillWatchDbContext dbContext)
    {
        _plaidApiClient = plaidApiClient;
        _tokenProtector = tokenProtector;
        _dbContext = dbContext;
    }

    public async Task<PlaidConnectionResult> ExchangeAndSaveAsync(
        Guid userId,
        string publicToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new ArgumentException(
                "Plaid public token is required.",
                nameof(publicToken));
        }

        using var exchangeResponse =
            await _plaidApiClient.PostAsync(
                "/item/public_token/exchange",
                new
                {
                    public_token = publicToken
                },
                cancellationToken);

        var root = exchangeResponse.RootElement;

        var accessToken =
            root.GetProperty("access_token")
                .GetString();

        var itemId =
            root.GetProperty("item_id")
                .GetString();

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(itemId))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid token exchange response.");
        }

        using var itemResponse =
            await _plaidApiClient.PostAsync(
                "/item/get",
                new
                {
                    access_token = accessToken
                },
                cancellationToken);

        var item =
            itemResponse.RootElement.GetProperty("item");

        var institutionId =
            item.TryGetProperty(
                "institution_id",
                out var institutionIdElement)
                ? institutionIdElement.GetString()
                : null;

        var institutionName =
            item.TryGetProperty(
                "institution_name",
                out var institutionNameElement)
                ? institutionNameElement.GetString()
                : null;

        institutionName =
            string.IsNullOrWhiteSpace(institutionName)
                ? "Connected institution"
                : institutionName;

        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    existing =>
                        existing.UserId == userId &&
                        existing.PlaidItemId == itemId,
                    cancellationToken);

        if (connection is null)
        {
            connection = new BankConnectionEntity
            {
                UserId = userId,
                PlaidItemId = itemId
            };

            _dbContext.BankConnections.Add(connection);
        }

        connection.InstitutionName =
            institutionName;

        connection.PlaidInstitutionId =
            institutionId;

        connection.ProtectedPlaidAccessToken =
            _tokenProtector.Protect(accessToken);

        connection.Status =
            BankConnectionStatus.Active;

        connection.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new PlaidConnectionResult(
            connection.Id,
            connection.InstitutionName,
            connection.Status.ToString());
    }
}

public sealed record PlaidConnectionResult(
    Guid Id,
    string InstitutionName,
    string Status);