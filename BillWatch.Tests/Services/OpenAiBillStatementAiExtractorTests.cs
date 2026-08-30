using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BillWatch.API.Services.Statements;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Services;

public sealed class OpenAiBillStatementAiExtractorTests
{
    [Fact]
    public async Task DisabledProvider_FailsWithoutSendingRequest()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    throw new InvalidOperationException(
                        "The provider must not be called."));

        var extractor =
            CreateExtractor(
                handler,
                new OpenAiBillStatementOptions
                {
                    Enabled =
                        false
                });

        var exception =
            await Assert.ThrowsAsync<
                BillStatementAiExtractionException>(
                () =>
                    extractor.ExtractAsync(
                        CreateRequest(
                            "Total due $10.00")));

        Assert.Contains(
            "disabled",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Null(
            handler.RequestBody);
    }

    [Fact]
    public async Task EnabledProvider_UsesStrictSchemaAndBoundedDocument()
    {
        var responseCandidate =
            new BillStatementAiCandidate(
                ProviderName:
                    "ACME",

                AccountIdentifierSuffix:
                    null,

                BillingPeriodStart:
                    null,

                BillingPeriodEnd:
                    null,

                StatementDate:
                    null,

                DueDate:
                    null,

                PreviousBalance:
                    null,

                Payments:
                    null,

                CurrentCharges:
                    null,

                TotalDue:
                    10m,

                CurrencyCode:
                    "USD",

                PlanOrService:
                    null,

                UsageSummary:
                    null,

                LineItems:
                    [],

                Evidence:
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.ProviderName,
                            "ACME"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $10.00"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "USD")
                    ],

                ModelConfidence:
                    BillStatementAiModelConfidence.High);

        var handler =
            new RecordingHandler(
                _ =>
                    CreateProviderResponse(
                        responseCandidate));

        var options =
            CreateEnabledOptions();

        options.MaxDocumentCharacters =
            1_000;

        var extractor =
            CreateExtractor(
                handler,
                options);

        var includedText =
            "ACME Total due $10.00 USD ";

        var excludedMarker =
            "MUST_NOT_BE_SENT";

        var documentText =
            includedText.PadRight(
                1_000,
                'x') +
            excludedMarker;

        var candidate =
            await extractor.ExtractAsync(
                CreateRequest(
                    documentText));

        Assert.Equal(
            "ACME",
            candidate.ProviderName);

        Assert.NotNull(
            handler.RequestBody);

        using var requestJson =
            JsonDocument.Parse(
                handler.RequestBody);

        var root =
            requestJson.RootElement;

        Assert.True(
            root.GetProperty("text")
                .GetProperty("format")
                .GetProperty("strict")
                .GetBoolean());

        Assert.Equal(
            "json_schema",
            root.GetProperty("text")
                .GetProperty("format")
                .GetProperty("type")
                .GetString());

        Assert.False(
            root.GetProperty("store")
                .GetBoolean());

        Assert.Contains(
            options.PromptVersion,
            root.GetProperty("instructions")
                .GetString());

        Assert.DoesNotContain(
            excludedMarker,
            handler.RequestBody,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            options.ApiKey!,
            handler.RequestBody,
            StringComparison.Ordinal);

        Assert.Equal(
            $"Bearer {options.ApiKey}",
            handler.Authorization);
    }

    [Fact]
    public async Task ProviderHttpFailure_ReturnsSanitizedException()
    {
        const string sensitiveProviderBody =
            "provider-internal-detail";

        var handler =
            new RecordingHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.BadRequest)
                    {
                        Content =
                            new StringContent(
                                sensitiveProviderBody)
                    });

        var extractor =
            CreateExtractor(
                handler,
                CreateEnabledOptions());

        var exception =
            await Assert.ThrowsAsync<
                BillStatementAiExtractionException>(
                () =>
                    extractor.ExtractAsync(
                        CreateRequest(
                            "ACME Total due $10.00 USD")));

        Assert.Contains(
            "HTTP 400",
            exception.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            sensitiveProviderBody,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncompleteProviderResponse_IsRejected()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content =
                            new StringContent(
                                """
                                {
                                  "status": "incomplete",
                                  "incomplete_details": {
                                    "reason": "max_output_tokens"
                                  },
                                  "output": []
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                    });

        var extractor =
            CreateExtractor(
                handler,
                CreateEnabledOptions());

        var exception =
            await Assert.ThrowsAsync<
                BillStatementAiExtractionException>(
                () =>
                    extractor.ExtractAsync(
                        CreateRequest(
                            "ACME Total due $10.00 USD")));

        Assert.Contains(
            "did not complete",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "max_output_tokens",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledOptions_RequireApiKey()
    {
        var validator =
            new OpenAiBillStatementOptionsValidator();

        var result =
            validator.Validate(
                null,
                new OpenAiBillStatementOptions
                {
                    Enabled =
                        true,

                    ApiKey =
                        null
                });

        Assert.True(
            result.Failed);

        Assert.Contains(
            result.Failures,
            failure =>
                failure.Contains(
                    "ApiKey",
                    StringComparison.Ordinal));
    }

    private static OpenAiBillStatementAiExtractor CreateExtractor(
        HttpMessageHandler handler,
        OpenAiBillStatementOptions options)
    {
        return new OpenAiBillStatementAiExtractor(
            new HttpClient(
                handler),
            Options.Create(
                options));
    }

    private static OpenAiBillStatementOptions CreateEnabledOptions()
    {
        return new OpenAiBillStatementOptions
        {
            Enabled =
                true,

            ApiKey =
                "test-key-not-a-secret"
        };
    }

    private static BillStatementAiExtractionRequest CreateRequest(
        string documentText)
    {
        return new BillStatementAiExtractionRequest(
            DocumentText:
                documentText,

            Hints:
                new BillStatementExtractionHints(
                    ExpectedProviderName:
                        "ACME",

                    ExpectedCategory:
                        "Internet"),

            PromptVersion:
                "bill-statement-extraction-v1");
    }

    private static HttpResponseMessage CreateProviderResponse(
        BillStatementAiCandidate candidate)
    {
        var serializerOptions =
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

        serializerOptions.Converters.Add(
            new JsonStringEnumConverter());

        var candidateJson =
            JsonSerializer.Serialize(
                candidate,
                serializerOptions);

        var responseJson =
            new JsonObject
            {
                ["status"] =
                    "completed",

                ["output"] =
                    new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] =
                                "message",

                            ["content"] =
                                new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["type"] =
                                            "output_text",

                                        ["text"] =
                                            candidateJson
                                    }
                                }
                        }
                    }
            };

        return new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content =
                new StringContent(
                    responseJson.ToJsonString(),
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody =
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(
                        cancellationToken);

            Authorization =
                request.Headers.Authorization?.ToString();

            return responseFactory(
                request);
        }
    }
}
