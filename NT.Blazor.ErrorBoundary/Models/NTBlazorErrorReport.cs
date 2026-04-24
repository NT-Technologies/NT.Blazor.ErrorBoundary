using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.Models;

/// <summary>
/// Describes an unhandled Blazor component exception reported from a client-side render mode.
/// </summary>
public sealed class NTBlazorErrorReport {
    private const string TruncatedReportFieldMessage = "\n... report field truncated ...";

    /// <summary>
    /// Gets the boundary component that caught the exception.
    /// </summary>
    public string? BoundaryName { get; init; }

    /// <summary>
    /// Gets the exception message for diagnostic logging.
    /// </summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>
    /// Gets the exception stack trace for diagnostic logging.
    /// </summary>
    public string? ExceptionStackTrace { get; init; }

    /// <summary>
    /// Gets the fully qualified exception type name.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Gets whether the boundary was interactive when the error was caught.
    /// </summary>
    public bool? IsInteractive { get; init; }

    /// <summary>
    /// Gets the UTC time when the report was created.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>
    /// Gets the renderer name that handled the boundary.
    /// </summary>
    public string? RenderMode { get; init; }

    /// <summary>
    /// Gets the browser URI active when the error was caught.
    /// </summary>
    public string? Uri { get; init; }

    /// <summary>
    /// Creates a report from an exception and boundary context.
    /// </summary>
    public static NTBlazorErrorReport FromException(Exception exception, NTBlazorErrorBoundaryContext context, int maxFieldLength = NTBlazorErrorBoundaryHttpClientOptions.DefaultMaxReportFieldLength) {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        return new NTBlazorErrorReport {
            BoundaryName = Truncate(context.BoundaryName, maxFieldLength),
            ExceptionMessage = Truncate(exception.Message, maxFieldLength),
            ExceptionStackTrace = Truncate(exception.StackTrace, maxFieldLength),
            ExceptionType = Truncate(exception.GetType().FullName, maxFieldLength),
            IsInteractive = context.IsInteractive,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            RenderMode = Truncate(context.RenderMode, maxFieldLength),
            Uri = Truncate(context.Uri, maxFieldLength)
        };
    }

    /// <summary>
    /// Creates a report with string fields capped to the specified maximum length.
    /// </summary>
    public NTBlazorErrorReport WithTruncatedFields(int maxFieldLength) {
        return new NTBlazorErrorReport {
            BoundaryName = Truncate(BoundaryName, maxFieldLength),
            ExceptionMessage = Truncate(ExceptionMessage, maxFieldLength),
            ExceptionStackTrace = Truncate(ExceptionStackTrace, maxFieldLength),
            ExceptionType = Truncate(ExceptionType, maxFieldLength),
            IsInteractive = IsInteractive,
            OccurredAtUtc = OccurredAtUtc,
            RenderMode = Truncate(RenderMode, maxFieldLength),
            Uri = Truncate(Uri, maxFieldLength)
        };
    }

    private static string? Truncate(string? value, int maxLength) {
        if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength) {
            return value;
        }

        if (maxLength <= TruncatedReportFieldMessage.Length) {
            return value[..maxLength];
        }

        return string.Concat(value.AsSpan(0, maxLength - TruncatedReportFieldMessage.Length), TruncatedReportFieldMessage);
    }
}
