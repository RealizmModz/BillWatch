namespace BillWatch.Tests.Infrastructure;

internal sealed class ScriptedHttpMessageHandler(
    params HttpResponseMessage[] responses)
    : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses =
        new(
            responses);

    public List<CapturedHttpRequest> Requests { get; } =
        [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_responses.Count ==
            0)
        {
            throw new InvalidOperationException(
                "No scripted HTTP response remains.");
        }

        var body =
            request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);

        Requests.Add(
            new CapturedHttpRequest(
                request.Method,
                request.RequestUri
                ?? throw new InvalidOperationException(
                    "The scripted request did not have a URI."),
                body));

        return _responses.Dequeue();
    }
}

internal sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri Uri,
    string Body);
