using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal static class NTBlazorErrorBoundaryMetrics {
    internal const string ErrorsCounterName = "nt.blazor.error_boundary.errors";

    private static readonly Meter Meter = new(NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName);
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>(ErrorsCounterName, description: "Blazor errors logged by the error boundary integration.");

    internal static void RecordServerError(string reportKind, string? boundary) {
        Errors.Add(1, new TagList { { "source", "server" }, { "report_kind", reportKind }, { "boundary", boundary } });
    }

    internal static void RecordClientError(string reportKind) {
        Errors.Add(1, new TagList { { "source", "client" }, { "report_kind", reportKind } });
    }
}
