using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal sealed class NTBlazorCircuitDiagnosticsHandler(ILogger<NTBlazorCircuitDiagnosticsHandler> _logger, NTBlazorCircuitContext _context) : CircuitHandler {
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) {
        Log(circuit.Id, LogLevel.Information, "Blazor circuit {BlazorCircuitId} opened");
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken) {
        Log(circuit.Id, LogLevel.Information, "Blazor circuit {BlazorCircuitId} closed");
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken) {
        Log(circuit.Id, LogLevel.Warning, "Blazor circuit {BlazorCircuitId} connection down");
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) {
        Log(circuit.Id, LogLevel.Information, "Blazor circuit {BlazorCircuitId} connection up");
        return Task.CompletedTask;
    }

    // Circuit has no public constructor, so the logic takes the id and tests call it directly.
    internal void Log(string circuitId, LogLevel level, string message) {
        _context.CircuitId = circuitId;
        using (_logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["BlazorCircuitId"] = circuitId })) {
            _logger.Log(level, message, circuitId);
        }
    }
}
