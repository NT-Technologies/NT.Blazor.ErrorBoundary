using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace NT.Blazor.ErrorBoundary.Components;

/// <summary>
/// Marks an interactive route as ready and records Blazor location-changing events for browser telemetry.
/// </summary>
public sealed class NTBlazorInteractiveReady(IJSRuntime _jsRuntime, NavigationManager _navigationManager, ILogger<NTBlazorInteractiveReady> _logger) : ComponentBase, IDisposable {
    private IDisposable? _locationChangingRegistration;

    /// <inheritdoc />
    protected override void OnInitialized() {
        _locationChangingRegistration = _navigationManager.RegisterLocationChangingHandler(OnLocationChangingAsync);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!firstRender || !RendererInfo.IsInteractive) {
            return;
        }

        try {
            await _jsRuntime.InvokeVoidAsync("NTBlazorErrorBoundary.markInteractiveReady");
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException) {
            _logger.LogDebug(exception, "Browser telemetry was unavailable while marking the route interactive.");
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        _locationChangingRegistration?.Dispose();
        _locationChangingRegistration = null;
        GC.SuppressFinalize(this);
    }

    private async ValueTask OnLocationChangingAsync(LocationChangingContext context) {
        try {
            await _jsRuntime.InvokeVoidAsync(
                "NTBlazorErrorBoundary.markLocationChanging",
                context.CancellationToken,
                context.TargetLocation,
                context.IsNavigationIntercepted);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException) {
            _logger.LogDebug(exception, "Browser telemetry was unavailable while recording a location change.");
        }
    }
}
