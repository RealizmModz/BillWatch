using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace BillWatch.Web.Infrastructure;

public static class WebHostingExtensions
{
    public static BillWatchWebHostingConfiguration
        ConfigureBillWatchWebHosting(
            this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(
            builder);

        var apiBaseUrl =
            builder.Configuration[
                "BillWatchApi:BaseUrl"];

        if (string.IsNullOrWhiteSpace(
                apiBaseUrl))
        {
            if (!builder.Environment
                .IsDevelopment())
            {
                throw new InvalidOperationException(
                    "BillWatchApi:BaseUrl must be configured outside development.");
            }

            apiBaseUrl =
                "https://localhost:7243";
        }

        if (!Uri.TryCreate(
                apiBaseUrl,
                UriKind.Absolute,
                out var apiBaseUri) ||
            (apiBaseUri.Scheme !=
                Uri.UriSchemeHttps &&
             apiBaseUri.Scheme !=
                Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "BillWatchApi:BaseUrl must be an absolute HTTP or HTTPS URI.");
        }

        if (!builder.Environment
                .IsDevelopment() &&
            apiBaseUri.Scheme ==
                Uri.UriSchemeHttp &&
            !IsPrivateContainerHost(
                apiBaseUri.Host))
        {
            throw new InvalidOperationException(
                "Production HTTP API communication is allowed only for a private container hostname.");
        }

        var apiHostHeader =
            builder.Configuration[
                "BillWatchApi:HostHeader"]?
                .Trim();

        if (!builder.Environment
                .IsDevelopment() &&
            apiBaseUri.Scheme ==
                Uri.UriSchemeHttp)
        {
            if (string.IsNullOrWhiteSpace(
                    apiHostHeader) ||
                apiHostHeader.Contains(
                    "://",
                    StringComparison.Ordinal) ||
                apiHostHeader.Contains(
                    "/",
                    StringComparison.Ordinal) ||
                apiHostHeader.Contains(
                    ":",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "BillWatchApi:HostHeader must be an explicit hostname when production uses an internal HTTP API address.");
            }
        }

        var allowedHosts =
            builder.Configuration[
                "AllowedHosts"];

        if (!builder.Environment
            .IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(
                    allowedHosts) ||
                string.Equals(
                    allowedHosts.Trim(),
                    "*",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "AllowedHosts must be explicitly configured outside development.");
            }
        }

        var configuredDataProtectionPath =
            builder.Configuration[
                "DataProtection:KeysPath"];

        var dataProtectionBuilder =
            builder.Services
                .AddDataProtection()
                .SetApplicationName(
                    "BillWatch.Web");

        if (!string.IsNullOrWhiteSpace(
                configuredDataProtectionPath))
        {
            if (!builder.Environment
                    .IsDevelopment() &&
                !Path.IsPathFullyQualified(
                    configuredDataProtectionPath))
            {
                throw new InvalidOperationException(
                    "DataProtection:KeysPath must be an absolute path outside development.");
            }

            var dataProtectionKeysPath =
                Path.GetFullPath(
                    configuredDataProtectionPath);

            Directory.CreateDirectory(
                dataProtectionKeysPath);

            dataProtectionBuilder
                .PersistKeysToFileSystem(
                    new DirectoryInfo(
                        dataProtectionKeysPath));
        }
        else if (!builder.Environment
            .IsDevelopment())
        {
            throw new InvalidOperationException(
                "DataProtection:KeysPath must be configured outside development.");
        }

        var configuredKnownProxies =
            builder.Configuration
                .GetSection(
                    "ReverseProxy:KnownProxies")
                .Get<string[]>()
            ?? [];

        var useForwardedHeaders =
            configuredKnownProxies.Length >
                0;

        if (useForwardedHeaders)
        {
            builder.Services.Configure<
                ForwardedHeadersOptions>(
                options =>
                {
                    options.ForwardedHeaders =
                        ForwardedHeaders.XForwardedFor |
                        ForwardedHeaders.XForwardedProto;

                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();

                    foreach (var configuredProxy in
                             configuredKnownProxies)
                    {
                        if (!IPAddress.TryParse(
                                configuredProxy,
                                out var proxyAddress))
                        {
                            throw new InvalidOperationException(
                                "ReverseProxy:KnownProxies contains an invalid IP address.");
                        }

                        options.KnownProxies.Add(
                            proxyAddress);
                    }
                });
        }
        else if (!builder.Environment
            .IsDevelopment())
        {
            throw new InvalidOperationException(
                "At least one trusted reverse proxy must be configured outside development.");
        }

        return new BillWatchWebHostingConfiguration(
            apiBaseUri,
            apiHostHeader,
            useForwardedHeaders);
    }

    public static IApplicationBuilder
        UseBillWatchWebSecurityHeaders(
            this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(
            app);

        return app.Use(
            async (
                context,
                next) =>
            {
                context.Response.OnStarting(
                    () =>
                    {
                        context.Response.Headers[
                            "X-Content-Type-Options"] =
                            "nosniff";

                        context.Response.Headers[
                            "X-Frame-Options"] =
                            "DENY";

                        context.Response.Headers[
                            "Referrer-Policy"] =
                            "no-referrer";

                        context.Response.Headers[
                            "Permissions-Policy"] =
                            "camera=(), microphone=(), geolocation=()";

                        context.Response.Headers[
                            "Content-Security-Policy"] =
                            "frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";

                        if (context.Request.Path
                                .StartsWithSegments(
                                    "/auth") ||
                            context.Request.Path
                                .StartsWithSegments(
                                    "/bff") ||
                            context.Request.Path
                                .StartsWithSegments(
                                    "/health"))
                        {
                            context.Response.Headers[
                                "Cache-Control"] =
                                "no-store, no-cache, max-age=0, must-revalidate";

                            context.Response.Headers[
                                "Pragma"] =
                                "no-cache";

                            context.Response.Headers[
                                "Expires"] =
                                "0";
                        }

                        return Task.CompletedTask;
                    });

                await next();
            });
    }

    private static bool IsPrivateContainerHost(
        string host)
    {
        if (string.IsNullOrWhiteSpace(
                host))
        {
            return false;
        }

        if (string.Equals(
                host,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host.IndexOf(
                   '.') <
               0;
    }
}

public sealed record BillWatchWebHostingConfiguration(
    Uri ApiBaseUri,
    string? ApiHostHeader,
    bool UseForwardedHeaders);