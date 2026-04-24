namespace NT.Blazor.ErrorBoundary.Services;

/// <summary>
/// Configures fallback UI behavior for <see cref="Components.NTErrorBoundary"/>.
/// </summary>
public sealed class NTErrorBoundaryOptions {
    /// <summary>
    /// Gets or sets whether the current host environment is development.
    /// </summary>
    public bool IsDevelopmentEnvironment { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of characters rendered for exception details.
    /// </summary>
    public int MaxExceptionDetailsLength { get; set; } = 16_384;
}
