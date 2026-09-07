using System.Net;
using System.Net.Http.Json;
using System.Text;
using BillWatch.Core.Legal;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class RegistrationLegalAcceptanceTests
{
    private const string Password =
        "BillWatch!LegalTests123";

    [Fact]
    public async Task RegistrationWithoutAcceptance_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email =
                        $"legal-missing-{Guid.NewGuid():N}@billwatch.local",
                    password = Password
                });

        await AssertLegalRejectionAsync(response);
    }

    [Fact]
    public async Task RegistrationWithFalseAcceptance_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email =
                        $"legal-false-{Guid.NewGuid():N}@billwatch.local",
                    password = Password,
                    acceptedTermsAndPrivacy = false,
                    legalTermsVersion =
                        BillWatchLegalDocuments.CurrentVersion
                });

        await AssertLegalRejectionAsync(response);
    }

    [Fact]
    public async Task RegistrationWithStaleVersion_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email =
                        $"legal-stale-{Guid.NewGuid():N}@billwatch.local",
                    password = Password,
                    acceptedTermsAndPrivacy = true,
                    legalTermsVersion =
                        "2026-01-01-obsolete"
                });

        await AssertLegalRejectionAsync(response);
    }

    [Fact]
    public async Task RegistrationWithCurrentAcceptance_SucceedsAndCanLogin()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"legal-current-{Guid.NewGuid():N}@billwatch.local";

        using var registerResponse =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = Password,
                    acceptedTermsAndPrivacy = true,
                    legalTermsVersion =
                        BillWatchLegalDocuments.CurrentVersion
                });

        Assert.Equal(
            HttpStatusCode.OK,
            registerResponse.StatusCode);

        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password = Password
                });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);
    }

    [Fact]
    public async Task OversizedRegistrationBody_IsRejectedBeforeIdentityProcessing()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var oversizedJson =
            "{\"padding\":\"" +
            new string('x', 17 * 1024) +
            "\"}";

        using var response =
            await client.PostAsync(
                "/api/auth/register",
                new StringContent(
                    oversizedJson,
                    Encoding.UTF8,
                    "application/json"));

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);

        AssertNoStore(response);
    }

    private static async Task AssertLegalRejectionAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "Terms and Privacy Notice",
            body,
            StringComparison.Ordinal);

        AssertNoStore(response);
    }

    private static void AssertNoStore(
        HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(
                "Cache-Control",
                out var cacheControlValues));

        Assert.Contains(
            cacheControlValues,
            value =>
                value.Contains(
                    "no-store",
                    StringComparison.OrdinalIgnoreCase));
    }
}
