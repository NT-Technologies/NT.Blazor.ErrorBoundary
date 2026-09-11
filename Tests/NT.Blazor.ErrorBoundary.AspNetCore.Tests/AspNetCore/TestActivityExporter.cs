using OpenTelemetry;
using System.Diagnostics;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

internal sealed class TestActivityExporter(ActivityTraceId _traceId) : BaseExporter<Activity> {
    private readonly TaskCompletionSource<Activity> _exportedActivity = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Activity> ExportedActivity => _exportedActivity.Task;

    public override ExportResult Export(in Batch<Activity> batch) {
        foreach (var activity in batch) {
            if (activity.TraceId == _traceId && activity.Kind == ActivityKind.Server) {
                _exportedActivity.TrySetResult(activity);
            }
        }

        return ExportResult.Success;
    }
}
