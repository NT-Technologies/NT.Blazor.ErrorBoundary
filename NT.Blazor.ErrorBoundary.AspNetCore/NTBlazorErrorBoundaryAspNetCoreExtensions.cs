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
    /// The name of the meter that publishes the <c>nt.blazor.error_boundary.errors</c> counter; pass it to <c>AddMeter</c>.
    /// </summary>
    public const string MeterName = "NT.Blazor.ErrorBoundary";

    /// <summary>
    /// Adds a server-side logger for errors caught by <see cref="NT.Blazor.ErrorBoundary.Components.NTErrorBoundary"/>.
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

        return endpoints.MapPost(options.ReportUri, (NTBlazorErrorReport report, ILoggerFactory loggerFactory, HttpContext httpContext) => {
            var logger = loggerFactory.CreateLogger("NT.Blazor.ErrorBoundary.ClientReport");
            return HandleClientReport(logger, report, httpContext, options);
        })
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

        var processed = report.WithTruncatedFields(options.MaxReportFieldLength);
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

    internal static void LogClientReport(ILogger logger, NTBlazorErrorReport report, HttpContext httpContext) {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(httpContext);

        using (logger.BeginScope(CreateScope(report, httpContext))) {
            logger.LogError(
                new ClientBlazorErrorReportException(report),
                "Blazor client report {ReportKind} caught by {BoundaryName}: {ExceptionType}",
                report.ReportKind,
                report.BoundaryName,
                report.ExceptionType);
        }

        NTBlazorErrorBoundaryMetrics.RecordClientError(report.ReportKind);
    }

    private static Dictionary<string, object?> CreateScope(NTBlazorErrorReport report, HttpContext httpContext) {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["ApplicationVersion"] = report.ApplicationVersion,
            ["BlazorBoundaryName"] = report.BoundaryName,
            ["BlazorErrorReferenceId"] = report.ReferenceId,
            ["BlazorBreadcrumbs"] = JsonSerializer.Serialize(report.Breadcrumbs),
            ["BlazorClientSessionId"] = report.ClientSessionId,
            ["BlazorExceptionDetails"] = report.ExceptionDetails,
            ["BlazorExceptionMessage"] = report.ExceptionMessage,
            ["BlazorExceptionStackTrace"] = report.ExceptionStackTrace,
            ["BlazorExceptionType"] = report.ExceptionType,
            ["BlazorIsNavigationIntercepted"] = report.IsNavigationIntercepted,
            ["BlazorIsOnline"] = report.IsOnline,
            ["BlazorIsInteractive"] = report.IsInteractive,
            ["BlazorJavaScriptColumn"] = report.JavaScriptColumn,
            ["BlazorJavaScriptLine"] = report.JavaScriptLine,
            ["BlazorJavaScriptSource"] = report.JavaScriptSource,
            ["BlazorNavigationId"] = report.NavigationId,
            ["BlazorNavigationPhase"] = report.NavigationPhase,
            ["BlazorOccurredAtUtc"] = report.OccurredAtUtc,
            ["BlazorOriginUri"] = report.OriginUri,
            ["BlazorPageTraceParent"] = report.TraceParent,
            ["BlazorRenderMode"] = report.RenderMode,
            ["BlazorReportKind"] = report.ReportKind,
            ["BlazorTargetUri"] = report.TargetUri,
            ["BlazorUri"] = report.Uri,
            ["RequestId"] = httpContext.TraceIdentifier,
            ["RequestMethod"] = httpContext.Request.Method,
            ["RequestPath"] = httpContext.Request.Path.Value,
            ["UserAgent"] = httpContext.Request.Headers.UserAgent.ToString()
        };

        if (Activity.Current is { } activity) {
            scope["TraceId"] = activity.TraceId.ToString();
            scope["SpanId"] = activity.SpanId.ToString();
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(userId)) {
            scope["UserId"] = userId;
        }

        return scope;
    }
}
