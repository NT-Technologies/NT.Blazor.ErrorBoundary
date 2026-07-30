namespace NT.Blazor.ErrorBoundary.Models;

/// <summary>
/// Provides render context for exceptions caught by a Blazor error boundary.
/// </summary>
public sealed record NTBlazorErrorBoundaryContext {
    /// <summary>
    /// Gets the fully qualified boundary component name.
    /// </summary>
    public string? BoundaryName { get; init; }

    /// <summary>
    /// Gets the browser URI where the boundary instance was created.
    /// </summary>
    public string? OriginUri { get; init; }

    /// <summary>
    /// Gets whether the boundary was rendered interactively when the error was caught.
    /// </summary>
    public bool? IsInteractive { get; init; }

    /// <summary>
    /// Gets the renderer name that handled the boundary.
    /// </summary>
    public string? RenderMode { get; init; }

    /// <summary>
    /// Gets the browser URI active when the error was caught.
    /// </summary>
    public string? Uri { get; init; }
}
