using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal sealed class NTBlazorServerErrorReporter(ILogger<NTBlazorServerErrorReporter> _logger, IOptions<NTBlazorErrorBoundaryHttpClientOptions> _options, NTBlazorCircuitContext _circuit, INTBlazorErrorBoundaryExporter? _exporter = null) : INTBlazorErrorReporter {
    public Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.Value;
        var report = NTBlazorErrorReport.FromException(exception, context, options.MaxReportFieldLength);
        NTBlazorErrorReport? filtered;
        try {
            filtered = options.FilterReport is null ? report : options.FilterReport(report);
        }
        catch (Exception filterException) {
            // As for browser reports: drop rather than log unfiltered data, and do not throw from inside the boundary's error handling.
            _logger.LogWarning(filterException, "FilterReport threw; the Blazor server error was dropped.");
            return Task.CompletedTask;
        }

        if (filtered is not null) {
            var fields = CreateFields(filtered);
            NTBlazorLog.Write(_logger, LogLevel.Error, exception, fields, "Unhandled Blazor component exception caught by {blazor.boundary.name}");
            NTBlazorLog.Export(_logger, _exporter, target => target.ExportException(filtered, fields));
            NTBlazorErrorBoundaryTelemetry.RecordServerError(filtered.ReportKind, filtered.BoundaryName);
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, object?> CreateFields(NTBlazorErrorReport report) {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["blazor.app.version"] = report.ApplicationVersion,
            ["blazor.boundary.name"] = report.BoundaryName,
            ["blazor.error.fingerprint"] = NTBlazorErrorFingerprint.Compute(report),
            ["blazor.error.reference_id"] = report.ReferenceId,
            ["blazor.interactive"] = report.IsInteractive,
            ["blazor.navigation.origin_url"] = report.OriginUri,
            ["blazor.render_mode"] = report.RenderMode,
            ["url.full"] = report.Uri
        };

        if (_circuit.CircuitId is { } circuitId) {
            fields["aspnetcore.components.circuit.id"] = circuitId;
        }

        return fields;
    }
}
