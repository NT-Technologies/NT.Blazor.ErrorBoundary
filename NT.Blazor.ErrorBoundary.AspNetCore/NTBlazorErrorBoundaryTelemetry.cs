using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal static class NTBlazorErrorBoundaryTelemetry {
    internal const string ErrorsCounterName = "blazor.errors";

    internal static readonly ActivitySource Source = new(NTBlazorErrorBoundaryAspNetCoreExtensions.ActivitySourceName);

    private static readonly Meter Meter = new(NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName);
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>(ErrorsCounterName, "{error}", "Blazor errors logged by the error boundary integration.");

    internal static void RecordServerError(string reportKind, string? boundary) {
        Errors.Add(1, new TagList { { "blazor.error.source", "server" }, { "blazor.report.kind", reportKind }, { "blazor.boundary.name", boundary } });
    }

    internal static void RecordClientError(string reportKind) {
        Errors.Add(1, new TagList { { "blazor.error.source", "client" }, { "blazor.report.kind", reportKind } });
    }
}
