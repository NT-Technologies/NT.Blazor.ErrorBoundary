namespace NT.Blazor.ErrorBoundary.Models;

/// <summary>
/// Describes one browser navigation or lifecycle event retained before an error report is created.
/// </summary>
public sealed record NTBlazorBreadcrumb {
    /// <summary>
    /// Gets whether Blazor intercepted the navigation.
    /// </summary>
    public bool? IsNavigationIntercepted { get; init; }

    /// <summary>
    /// Gets the UTC time when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>
    /// Gets the navigation phase represented by this breadcrumb.
    /// </summary>
    public string? Phase { get; init; }

    /// <summary>
    /// Gets the sanitized navigation target URI.
    /// </summary>
    public string? TargetUri { get; init; }

    /// <summary>
    /// Gets the sanitized browser URI active when the event occurred.
    /// </summary>
    public string? Uri { get; init; }
}
