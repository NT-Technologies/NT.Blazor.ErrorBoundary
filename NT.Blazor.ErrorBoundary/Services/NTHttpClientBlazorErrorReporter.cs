using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using NT.Blazor.ErrorBoundary.Models;
using System.Net.Http.Json;

namespace NT.Blazor.ErrorBoundary.Services;

internal sealed class NTHttpClientBlazorErrorReporter : INTBlazorErrorReporter {

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
            if (await SubmitThroughBrowserAsync(report, cancellationToken)) {
                return;
            }

            using var response = await _httpClient.PostAsJsonAsync(_reportUri, report, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception reportException) {
            _logger.LogWarning(reportException, "Failed to submit Blazor error report.");
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

    private async Task<bool> SubmitThroughBrowserAsync(NTBlazorErrorReport report, CancellationToken cancellationToken) {
        try {
            return await _jsRuntime.InvokeAsync<bool>(
                "NTBlazorErrorBoundary.submitReport",
                cancellationToken,
                report);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or NotSupportedException) {
            _logger.LogDebug(exception, "Durable browser telemetry transport was unavailable.");
            return false;
        }
    }
}
