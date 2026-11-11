using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics;

namespace NT.Blazor.ErrorBoundary.Components;

/// <summary>
/// Renders the browser telemetry script tag configured from the registered report options.
/// </summary>
public partial class NTBlazorErrorBoundaryScript(IOptions<NTBlazorErrorBoundaryHttpClientOptions> _options) {
    /// <summary>
    /// Gets or sets the navigation stall timeout in milliseconds.
    /// </summary>
    [Parameter]
    public int? StallTimeoutMs { get; set; }

    // The server renders the page and script, so reports the browser script raises carry the server app's version.
    private static string? ApplicationVersion => NTBlazorErrorReport.EntryAssemblyVersion;

    private static string? TraceParent => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
}
