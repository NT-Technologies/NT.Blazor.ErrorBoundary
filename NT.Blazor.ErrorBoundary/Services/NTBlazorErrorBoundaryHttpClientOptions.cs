namespace NT.Blazor.ErrorBoundary.Services;

/// <summary>
/// Configures HTTP reporting for client-side Blazor error boundary exceptions.
/// </summary>
public sealed class NTBlazorErrorBoundaryHttpClientOptions {
    /// <summary>
    /// The default maximum number of characters retained for each reported string field.
    /// </summary>
    public const int DefaultMaxReportFieldLength = 16_384;

    /// <summary>
    /// Gets or sets the relative or absolute endpoint used to submit client error reports.
    /// </summary>
    public required string ReportUri { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of characters retained for each reported string field.
    /// </summary>
    public int MaxReportFieldLength { get; set; } = DefaultMaxReportFieldLength;
}
