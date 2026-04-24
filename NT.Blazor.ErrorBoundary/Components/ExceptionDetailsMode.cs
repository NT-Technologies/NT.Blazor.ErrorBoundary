namespace NT.Blazor.ErrorBoundary.Components;

/// <summary>
/// Defines when <see cref="NTErrorBoundary"/> renders exception details in fallback UI.
/// </summary>
public enum ExceptionDetailsMode {
    /// <summary>
    /// Render exception details only when the host environment is development.
    /// </summary>
    DevelopmentOnly,

    /// <summary>
    /// Always render exception details.
    /// </summary>
    Always,

    /// <summary>
    /// Never render exception details.
    /// </summary>
    Never
}
