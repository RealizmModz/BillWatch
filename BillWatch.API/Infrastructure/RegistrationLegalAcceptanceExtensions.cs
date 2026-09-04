using System.Text.Json;
using BillWatch.Core.Legal;

namespace BillWatch.API.Infrastructure;

public static class RegistrationLegalAcceptanceExtensions
{
    private const long MaximumRegistrationBodyBytes =
        16 * 1024;

    public static IApplicationBuilder
        UseBillWatchRegistrationLegalAcceptance(
            this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(
            async (context, next) =>
            {
                if (!IsRegistrationRequest(context))
                {
                    await next();
                    return;
                }

                if (context.Request.ContentLength is > MaximumRegistrationBodyBytes)
                {
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status413PayloadTooLarge,
                        "The account-registration request is too large.");
                    return;
                }

                context.Request.EnableBuffering(
                    bufferThreshold: 8 * 1024,
                    bufferLimit: MaximumRegistrationBodyBytes);

                RegistrationAcceptanceResult acceptance;

                try
                {
                    using var document =
                        await JsonDocument.ParseAsync(
                            context.Request.Body,
                            new JsonDocumentOptions
                            {
                                AllowTrailingCommas = false,
                                CommentHandling = JsonCommentHandling.Disallow,
                                MaxDepth = 16
                            },
                            context.RequestAborted);

                    acceptance =
                        ReadAcceptance(document.RootElement);
                }
                catch (JsonException)
                {
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "The account-registration request is invalid.");
                    return;
                }
                catch (IOException)
                {
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status413PayloadTooLarge,
                        "The account-registration request is too large.");
                    return;
                }
                finally
                {
                    if (context.Request.Body.CanSeek)
                    {
                        context.Request.Body.Position = 0;
                    }
                }

                if (!acceptance.Accepted ||
                    !string.Equals(
                        acceptance.Version,
                        BillWatchLegalDocuments.CurrentVersion,
                        StringComparison.Ordinal))
                {
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Accept the current BillWatch Terms and Privacy Notice to create an account.");
                    return;
                }

                await next();
            });
    }

    private static bool IsRegistrationRequest(
        HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method) &&
               context.Request.Path.Equals(
                   "/api/auth/register",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static RegistrationAcceptanceResult ReadAcceptance(
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return RegistrationAcceptanceResult.Rejected;
        }

        var accepted =
            root.TryGetProperty(
                "acceptedTermsAndPrivacy",
                out var acceptedElement) &&
            acceptedElement.ValueKind == JsonValueKind.True;

        var version =
            root.TryGetProperty(
                "legalTermsVersion",
                out var versionElement) &&
            versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()
                : null;

        return new RegistrationAcceptanceResult(
            accepted,
            version);
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title)
    {
        return Results.Problem(
                statusCode: statusCode,
                title: title)
            .ExecuteAsync(context);
    }

    private sealed record RegistrationAcceptanceResult(
        bool Accepted,
        string? Version)
    {
        public static RegistrationAcceptanceResult Rejected { get; } =
            new(
                false,
                null);
    }
}
