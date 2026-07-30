using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.Models;

/// <summary>
/// Describes an unhandled Blazor or browser exception reported from a client-side render mode.
/// </summary>
public sealed class NTBlazorErrorReport {
    private const string TruncatedReportFieldMessage = "\n... report field truncated ...";

    /// <summary>
    /// Gets the deployed application version.
    /// </summary>
    public string? ApplicationVersion { get; init; }

    /// <summary>
    /// Gets the boundary component or browser subsystem that caught the exception.
    /// </summary>
    public string? BoundaryName { get; init; }

    /// <summary>
    /// Gets the recent browser navigation breadcrumbs.
    /// </summary>
    public IReadOnlyList<NTBlazorBreadcrumb> Breadcrumbs { get; init; } = [];

    /// <summary>
    /// Gets the browser-session correlation identifier.
    /// </summary>
    public string? ClientSessionId { get; init; }

    /// <summary>
    /// Gets the complete formatted exception, including inner exceptions.
    /// </summary>
    public string? ExceptionDetails { get; init; }

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
    /// Gets whether the browser reports an active network connection.
    /// </summary>
    public bool? IsOnline { get; init; }

    /// <summary>
    /// Gets whether the boundary was interactive when the error was caught.
    /// </summary>
    public bool? IsInteractive { get; init; }

    /// <summary>
    /// Gets whether Blazor intercepted the current navigation.
    /// </summary>
    public bool? IsNavigationIntercepted { get; init; }

    /// <summary>
    /// Gets the JavaScript source column for a browser error.
    /// </summary>
    public int? JavaScriptColumn { get; init; }

    /// <summary>
    /// Gets the JavaScript source line for a browser error.
    /// </summary>
    public int? JavaScriptLine { get; init; }

    /// <summary>
    /// Gets the JavaScript source URL for a browser error.
    /// </summary>
    public string? JavaScriptSource { get; init; }

    /// <summary>
    /// Gets the current navigation correlation identifier.
    /// </summary>
    public string? NavigationId { get; init; }

    /// <summary>
    /// Gets the last observed navigation phase.
    /// </summary>
    public string? NavigationPhase { get; init; }

    /// <summary>
    /// Gets the UTC time when the report was created.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>
    /// Gets the browser URI where the reporting boundary was created.
    /// </summary>
    public string? OriginUri { get; init; }

    /// <summary>
    /// Gets the report classification, such as Exception, JavaScriptError, or NavigationStalled.
    /// </summary>
    public string ReportKind { get; init; } = NTBlazorReportKinds.Exception;

    /// <summary>
    /// Gets the renderer name that handled the boundary.
    /// </summary>
    public string? RenderMode { get; init; }

    /// <summary>
    /// Gets the current navigation target URI.
    /// </summary>
    public string? TargetUri { get; init; }

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
            ExceptionDetails = Truncate(exception.ToString(), maxFieldLength),
            ExceptionMessage = Truncate(exception.Message, maxFieldLength),
            ExceptionStackTrace = Truncate(exception.StackTrace, maxFieldLength),
            ExceptionType = Truncate(exception.GetType().FullName, maxFieldLength),
            IsInteractive = context.IsInteractive,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            OriginUri = Truncate(context.OriginUri, maxFieldLength),
            RenderMode = Truncate(context.RenderMode, maxFieldLength),
            Uri = Truncate(context.Uri, maxFieldLength)
        };
    }

    /// <summary>
    /// Creates a report enriched with independently collected browser navigation context.
    /// </summary>
    public NTBlazorErrorReport WithClientContext(NTBlazorClientContext context, int maxFieldLength = NTBlazorErrorBoundaryHttpClientOptions.DefaultMaxReportFieldLength) {
        ArgumentNullException.ThrowIfNull(context);

        return Copy(
            applicationVersion: context.ApplicationVersion,
            breadcrumbs: context.Breadcrumbs,
            clientSessionId: context.ClientSessionId,
            isOnline: context.IsOnline,
            isNavigationIntercepted: context.IsNavigationIntercepted,
            navigationId: context.NavigationId,
            navigationPhase: context.NavigationPhase,
            originUri: OriginUri ?? context.OriginUri,
            targetUri: context.TargetUri,
            uri: context.CurrentUri ?? Uri,
            maxFieldLength);
    }

    /// <summary>
    /// Creates a report with string fields capped to the specified maximum length.
    /// </summary>
    public NTBlazorErrorReport WithTruncatedFields(int maxFieldLength) {
        return Copy(
            ApplicationVersion,
            Breadcrumbs,
            ClientSessionId,
            IsOnline,
            IsNavigationIntercepted,
            NavigationId,
            NavigationPhase,
            OriginUri,
            TargetUri,
            Uri,
            maxFieldLength);
    }

    private NTBlazorErrorReport Copy(string? applicationVersion, IReadOnlyList<NTBlazorBreadcrumb> breadcrumbs, string? clientSessionId, bool? isOnline, bool? isNavigationIntercepted, string? navigationId, string? navigationPhase, string? originUri, string? targetUri, string? uri, int maxFieldLength) {
        return new NTBlazorErrorReport {
            ApplicationVersion = Truncate(applicationVersion, maxFieldLength),
            BoundaryName = Truncate(BoundaryName, maxFieldLength),
            Breadcrumbs = breadcrumbs
                .TakeLast(20)
                .Select(breadcrumb => new NTBlazorBreadcrumb {
                    IsNavigationIntercepted = breadcrumb.IsNavigationIntercepted,
                    OccurredAtUtc = breadcrumb.OccurredAtUtc,
                    Phase = Truncate(breadcrumb.Phase, maxFieldLength),
                    TargetUri = Truncate(breadcrumb.TargetUri, maxFieldLength),
                    Uri = Truncate(breadcrumb.Uri, maxFieldLength)
                })
                .ToArray(),
            ClientSessionId = Truncate(clientSessionId, maxFieldLength),
            ExceptionDetails = Truncate(ExceptionDetails, maxFieldLength),
            ExceptionMessage = Truncate(ExceptionMessage, maxFieldLength),
            ExceptionStackTrace = Truncate(ExceptionStackTrace, maxFieldLength),
            ExceptionType = Truncate(ExceptionType, maxFieldLength),
            IsOnline = isOnline,
            IsInteractive = IsInteractive,
            IsNavigationIntercepted = isNavigationIntercepted,
            JavaScriptColumn = JavaScriptColumn,
            JavaScriptLine = JavaScriptLine,
            JavaScriptSource = Truncate(JavaScriptSource, maxFieldLength),
            NavigationId = Truncate(navigationId, maxFieldLength),
            NavigationPhase = Truncate(navigationPhase, maxFieldLength),
            OccurredAtUtc = OccurredAtUtc,
            OriginUri = Truncate(originUri, maxFieldLength),
            RenderMode = Truncate(RenderMode, maxFieldLength),
            ReportKind = Truncate(ReportKind, maxFieldLength) ?? NTBlazorReportKinds.Exception,
            TargetUri = Truncate(targetUri, maxFieldLength),
            Uri = Truncate(uri, maxFieldLength)
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

/// <summary>
/// Defines standard client telemetry report classifications.
/// </summary>
public static class NTBlazorReportKinds {
    /// <summary>
    /// A .NET exception caught by an error boundary or logger.
    /// </summary>
    public const string Exception = "Exception";

    /// <summary>
    /// A browser JavaScript error.
    /// </summary>
    public const string JavaScriptError = "JavaScriptError";

    /// <summary>
    /// An unhandled browser promise rejection.
    /// </summary>
    public const string JavaScriptUnhandledRejection = "JavaScriptUnhandledRejection";

    /// <summary>
    /// A navigation that failed to reach its expected completion marker.
    /// </summary>
    public const string NavigationStalled = "NavigationStalled";
}
