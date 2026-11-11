using OpenTelemetry;
using System.Net;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

// Answers each request with the next queued status (200 once the queue is empty) and records what was sent.
internal sealed class RecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler {
    private readonly Queue<HttpStatusCode> _statuses = new(statuses);

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (Requests) {
            Requests.Add(new RecordedRequest(request.RequestUri!, request.Content.Headers.ContentType?.MediaType, request.Headers.Authorization?.ToString(), body, Sdk.SuppressInstrumentation));
        }

        var status = _statuses.TryDequeue(out var next) ? next : HttpStatusCode.OK;
        return new HttpResponseMessage(status) { Content = new StringContent("{\"itemsReceived\":1,\"itemsAccepted\":0,\"errors\":[]}") };
    }
}
