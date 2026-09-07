using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BillWatch.API.Services.Identity;

public interface IExternalIdentityTokenValidator
{
    bool IsProviderConfigured(
        string provider);

    Task<ExternalIdentity?> ValidateAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalIdentityTokenValidator :
    IExternalIdentityTokenValidator
{
    private const int MaxIdentityTokenLength =
        32 * 1024;

    private static readonly TimeSpan ClockSkew =
        TimeSpan.FromMinutes(2);

    private readonly ExternalIdentityOptions
        _options;

    private readonly IHttpClientFactory
        _httpClientFactory;

    private readonly ConcurrentDictionary<
        string,
        ConfigurationManager<OpenIdConnectConfiguration>>
        _configurationManagers =
            new(StringComparer.OrdinalIgnoreCase);

    public ExternalIdentityTokenValidator(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
        : this(
            Options.Create(
                BindOptions(
                    configuration)),
            httpClientFactory)
    {
    }

    public ExternalIdentityTokenValidator(
        IOptions<ExternalIdentityOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _options =
            options.Value;

        _httpClientFactory =
            httpClientFactory;
    }

    public bool IsProviderConfigured(
        string provider)
    {
        return _options.GetProvider(provider)?
            .IsConfigured ==
            true;
    }

    public async Task<ExternalIdentity?> ValidateAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default)
    {
        var providerOptions =
            _options.GetProvider(provider);

        if (providerOptions is null ||
            !providerOptions.IsConfigured ||
            string.IsNullOrWhiteSpace(idToken) ||
            idToken.Length > MaxIdentityTokenLength)
        {
            return null;
        }

        var metadataAddress =
            ValidateMetadataAddress(
                providerOptions);

        var configurationManager =
            _configurationManagers.GetOrAdd(
                providerOptions.Provider,
                _ =>
                    CreateConfigurationManager(
                        metadataAddress));

        OpenIdConnectConfiguration configuration;

        try
        {
            configuration =
                await configurationManager
                    .GetConfigurationAsync(
                        cancellationToken);
        }
        catch (
            Exception exception)
            when (exception is HttpRequestException or
                  IOException or
                  TaskCanceledException or
                  InvalidOperationException)
        {
            throw new ExternalIdentityProviderUnavailableException(
                exception);
        }

        var validIssuers =
            new HashSet<string>(
                StringComparer.Ordinal)
            {
                configuration.Issuer
            };

        foreach (var issuer in
                 providerOptions.AdditionalValidIssuers)
        {
            if (!string.IsNullOrWhiteSpace(issuer))
            {
                validIssuers.Add(
                    issuer.Trim());
            }
        }

        var validationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey =
                    true,

                IssuerSigningKeys =
                    configuration.SigningKeys,

                ValidateIssuer =
                    true,

                ValidIssuers =
                    validIssuers,

                ValidateAudience =
                    true,

                ValidAudience =
                    providerOptions.Audience!.Trim(),

                RequireSignedTokens =
                    true,

                RequireExpirationTime =
                    true,

                ValidateLifetime =
                    true,

                ClockSkew =
                    ClockSkew
            };

        var handler =
            new JsonWebTokenHandler
            {
                MaximumTokenSizeInBytes =
                    MaxIdentityTokenLength
            };

        TokenValidationResult validationResult;

        try
        {
            validationResult =
                await handler.ValidateTokenAsync(
                    idToken,
                    validationParameters);
        }
        catch (SecurityTokenException)
        {
            return null;
        }

        if (!validationResult.IsValid ||
            validationResult.ClaimsIdentity is null)
        {
            return null;
        }

        var identity =
            validationResult.ClaimsIdentity;

        var subject =
            identity.FindFirst("sub")?
                .Value?
                .Trim();

        if (string.IsNullOrWhiteSpace(subject) ||
            subject.Length > 512 ||
            subject.Any(char.IsControl))
        {
            return null;
        }

        var email =
            FirstClaimValue(
                identity,
                ClaimTypes.Email,
                "email",
                "preferred_username");

        if (!string.IsNullOrWhiteSpace(email))
        {
            email =
                email.Trim();

            if (email.Length > 320 ||
                email.Any(char.IsControl))
            {
                email =
                    null;
            }
        }

        var emailVerified =
            bool.TryParse(
                FirstClaimValue(
                    identity,
                    "email_verified"),
                out var parsedEmailVerified) &&
            parsedEmailVerified;

        return new ExternalIdentity(
            providerOptions.Provider,
            subject,
            email,
            emailVerified);
    }

    private ConfigurationManager<OpenIdConnectConfiguration>
        CreateConfigurationManager(
            Uri metadataAddress)
    {
        var httpClient =
            _httpClientFactory.CreateClient(
                "ExternalIdentityMetadata");

        var documentRetriever =
            new HttpDocumentRetriever(
                httpClient)
            {
                RequireHttps =
                    true
            };

        return new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress.AbsoluteUri,
            new OpenIdConnectConfigurationRetriever(),
            documentRetriever)
        {
            AutomaticRefreshInterval =
                TimeSpan.FromHours(12),

            RefreshInterval =
                TimeSpan.FromMinutes(5)
        };
    }

    private static ExternalIdentityOptions BindOptions(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(
            configuration);

        var options =
            new ExternalIdentityOptions();

        configuration
            .GetSection(
                ExternalIdentityOptions.SectionName)
            .Bind(
                options);

        return options;
    }

    private static Uri ValidateMetadataAddress(
        ExternalIdentityProviderOptions provider)
    {
        if (!Uri.TryCreate(
                provider.MetadataAddress,
                UriKind.Absolute,
                out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(
                uri.Host,
                provider.AllowedMetadataHost,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "External identity provider metadata is not configured safely.");
        }

        return uri;
    }

    private static string? FirstClaimValue(
        ClaimsIdentity identity,
        params string[] claimTypes)
    {
        foreach (var claimType in
                 claimTypes)
        {
            var value =
                identity.FindFirst(claimType)?
                    .Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified);

public sealed class ExternalIdentityProviderUnavailableException :
    Exception
{
    public ExternalIdentityProviderUnavailableException(
        Exception innerException)
        : base(
            "External identity provider metadata is temporarily unavailable.",
            innerException)
    {
    }
}
