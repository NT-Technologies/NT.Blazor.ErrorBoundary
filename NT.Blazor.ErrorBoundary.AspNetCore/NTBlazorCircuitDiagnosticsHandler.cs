using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal sealed class NTBlazorCircuitDiagnosticsHandler(ILogger<NTBlazorCircuitDiagnosticsHandler> _logger, NTBlazorCircuitContext _context, INTBlazorErrorBoundaryExporter? _exporter = null) : CircuitHandler {
    // Opens and closes are not logged: they are high-volume, and ASP.NET Core's circuit metrics already count them.
    // The ID is still recorded so server-rendered error logs carry it.
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) {
        _context.CircuitId = circuit.Id;
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken) {
        OnConnectionChanged(circuit.Id, isUp: false);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) {
        OnConnectionChanged(circuit.Id, isUp: true);
        return Task.CompletedTask;
    }

    // Circuit has no public constructor, so the overrides pass only its ID and tests call this directly.
    internal void OnConnectionChanged(string circuitId, bool isUp) {
        _context.CircuitId = circuitId;
        // ASP.NET Core's own circuit ID attribute name, so these events line up with its built-in circuit telemetry.
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal) { ["aspnetcore.components.circuit.id"] = circuitId };
        var eventId = isUp ? NTBlazorLog.CircuitConnectionUp : NTBlazorLog.CircuitConnectionDown;
        NTBlazorLog.WriteEvent(_logger, isUp ? LogLevel.Information : LogLevel.Warning, eventId, fields, isUp ? "Blazor circuit {aspnetcore.components.circuit.id} connection up" : "Blazor circuit {aspnetcore.components.circuit.id} connection down");
        NTBlazorLog.Export(_logger, _exporter, target => target.ExportEvent(eventId.Name!, DateTimeOffset.UtcNow, fields));
    }
}
