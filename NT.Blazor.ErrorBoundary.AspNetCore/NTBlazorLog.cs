using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

// Fields travel on the log entry rather than in a scope: the Azure Monitor distro drops scopes by default, and its exporter maps names such as enduser.id and microsoft.custom_event.name only from entry attributes.
internal static partial class NTBlazorLog {
    internal static readonly EventId NavigationStalled = new(1, "blazor.navigation.stalled");
    internal static readonly EventId ReconnectFailed = new(2, "blazor.reconnect.failed");
    internal static readonly EventId CircuitConnectionDown = new(3, "blazor.circuit.connection_down");
    internal static readonly EventId CircuitConnectionUp = new(4, "blazor.circuit.connection_up");

    internal static void Write(ILogger logger, LogLevel level, Exception exception, Dictionary<string, object?> fields, string template) {
        Log(logger, level, default, exception, fields, template);
    }

    // OpenTelemetry exporters send the EventId name as the log's event name; Azure Monitor turns microsoft.custom_event.name into a customEvents row.
    internal static void WriteEvent(ILogger logger, LogLevel level, EventId eventId, Dictionary<string, object?> fields, string template) {
        fields["microsoft.custom_event.name"] = eventId.Name;
        Log(logger, level, eventId, null, fields, template);
    }

    // An exporter is an extension point: one that throws must not fail the report request, skip the metric, or escape into the circuit.
    internal static void Export(ILogger logger, INTBlazorErrorBoundaryExporter? exporter, Action<INTBlazorErrorBoundaryExporter> export) {
        if (exporter is null) {
            return;
        }

        try {
            export(exporter);
        }
        catch (Exception exception) {
            logger.LogWarning(exception, "The registered INTBlazorErrorBoundaryExporter threw; the error or event was still logged.");
        }
    }

    private static void Log(ILogger logger, LogLevel level, EventId eventId, Exception? exception, Dictionary<string, object?> fields, string template) {
        List<KeyValuePair<string, object?>> state = [.. fields, new("{OriginalFormat}", template)];
        // Client-controlled values must not add lines to plain-text sinks (CWE-117), so line breaks are replaced in the message only; the state keeps the raw values.
        logger.Log(level, eventId, state, exception, (_, _) => Placeholder().Replace(template, match => fields.TryGetValue(match.Groups[1].Value, out var value) ? value is null ? "(null)" : Convert.ToString(value, CultureInfo.InvariantCulture)?.ReplaceLineEndings(" ") ?? string.Empty : match.Value));
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Placeholder();
}
