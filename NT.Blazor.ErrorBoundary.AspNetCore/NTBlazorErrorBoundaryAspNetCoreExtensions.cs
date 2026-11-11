using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

/// <summary>
/// Registers ASP.NET Core services and endpoints for Blazor error boundary reporting.
/// </summary>
public static class NTBlazorErrorBoundaryAspNetCoreExtensions {
    /// <summary>
    /// The name of the meter that publishes the <c>blazor.errors</c> counter. <see cref="AddNTBlazorErrorBoundaryServerLogging"/> registers it with the host's OpenTelemetry meter provider.
    /// </summary>
    public const string MeterName = "NT.Blazor.ErrorBoundary";

    /// <summary>
    /// The name of the activity source that traces browser reports uploaded without a <c>traceparent</c> header. <see cref="AddNTBlazorErrorBoundaryServerLogging"/> registers it with the host's OpenTelemetry tracer provider.
    /// </summary>
    public const string ActivitySourceName = "NT.Blazor.ErrorBoundary";

    /// <summary>
    /// Adds a server-side logger for errors caught by <see cref="NT.Blazor.ErrorBoundary.Components.NTErrorBoundary"/>,
    /// and registers <see cref="MeterName"/> and <see cref="ActivitySourceName"/> with the host's OpenTelemetry pipeline when it has one.
    /// </summary>
    public static IServiceCollection AddNTBlazorErrorBoundaryServerLogging(this IServiceCollection services, string reportUri) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportUri);

        services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
            options.ReportUri = reportUri;
        });
        services.AddOptions<NTErrorBoundaryOptions>()
            .Configure<IHostEnvironment>((options, environment) => {
                options.IsDevelopmentEnvironment = environment.IsDevelopment();
            });
        services.TryAddScoped<NTBlazorCircuitContext>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, NTBlazorCircuitDiagnosticsHandler>());
        services.AddScoped<INTBlazorErrorReporter, NTBlazorServerErrorReporter>();
        // Applies only if the host configures OpenTelemetry metrics or tracing; otherwise nothing is collected.
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(MeterName));
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(ActivitySourceName));
        return services;
    }

    /// <inheritdoc cref="AddNTBlazorErrorBoundaryServerLogging"/>
    [Obsolete("Use AddNTBlazorErrorBoundaryServerLogging.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddBlazorErrorBoundaryServerLogging(this IServiceCollection services, string reportUri) => services.AddNTBlazorErrorBoundaryServerLogging(reportUri);

    /// <inheritdoc cref="MapNTBlazorErrorBoundaryTelemetry"/>
    [Obsolete("Use MapNTBlazorErrorBoundaryTelemetry.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RouteHandlerBuilder MapBlazorErrorBoundaryTelemetry(this IEndpointRouteBuilder endpoints) => endpoints.MapNTBlazorErrorBoundaryTelemetry();

    /// <summary>
    /// Maps an endpoint that receives client-side Blazor error reports and logs them through <see cref="ILogger"/>.
    /// The endpoint rejects oversized bodies with 413, unknown report kinds or implausible timestamps with 400, and honors <see cref="NTBlazorErrorBoundaryHttpClientOptions.FilterReport"/>.
    /// </summary>
    public static RouteHandlerBuilder MapNTBlazorErrorBoundaryTelemetry(this IEndpointRouteBuilder endpoints) {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<NTBlazorErrorBoundaryHttpClientOptions>>().Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ReportUri);

        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("NT.Blazor.ErrorBoundary.ClientReport");
        return endpoints.MapPost(options.ReportUri, (NTBlazorErrorReport report, HttpContext httpContext) => HandleClientReport(logger, report, httpContext, options))
            .WithMetadata(new RequestSizeLimitAttribute(options.MaxReportRequestBytes))
            .WithName("Report Blazor Error")
            .WithSummary("Reports a client-side Blazor error boundary exception.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413PayloadTooLarge);
    }

    internal static IResult HandleClientReport(ILogger logger, NTBlazorErrorReport report, HttpContext httpContext, NTBlazorErrorBoundaryHttpClientOptions options) {
        var now = (httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        if (!NTBlazorReportKinds.IsKnown(report.ReportKind) || report.OccurredAtUtc > now.AddMinutes(5) || report.OccurredAtUtc < now.AddDays(-7)) {
            return Results.BadRequest();
        }

        // A client that does not use the package's script can send query strings and fragments, which often carry tokens, so they are removed here too.
        var processed = (report with {
            JavaScriptSource = RemoveQueryAndFragment(report.JavaScriptSource),
            OriginUri = RemoveQueryAndFragment(report.OriginUri),
            TargetUri = RemoveQueryAndFragment(report.TargetUri),
            Uri = RemoveQueryAndFragment(report.Uri),
            Breadcrumbs = [.. report.Breadcrumbs.Select(breadcrumb => breadcrumb with { TargetUri = RemoveQueryAndFragment(breadcrumb.TargetUri), Uri = RemoveQueryAndFragment(breadcrumb.Uri) })]
        }).WithTruncatedFields(options.MaxReportFieldLength);
        NTBlazorErrorReport? filtered;
        try {
            filtered = options.FilterReport is null ? processed : options.FilterReport(processed);
        }
        catch (Exception exception) {
            // Drop rather than log unfiltered data, and still answer 204 so browsers do not retry a report that would fail again.
            logger.LogWarning(exception, "FilterReport threw; the Blazor client report was dropped.");
            return Results.NoContent();
        }

        if (filtered is not null) {
            LogClientReport(logger, filtered, httpContext);
        }

        return Results.NoContent();
    }

    private static string? RemoveQueryAndFragment(string? uri) {
        var index = uri?.AsSpan().IndexOfAny('?', '#') ?? -1;
        return index < 0 ? uri : uri![..index];
    }

    internal static void LogClientReport(ILogger logger, NTBlazorErrorReport report, HttpContext httpContext) {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(httpContext);

        // Beacon uploads cannot send a traceparent header, so the report's own value links the log to the page's trace.
        // The span exports when the host registers ActivitySourceName; otherwise an unexported activity still carries the trace ID.
        using var pageActivity = ActivityContext.TryParse(report.TraceParent, null, out var page) && page.TraceId != Activity.Current?.TraceId
            ? NTBlazorErrorBoundaryTelemetry.Source.StartActivity("blazor.client_report", ActivityKind.Internal, page)
                ?? new Activity("blazor.client_report").SetParentId(page.TraceId, page.SpanId, page.TraceFlags).Start()
            : null;
        var fields = CreateFields(report, httpContext);
        const string Template = "Blazor client report {blazor.report.kind} caught by {blazor.boundary.name}: {blazor.exception.type}";
        // Signals without a browser exception become events instead of exceptions.
        var eventId = report.ReportKind switch {
            NTBlazorReportKinds.NavigationStalled => NTBlazorLog.NavigationStalled,
            NTBlazorReportKinds.ReconnectFailed => NTBlazorLog.ReconnectFailed,
            _ => (EventId?)null
        };
        var exporter = httpContext.RequestServices?.GetService<INTBlazorErrorBoundaryExporter>();
        if (eventId is { } signal) {
            NTBlazorLog.WriteEvent(logger, LogLevel.Error, signal, fields, Template);
            NTBlazorLog.Export(logger, exporter, target => target.ExportEvent(signal.Name!, report.OccurredAtUtc, fields));
        }
        else {
            NTBlazorLog.Write(logger, LogLevel.Error, new ClientBlazorErrorReportException(report), fields, Template);
            NTBlazorLog.Export(logger, exporter, target => target.ExportException(report, fields));
        }

        NTBlazorErrorBoundaryTelemetry.RecordClientError(report.ReportKind);
    }

    private static Dictionary<string, object?> CreateFields(NTBlazorErrorReport report, HttpContext httpContext) {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["blazor.boundary.name"] = report.BoundaryName,
            ["blazor.app.version"] = report.ApplicationVersion,
            ["blazor.client.online"] = report.IsOnline,
            ["blazor.error.fingerprint"] = NTBlazorErrorFingerprint.Compute(report),
            ["blazor.error.occurred_at"] = report.OccurredAtUtc,
            ["blazor.error.reference_id"] = report.ReferenceId,
            ["blazor.exception.details"] = report.ExceptionDetails,
            ["blazor.exception.message"] = report.ExceptionMessage,
            ["blazor.exception.stacktrace"] = report.ExceptionStackTrace,
            ["blazor.exception.type"] = report.ExceptionType,
            ["blazor.interactive"] = report.IsInteractive,
            ["blazor.navigation.breadcrumbs"] = JsonSerializer.Serialize(report.Breadcrumbs),
            ["blazor.navigation.id"] = report.NavigationId,
            ["blazor.navigation.intercepted"] = report.IsNavigationIntercepted,
            ["blazor.navigation.origin_url"] = report.OriginUri,
            ["blazor.navigation.phase"] = report.NavigationPhase,
            ["blazor.navigation.target_url"] = report.TargetUri,
            ["blazor.page.traceparent"] = report.TraceParent,
            ["blazor.render_mode"] = report.RenderMode,
            ["blazor.report.kind"] = report.ReportKind,
            ["code.column.number"] = report.JavaScriptColumn,
            ["code.file.path"] = report.JavaScriptSource,
            ["code.line.number"] = report.JavaScriptLine,
            ["url.full"] = report.Uri,
            ["user_agent.original"] = httpContext.Request.Headers.UserAgent.ToString(),
            // OpenTelemetry's session.id and user.id are not mapped by the Azure Monitor exporter, which reads the microsoft.* and enduser.* names instead, so both are written.
            ["session.id"] = report.ClientSessionId,
            ["microsoft.session.id"] = report.ClientSessionId
        };

        // A blank NameIdentifier must not hide a usable sub claim.
        var userId = new[] { ClaimTypes.NameIdentifier, "sub" }.Select(httpContext.User.FindFirstValue).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (userId is not null) {
            fields["user.id"] = userId;
            fields["enduser.id"] = userId;
        }

        return fields;
    }
}
