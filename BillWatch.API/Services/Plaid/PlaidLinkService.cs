using System.Globalization;
using System.Text.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidLinkService
{
    private const int MaxLinkTokenLength =
        8 * 1024;

    private const int MaxHostedLinkUrlLength =
        4 * 1024;

    private static readonly TimeSpan
        DefaultLinkSessionLifetime =
            TimeSpan.FromHours(
                4);

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    private readonly BillWatchDbContext
        _dbContext;

    public PlaidLinkService(
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector,
        BillWatchDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(
            plaidApiClient);

        ArgumentNullException.ThrowIfNull(
            tokenProtector);

        ArgumentNullException.ThrowIfNull(
            dbContext);

        _plaidApiClient =
            plaidApiClient;

        _tokenProtector =
            tokenProtector;

        _dbContext =
            dbContext;
    }

    public async Task<PlaidHostedLinkSession>
        CreateLinkSessionAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var response =
            await _plaidApiClient.PostAsync(
                "link/token/create",
                new
                {
                    client_name =
                        "BillWatch",

                    user =
                        new
                        {
                            client_user_id =
                                userId.ToString(
                                    "D",
                                    CultureInfo.InvariantCulture)
                        },

                    products =
                        new[]
                        {
                            "transactions"
                        },

                    country_codes =
                        new[]
                        {
                            "US"
                        },

                    language =
                        "en",

                    hosted_link =
                        new
                        {
                        }
                },
                cancellationToken);

        var root =
            response.RootElement;

        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Link token response.");
        }

        var linkToken =
            GetRequiredString(
                root,
                "link_token",
                MaxLinkTokenLength,
                "Plaid returned an invalid Link token response.");

        var hostedLinkUrlText =
            GetRequiredString(
                root,
                "hosted_link_url",
                MaxHostedLinkUrlLength,
                "Plaid returned an invalid Hosted Link response.");

        var hostedLinkUrl =
            ValidateHostedLinkUrl(
                hostedLinkUrlText);

        var now =
            DateTimeOffset.UtcNow;

        var expiresAtUtc =
            ReadExpiration(
                root,
                now);

        /*
         * Protect the Link token before any database state is created.
         *
         * Only protected ciphertext is persisted. The plaintext token
         * remains local to this request and is never returned to the MAUI
         * client by this service.
         */
        var protectedLinkToken =
            _tokenProtector.ProtectLinkToken(
                linkToken);

        cancellationToken.ThrowIfCancellationRequested();

        var linkSession =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                ProtectedLinkToken =
                    protectedLinkToken,

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

        /*
         * Deliberately do not return the plaintext Link token.
         *
         * BillWatch uses Plaid Hosted Link, so the client only needs the
         * hosted URL and BillWatch-owned session identifier.
         */
        return new PlaidHostedLinkSession(
            linkSession.Id,
            hostedLinkUrl.AbsoluteUri);
    }

    private static string GetRequiredString(
        JsonElement parent,
        string propertyName,
        int maxLength,
        string safeFailureMessage)
    {
        if (parent.ValueKind !=
                JsonValueKind.Object ||
            !parent.TryGetProperty(
                propertyName,
                out var element) ||
            element.ValueKind !=
                JsonValueKind.String)
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        var value =
            element.GetString();

        if (string.IsNullOrWhiteSpace(
                value) ||
            value.Length >
                maxLength ||
            value.Any(
                char.IsControl))
        {
            throw new InvalidOperationException(
                safeFailureMessage);
        }

        return value;
    }

    private static Uri ValidateHostedLinkUrl(
        string hostedLinkUrl)
    {
        if (!Uri.TryCreate(
                hostedLinkUrl,
                UriKind.Absolute,
                out var uri))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link URL.");
        }

        /*
         * Hosted Link is an externally navigated financial-authentication
         * URL. Fail closed unless it is HTTPS and belongs to Plaid.
         *
         * This prevents an unexpected upstream value from turning
         * BillWatch into a phishing redirect.
         */
        if (!string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                uri.Host) ||
            !string.IsNullOrEmpty(
                uri.UserInfo) ||
            !IsPlaidHost(
                uri.Host))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Hosted Link URL.");
        }

        return uri;
    }

    private static bool IsPlaidHost(
        string host)
    {
        if (string.Equals(
                host,
                "plaid.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host.EndsWith(
            ".plaid.com",
            StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset ReadExpiration(
        JsonElement root,
        DateTimeOffset now)
    {
        if (!root.TryGetProperty(
                "expiration",
                out var expirationElement) ||
            expirationElement.ValueKind ==
                JsonValueKind.Null)
        {
            return now.Add(
                DefaultLinkSessionLifetime);
        }

        if (expirationElement.ValueKind !=
            JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Link token expiration.");
        }

        var expirationText =
            expirationElement.GetString();

        if (string.IsNullOrWhiteSpace(
                expirationText) ||
            !DateTimeOffset.TryParse(
                expirationText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces |
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var expiration))
        {
            throw new InvalidOperationException(
                "Plaid returned an invalid Link token expiration.");
        }

        expiration =
            expiration.ToUniversalTime();

        /*
         * Do not persist a session that is already unusable.
         */
        if (expiration <=
            now)
        {
            throw new InvalidOperationException(
                "Plaid returned an expired Link token.");
        }

        return expiration;
    }
}

public sealed record PlaidHostedLinkSession(
    Guid SessionId,
    string HostedLinkUrl);