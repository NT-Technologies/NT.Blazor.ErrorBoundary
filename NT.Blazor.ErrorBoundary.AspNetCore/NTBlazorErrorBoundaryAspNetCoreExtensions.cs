using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Security.Claims;

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
                "Unhandled Blazor client exception caught by {BoundaryName}: {ExceptionType}",
                truncatedReport.BoundaryName,
                truncatedReport.ExceptionType);
        }
    }

    private static Dictionary<string, object?> CreateScope(NTBlazorErrorReport report, HttpContext httpContext) {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["BlazorBoundaryName"] = report.BoundaryName,
            ["BlazorExceptionMessage"] = report.ExceptionMessage,
            ["BlazorExceptionStackTrace"] = report.ExceptionStackTrace,
            ["BlazorExceptionType"] = report.ExceptionType,
            ["BlazorIsInteractive"] = report.IsInteractive,
            ["BlazorOccurredAtUtc"] = report.OccurredAtUtc,
            ["BlazorRenderMode"] = report.RenderMode,
            ["BlazorUri"] = report.Uri,
            ["RequestId"] = httpContext.TraceIdentifier,
            ["RequestMethod"] = httpContext.Request.Method,
            ["RequestPath"] = httpContext.Request.Path.Value
        };

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(userId)) {
            scope["UserId"] = userId;
        }

        return scope;
    }

    private sealed class ClientBlazorErrorReportException(NTBlazorErrorReport report)
        : Exception($"{report.ExceptionType}: {report.ExceptionMessage}");
}
