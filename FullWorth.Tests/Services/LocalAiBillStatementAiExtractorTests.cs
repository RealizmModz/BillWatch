using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FullWorth.API.Services.Statements;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class LocalAiBillStatementAiExtractorTests
{
    [Fact]
    public async Task DisabledLocalAi_FailsWithoutSendingRequest()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    throw new InvalidOperationException(
                        "The local model must not be called."));

        var extractor =
            CreateExtractor(
                handler,
                new LocalAiBillStatementOptions
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

        Assert.Null(handler.RequestBody);
    }

    [Fact]
    public async Task EnabledLocalAi_UsesStrictSchemaAndBoundedDocument()
    {
        BillStatementAiCandidate responseCandidate =
            CreateCandidate();

        var handler =
            new RecordingHandler(
                _ =>
                    CreateModelResponse(
                        responseCandidate));

        var options =
            CreateEnabledOptions();

        options.ApiKey =
            "local-test-key-not-a-secret";

        options.MaxDocumentCharacters =
            1_000;

        var extractor =
            CreateExtractor(
                handler,
                options);

        const string includedText =
            "ACME Total due $10.00 USD ";

        const string excludedMarker =
            "MUST_NOT_BE_SENT";

        string documentText =
            includedText.PadRight(
                1_000,
                'x') +
            excludedMarker;

        BillStatementAiCandidate candidate =
            await extractor.ExtractAsync(
                CreateRequest(
                    documentText));

        Assert.Equal(
            "ACME",
            candidate.ProviderName);

        Assert.NotNull(handler.RequestBody);

        using JsonDocument requestJson =
            JsonDocument.Parse(
                handler.RequestBody);

        JsonElement root =
            requestJson.RootElement;

        Assert.False(
            root.GetProperty("stream")
                .GetBoolean());

        Assert.Equal(
            0,
            root.GetProperty("temperature")
                .GetInt32());

        JsonElement responseFormat =
            root.GetProperty(
                "response_format");

        Assert.Equal(
            "json_schema",
            responseFormat
                .GetProperty("type")
                .GetString());

        Assert.True(
            responseFormat
                .GetProperty("json_schema")
                .GetProperty("strict")
                .GetBoolean());

        Assert.Equal(
            "object",
            responseFormat
                .GetProperty("json_schema")
                .GetProperty("schema")
                .GetProperty("type")
                .GetString());

        Assert.DoesNotContain(
            excludedMarker,
            handler.RequestBody,
            StringComparison.Ordinal);

        Assert.Contains(
            options.PromptVersion,
            handler.RequestBody,
            StringComparison.Ordinal);

        Assert.Equal(
            new Uri(options.Endpoint),
            handler.RequestUri);

        Assert.Equal(
            $"Bearer {options.ApiKey}",
            handler.Authorization);

        Assert.DoesNotContain(
            options.ApiKey!,
            handler.RequestBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpFailure_DoesNotExposeModelResponseBody()
    {
        const string sensitiveModelBody =
            "local-model-internal-detail";

        var handler =
            new RecordingHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.BadRequest)
                    {
                        Content =
                            new StringContent(
                                sensitiveModelBody)
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
            sensitiveModelBody,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncompleteGeneration_IsRejected()
    {
        var response =
            new JsonObject
            {
                ["choices"] =
                    new JsonArray
                    {
                        new JsonObject
                        {
                            ["finish_reason"] =
                                "length",

                            ["message"] =
                                new JsonObject
                                {
                                    ["role"] =
                                        "assistant",

                                    ["content"] =
                                        "{}"
                                }
                        }
                    }
            };

        var handler =
            new RecordingHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content =
                            new StringContent(
                                response.ToJsonString(),
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
            "length",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedResponse_IsRejectedBeforeParsing()
    {
        var content =
            new ByteArrayContent(
                [123, 125]);

        content.Headers.ContentLength =
            1_048_577;

        var handler =
            new RecordingHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content =
                            content
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
            "oversized",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "https://example.com/v1/chat/completions")]
    [InlineData(
        "http://192.168.1.50:8080/v1/chat/completions")]
    [InlineData(
        "http://127.0.0.1:8080/completion")]
    public void Options_RejectNonLoopbackOrWrongPath(
        string endpoint)
    {
        var validator =
            new LocalAiBillStatementOptionsValidator();

        var result =
            validator.Validate(
                null,
                new LocalAiBillStatementOptions
                {
                    Enabled =
                        true,

                    Endpoint =
                        endpoint
                });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(
        "http://127.0.0.1:8080/v1/chat/completions")]
    [InlineData(
        "http://localhost:8080/v1/chat/completions")]
    [InlineData(
        "https://localhost:8443/v1/chat/completions")]
    public void Options_AcceptLoopbackChatCompletionsEndpoint(
        string endpoint)
    {
        var validator =
            new LocalAiBillStatementOptionsValidator();

        var result =
            validator.Validate(
                null,
                new LocalAiBillStatementOptions
                {
                    Enabled =
                        true,

                    Endpoint =
                        endpoint
                });

        Assert.False(result.Failed);
    }

    private static LocalAiBillStatementAiExtractor CreateExtractor(
        HttpMessageHandler handler,
        LocalAiBillStatementOptions options)
    {
        return new LocalAiBillStatementAiExtractor(
            new HttpClient(handler),
            Options.Create(options));
    }

    private static LocalAiBillStatementOptions CreateEnabledOptions()
    {
        return new LocalAiBillStatementOptions
        {
            Enabled =
                true
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

    private static BillStatementAiCandidate CreateCandidate()
    {
        return new BillStatementAiCandidate(
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
    }

    private static HttpResponseMessage CreateModelResponse(
        BillStatementAiCandidate candidate)
    {
        var serializerOptions =
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

        serializerOptions.Converters.Add(
            new JsonStringEnumConverter());

        string candidateJson =
            JsonSerializer.Serialize(
                candidate,
                serializerOptions);

        var response =
            new JsonObject
            {
                ["choices"] =
                    new JsonArray
                    {
                        new JsonObject
                        {
                            ["finish_reason"] =
                                "stop",

                            ["message"] =
                                new JsonObject
                                {
                                    ["role"] =
                                        "assistant",

                                    ["content"] =
                                        candidateJson
                                }
                        }
                    }
            };

        return new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content =
                new StringContent(
                    response.ToJsonString(),
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri =
                request.RequestUri;

            RequestBody =
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(
                        cancellationToken);

            Authorization =
                request.Headers.Authorization?.ToString();

            return responseFactory(request);
        }
    }
}
