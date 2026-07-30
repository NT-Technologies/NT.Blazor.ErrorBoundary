using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

/// <summary>
/// Registers ASP.NET Core services and endpoints for Blazor error boundary reporting.
/// </summary>
public static class NTBlazorErrorBoundaryAspNetCoreExtensions {
    /// <summary>
    /// Adds a server-side logger for errors caught by <see cref="Components.NTErrorBoundary"/>.
    /// </summary>
    public static IServiceCollection AddBlazorErrorBoundaryServerLogging(this IServiceCollection services, string reportUri) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportUri);

        services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
            options.ReportUri = reportUri;
        });
        services.AddOptions<NTErrorBoundaryOptions>()
            .Configure<IHostEnvironment>((options, environment) => {
                options.IsDevelopmentEnvironment = environment.IsDevelopment();
            });
        services.AddScoped<INTBlazorErrorReporter, NTBlazorServerErrorReporter>();
        return services;
    }

    /// <summary>
    /// Maps an endpoint that receives client-side Blazor error reports and logs them through <see cref="ILogger"/>.
    /// </summary>
    public static RouteHandlerBuilder MapBlazorErrorBoundaryTelemetry(this IEndpointRouteBuilder endpoints) {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<NTBlazorErrorBoundaryHttpClientOptions>>();
        var httpClientOptions = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(httpClientOptions.ReportUri);
        var reportUri = httpClientOptions.ReportUri;
        var maxReportFieldLength = httpClientOptions.MaxReportFieldLength;

        return endpoints.MapPost(reportUri, (NTBlazorErrorReport report, ILoggerFactory loggerFactory, HttpContext httpContext) => {
            var logger = loggerFactory.CreateLogger("NT.Blazor.ErrorBoundary.ClientReport");
            LogClientReport(logger, report, httpContext, maxReportFieldLength);
            return Results.NoContent();
        })
            .WithName("Report Blazor Error")
            .WithSummary("Reports a client-side Blazor error boundary exception.")
            .Produces(StatusCodes.Status204NoContent);
    }

    internal static void LogClientReport(ILogger logger, NTBlazorErrorReport report, HttpContext httpContext, int maxReportFieldLength = NTBlazorErrorBoundaryHttpClientOptions.DefaultMaxReportFieldLength) {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(httpContext);

        var truncatedReport = report.WithTruncatedFields(maxReportFieldLength);
        using (logger.BeginScope(CreateScope(truncatedReport, httpContext))) {
            var exception = new ClientBlazorErrorReportException(truncatedReport);
            logger.LogError(
                exception,
                "Blazor client report {ReportKind} caught by {BoundaryName}: {ExceptionType}",
                truncatedReport.ReportKind,
                truncatedReport.BoundaryName,
                truncatedReport.ExceptionType);
        }
    }

    private static Dictionary<string, object?> CreateScope(NTBlazorErrorReport report, HttpContext httpContext) {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["ApplicationVersion"] = report.ApplicationVersion,
            ["BlazorBoundaryName"] = report.BoundaryName,
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

    private sealed class ClientBlazorErrorReportException(NTBlazorErrorReport report)
        : Exception($"{report.ReportKind}: {report.ExceptionType}: {report.ExceptionMessage}");
}
