using BillWatch.API.Data;
using BillWatch.API.Data.Entities;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidLinkService
{
    private readonly PlaidApiClient _plaidApiClient;
    private readonly PlaidTokenProtector _tokenProtector;
    private readonly BillWatchDbContext _dbContext;

    public PlaidLinkService(
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector,
        BillWatchDbContext dbContext)
    {
        _plaidApiClient = plaidApiClient;
        _tokenProtector = tokenProtector;
        _dbContext = dbContext;
    }

    public async Task<PlaidHostedLinkSession> CreateLinkSessionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _plaidApiClient.PostAsync(
                "/link/token/create",
                new
                {
                    client_name = "BillWatch",

                    user = new
                    {
                        client_user_id =
                            userId.ToString()
                    },

                    products = new[]
                    {
                        "transactions"
                    },

                    country_codes = new[]
                    {
                        "US"
                    },

                    language = "en",

                    hosted_link = new
                    {
                    }
                },
                cancellationToken);

        var root = response.RootElement;

        var linkToken =
            root.GetProperty("link_token")
                .GetString();

        var hostedLinkUrl =
            root.GetProperty("hosted_link_url")
                .GetString();

        if (string.IsNullOrWhiteSpace(linkToken))
        {
            throw new InvalidOperationException(
                "Plaid did not return a Link token.");
        }

        if (string.IsNullOrWhiteSpace(hostedLinkUrl))
        {
            throw new InvalidOperationException(
                "Plaid did not return a Hosted Link URL.");
        }

        var expiresAtUtc =
            DateTimeOffset.UtcNow.AddHours(4);

        if (root.TryGetProperty(
                "expiration",
                out var expirationElement))
        {
            var expirationText =
                expirationElement.GetString();

            if (DateTimeOffset.TryParse(
                    expirationText,
                    out var plaidExpiration))
            {
                expiresAtUtc =
                    plaidExpiration.ToUniversalTime();
            }
        }

        var now =
            DateTimeOffset.UtcNow;

        var linkSession =
            new PlaidLinkSessionEntity
            {
                UserId = userId,

                ProtectedLinkToken =
                    _tokenProtector.ProtectLinkToken(
                        linkToken),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    expiresAtUtc,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        _dbContext.PlaidLinkSessions.Add(
            linkSession);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new PlaidHostedLinkSession(
            linkSession.Id,
            linkToken,
            hostedLinkUrl);
    }
}

public sealed record PlaidHostedLinkSession(
    Guid SessionId,
    string LinkToken,
    string HostedLinkUrl);