using Azure.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;

// Posts a batch of envelopes to the ingestion endpoint, retrying what Application Insights says is worth retrying.
internal sealed class NTBlazorAzureMonitorSender(IHttpClientFactory _httpClientFactory, TokenCredential? _credential, Uri _trackUri, TimeProvider _timeProvider, ILogger _logger) {
    internal const string HttpClientName = "NT.Blazor.ErrorBoundary.AzureMonitor";
    internal const int MaxAttempts = 3;
    internal const int MaxLoggedResponseChars = 1024;
    // An honored Retry-After never stalls the single send loop longer than this.
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly string[] Scopes = ["https://monitor.azure.com//.default"];

    internal async Task SendAsync(List<string> batch, CancellationToken cancellationToken) {
        for (var attempt = 1; ; attempt++) {
            var (status, body, retryAfter) = await TryPostAsync(batch, cancellationToken);
            if (status == HttpStatusCode.OK) {
                return;
            }

            // A 206 lists the rejected items; only those rejected with a retryable status are sent again, since the rest would fail the same way.
            var retry = status == HttpStatusCode.PartialContent ? RetryableItems(batch, body) : IsRetryable(status) ? batch : [];
            if (retry.Count == 0 || attempt == MaxAttempts) {
                _logger.LogWarning("Application Insights did not accept all of {Count} Blazor errors or events. Status: {StatusCode}. Response: {Response}", batch.Count, status is null ? "no response" : (int)status, body?.Length > MaxLoggedResponseChars ? body[..MaxLoggedResponseChars] : body);
                return;
            }

            batch = retry;
            // Application Insights sends Retry-After with 429 and 503; without it, back off 2 then 4 seconds.
            var delay = retryAfter ?? TimeSpan.FromSeconds(attempt * 2);
            await Task.Delay(delay > MaxRetryDelay ? MaxRetryDelay : delay, _timeProvider, cancellationToken);
        }
    }

    private static bool IsRetryable(HttpStatusCode? status) => status is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;

    // Items whose error carries a numeric retryable status; anything else in the body, including an unexpected shape, makes the item final.
    private static List<string> RetryableItems(List<string> batch, string? body) {
        try {
            using var document = JsonDocument.Parse(body ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array) {
                return [];
            }

            return [.. errors.EnumerateArray()
                .Where(error => error.ValueKind == JsonValueKind.Object && Number(error, "statusCode") is { } statusCode && IsRetryable((HttpStatusCode)statusCode))
                .Select(error => Number(error, "index"))
                .OfType<int>()
                .Where(index => index >= 0 && index < batch.Count)
                .Distinct()
                .Select(index => batch[index])];
        }
        catch (JsonException) {
            return [];
        }
    }

    private static int? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private async Task<(HttpStatusCode? Status, string? Body, TimeSpan? RetryAfter)> TryPostAsync(List<string> batch, CancellationToken cancellationToken) {
        // Without this, HTTP instrumentation records every upload as a dependency call in the same Application Insights resource.
        using var suppress = SuppressInstrumentationScope.Begin();
        HttpResponseMessage response;
        try {
            using var request = new HttpRequestMessage(HttpMethod.Post, _trackUri) {
                Content = new StringContent(string.Join('\n', batch), Encoding.UTF8, "application/x-json-stream")
            };
            if (_credential is not null) {
                var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
                request.Headers.Authorization = new("Bearer", token.Token);
            }

            response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        // Network, timeout, and credential failures (whose types live in Azure.Identity) are all treated as retryable; shutdown cancellation is not.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested) {
            return (null, exception.Message, null);
        }

        using (response) {
            if (response.StatusCode == HttpStatusCode.OK) {
                return (response.StatusCode, null, null);
            }

            var retryAfter = response.Headers.RetryAfter is { } header ? header.Delta ?? header.Date - _timeProvider.GetUtcNow() : null;
            var wait = retryAfter > TimeSpan.Zero ? retryAfter : null;
            // An unreadable body must not turn a final status into a retryable "no response".
            try {
                return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), wait);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) {
                return (response.StatusCode, null, wait);
            }
        }
    }
}
