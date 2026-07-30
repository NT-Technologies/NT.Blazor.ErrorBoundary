namespace NT.Blazor.ErrorBoundary.Models;

/// <summary>
/// Describes browser and navigation context collected independently of the Blazor renderer.
/// </summary>
public sealed record NTBlazorClientContext {
    /// <summary>
    /// Gets the deployed application version.
    /// </summary>
    public string? ApplicationVersion { get; init; }

    /// <summary>
    /// Gets the recent browser navigation breadcrumbs.
    /// </summary>
    public IReadOnlyList<NTBlazorBreadcrumb> Breadcrumbs { get; init; } = [];

    /// <summary>
    /// Gets the browser-session correlation identifier.
    /// </summary>
    public string? ClientSessionId { get; init; }

    /// <summary>
    /// Gets the sanitized current browser URI.
    /// </summary>
    public string? CurrentUri { get; init; }

    /// <summary>
    /// Gets whether the browser reports an active network connection.
    /// </summary>
    public bool? IsOnline { get; init; }

    /// <summary>
    /// Gets whether Blazor intercepted the current navigation.
    /// </summary>
    public bool? IsNavigationIntercepted { get; init; }

    /// <summary>
    /// Gets the current navigation correlation identifier.
    /// </summary>
    public string? NavigationId { get; init; }

    /// <summary>
    /// Gets the last observed navigation phase.
    /// </summary>
    public string? NavigationPhase { get; init; }

    /// <summary>
    /// Gets the sanitized URI where the current navigation began.
    /// </summary>
    public string? OriginUri { get; init; }

    /// <summary>
    /// Gets the sanitized current navigation target URI.
    /// </summary>
    public string? TargetUri { get; init; }
}
