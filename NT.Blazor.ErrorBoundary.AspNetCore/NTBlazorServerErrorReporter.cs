using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal sealed class NTBlazorServerErrorReporter(ILogger<NTBlazorServerErrorReporter> _logger, IOptions<NTBlazorErrorBoundaryHttpClientOptions> _options, NTBlazorCircuitContext _circuit) : INTBlazorErrorReporter {
    public Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.Value;
        var report = NTBlazorErrorReport.FromException(exception, context, options.MaxReportFieldLength);
        if ((options.FilterReport is null ? report : options.FilterReport(report)) is { } filtered) {
            using (_logger.BeginScope(CreateScope(filtered))) {
                _logger.LogError(exception, "Unhandled Blazor component exception caught by {BoundaryName}", filtered.BoundaryName);
            }

            NTBlazorErrorBoundaryMetrics.RecordServerError(filtered.ReportKind, filtered.BoundaryName);
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, object?> CreateScope(NTBlazorErrorReport report) {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["BlazorBoundaryName"] = report.BoundaryName,
            ["BlazorErrorReferenceId"] = report.ReferenceId,
            ["BlazorIsInteractive"] = report.IsInteractive,
            ["BlazorOriginUri"] = report.OriginUri,
            ["BlazorRenderMode"] = report.RenderMode,
            ["BlazorUri"] = report.Uri
        };

        if (_circuit.CircuitId is { } circuitId) {
            scope["BlazorCircuitId"] = circuitId;
        }

        return scope;
    }
}
