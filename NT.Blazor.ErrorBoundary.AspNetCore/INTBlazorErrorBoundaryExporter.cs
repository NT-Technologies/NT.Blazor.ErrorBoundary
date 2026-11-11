using NT.Blazor.ErrorBoundary.Models;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

/// <summary>
/// Receives every exception and event this package logs, for a backend that needs them delivered directly rather than through the log pipeline,
/// for example to send browser exceptions as native exceptions or to keep them out of log sampling. Called in addition to logging, on the thread that
/// reported the error and inside its trace when one is known; implementations must not block.
/// </summary>
public interface INTBlazorErrorBoundaryExporter {
    /// <summary>
    /// Exports a server-rendered exception or a browser exception report.
    /// </summary>
    /// <param name="report">The truncated and filtered report. For server-rendered errors it is built from the caught exception.</param>
    /// <param name="attributes">The attributes logged with the exception, including <c>blazor.error.fingerprint</c>.</param>
    void ExportException(NTBlazorErrorReport report, IReadOnlyDictionary<string, object?> attributes);

    /// <summary>
    /// Exports an event: <c>blazor.navigation.stalled</c>, <c>blazor.reconnect.failed</c>, <c>blazor.circuit.connection_down</c>, or <c>blazor.circuit.connection_up</c>.
    /// </summary>
    /// <param name="name">The event name, which is also the log entry's <c>EventId</c> name.</param>
    /// <param name="occurredAt">When the event happened; browser events can arrive later than they occurred.</param>
    /// <param name="attributes">The attributes logged with the event.</param>
    void ExportEvent(string name, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> attributes);
}
