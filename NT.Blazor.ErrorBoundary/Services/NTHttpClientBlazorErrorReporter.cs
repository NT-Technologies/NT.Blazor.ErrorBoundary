using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using System.Net.Http.Json;

namespace NT.Blazor.ErrorBoundary.Services;

internal sealed class NTHttpClientBlazorErrorReporter : INTBlazorErrorReporter {

    private readonly HttpClient _httpClient;
    private readonly ILogger<NTHttpClientBlazorErrorReporter> _logger;
    private readonly int _maxReportFieldLength;
    private readonly string _reportUri;

    public NTHttpClientBlazorErrorReporter(HttpClient httpClient, IOptions<NTBlazorErrorBoundaryHttpClientOptions> options, ILogger<NTHttpClientBlazorErrorReporter> logger) {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ReportUri);

        _httpClient = httpClient;
        _logger = logger;
        _maxReportFieldLength = options.Value.MaxReportFieldLength;
        _reportUri = options.Value.ReportUri;
    }

    public async Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
        try {
            var report = NTBlazorErrorReport.FromException(exception, context, _maxReportFieldLength);
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
}
