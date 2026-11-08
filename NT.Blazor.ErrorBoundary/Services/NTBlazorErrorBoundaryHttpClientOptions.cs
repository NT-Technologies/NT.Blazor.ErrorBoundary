using NT.Blazor.ErrorBoundary.Models;

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

    /// <summary>
    /// Gets or sets an optional filter that redacts a report by returning a modified copy, or drops it by returning <see langword="null"/>.
    /// The client applies it before sending and the server applies it before logging.
    /// </summary>
    public Func<NTBlazorErrorReport, NTBlazorErrorReport?>? FilterReport { get; set; }

    /// <summary>
    /// Gets or sets whether the client reports unhandled Blazor exceptions that occur outside any error boundary.
    /// </summary>
    public bool ReportUnhandledExceptions { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum request body size, in bytes, accepted by the server telemetry endpoint.
    /// </summary>
    public long MaxReportRequestBytes { get; set; } = 1_048_576;
}
