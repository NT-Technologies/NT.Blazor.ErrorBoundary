using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using NT.Blazor.ErrorBoundary.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.Services;

internal sealed class NTHttpClientBlazorErrorReporter : INTBlazorErrorReporter {
    // Browsers refuse keepalive requests with larger bodies, so a larger report is sent without it.
    internal const int MaxKeepaliveBytes = 64_000;
    // The fetch options the WebAssembly HttpClient handler passes to the browser; other handlers ignore them.
    internal static readonly HttpRequestOptionsKey<IDictionary<string, object>> FetchOptions = new("WebAssemblyFetchOptions");

    private readonly Func<NTBlazorErrorReport, NTBlazorErrorReport?>? _filterReport;
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<NTHttpClientBlazorErrorReporter> _logger;
    private readonly int _maxReportFieldLength;
    private readonly string _reportUri;

    public NTHttpClientBlazorErrorReporter(HttpClient httpClient, IJSRuntime jsRuntime, IOptions<NTBlazorErrorBoundaryHttpClientOptions> options, ILogger<NTHttpClientBlazorErrorReporter> logger) {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ReportUri);

        _filterReport = options.Value.FilterReport;
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _logger = logger;
        _maxReportFieldLength = options.Value.MaxReportFieldLength;
        _reportUri = options.Value.ReportUri;
    }

    public async Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
        try {
            var report = NTBlazorErrorReport.FromException(exception, context, _maxReportFieldLength);
            report = await EnrichWithBrowserContextAsync(report, cancellationToken);
            if ((_filterReport is null ? report : _filterReport(report)) is { } filtered && !await TryPostAsync(filtered, cancellationToken)) {
                await QueueThroughBrowserAsync(filtered, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception reportException) {
            _logger.LogWarning(reportException, "Failed to submit Blazor error report.");
        }
    }

    // Returns false only when the report should be retried later from the browser queue.
    private async Task<bool> TryPostAsync(NTBlazorErrorReport report, CancellationToken cancellationToken) {
        try {
            var body = JsonSerializer.SerializeToUtf8Bytes(report, JsonSerializerOptions.Web);
            using var request = new HttpRequestMessage(HttpMethod.Post, _reportUri) {
                Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } } }
            };
            // keepalive lets the browser finish the upload when the page navigates away; the report is then neither lost nor queued again.
            if (body.Length <= MaxKeepaliveBytes) {
                request.Options.Set(FetchOptions, new Dictionary<string, object>(StringComparer.Ordinal) { ["keepalive"] = true });
            }

            if (!string.IsNullOrEmpty(report.TraceParent)) {
                request.Headers.TryAddWithoutValidation("traceparent", report.TraceParent);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) {
                return true;
            }

            var status = response.StatusCode;
            var isRetryable = (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
            _logger.LogWarning("Blazor error report was not accepted. Status: {StatusCode}. Queued for retry: {IsRetryable}.", (int)status, isRetryable);
            return !isRetryable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception exception) {
            _logger.LogWarning(exception, "Failed to post Blazor error report; queueing it in the browser.");
            return false;
        }
    }

    private async Task<NTBlazorErrorReport> EnrichWithBrowserContextAsync(NTBlazorErrorReport report, CancellationToken cancellationToken) {
        try {
            var context = await _jsRuntime.InvokeAsync<NTBlazorClientContext?>(
                "NTBlazorErrorBoundary.getContext",
                cancellationToken);
            return context is null ? report : report.WithClientContext(context, _maxReportFieldLength);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or NotSupportedException) {
            _logger.LogDebug(exception, "Browser telemetry context was unavailable.");
            return report;
        }
    }

    private async Task QueueThroughBrowserAsync(NTBlazorErrorReport report, CancellationToken cancellationToken) {
        try {
            await _jsRuntime.InvokeVoidAsync("NTBlazorErrorBoundary.queueReport", cancellationToken, report);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or NotSupportedException) {
            _logger.LogDebug(exception, "Browser report queue was unavailable.");
        }
    }
}
