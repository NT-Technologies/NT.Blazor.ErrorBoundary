using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics;

namespace NT.Blazor.ErrorBoundary.Components;

/// <summary>
/// Renders the browser telemetry script tag configured from the registered report options.
/// </summary>
public partial class NTBlazorErrorBoundaryScript(IOptions<NTBlazorErrorBoundaryHttpClientOptions> _options) {
    /// <summary>
    /// Gets or sets the deployed application version sent with browser reports.
    /// </summary>
    [Parameter]
    public string? ApplicationVersion { get; set; }

    /// <summary>
    /// Gets or sets the navigation stall timeout in milliseconds.
    /// </summary>
    [Parameter]
    public int? StallTimeoutMs { get; set; }

    private static string? TraceParent => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
}
