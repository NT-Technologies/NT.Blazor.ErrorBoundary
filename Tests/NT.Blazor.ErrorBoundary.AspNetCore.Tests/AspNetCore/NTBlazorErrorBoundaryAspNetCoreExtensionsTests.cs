using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics;
using System.Security.Claims;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class NTBlazorErrorBoundaryAspNetCoreExtensionsTests {
    [Fact]
    public void AddBlazorErrorBoundaryServerLogging_ConfiguresReportUri() {
        var services = new ServiceCollection();

        services.AddBlazorErrorBoundaryServerLogging("/api/errors");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NTBlazorErrorBoundaryHttpClientOptions>>();

        Assert.Equal("/api/errors", options.Value.ReportUri);
    }

    [Fact]
    public void MapBlazorErrorBoundaryTelemetry_UsesConfiguredReportUri() {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
            options.ReportUri = "/api/errors";
        });
        using var app = builder.Build();

        app.MapBlazorErrorBoundaryTelemetry();

        var routeEndpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>());
        Assert.Equal("/api/errors", routeEndpoint.RoutePattern.RawText);
    }

    [Fact]
    public void LogClientReport_LogsStructuredClientErrorFields() {
        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger("test");
        var httpContext = new DefaultHttpContext {
            TraceIdentifier = "request-123",
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "user-123")
            ], "Test"))
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/v1/Telemetry/BlazorError";
        httpContext.Request.Headers.UserAgent = "Telemetry Test Browser";
        var report = new NTBlazorErrorReport {
            ApplicationVersion = "4.13.0",
            BoundaryName = "Boundary",
            Breadcrumbs = [
                new NTBlazorBreadcrumb {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    Phase = "LocationChanging",
                    TargetUri = "https://example.test/claims/recovery/64469",
                    Uri = "https://example.test/claims/manage/64469"
                }
            ],
            ClientSessionId = "session-123",
            ExceptionDetails = "complete exception",
            ExceptionMessage = "client failure",
            ExceptionStackTrace = "stack",
            ExceptionType = typeof(InvalidOperationException).FullName,
            IsInteractive = true,
            IsNavigationIntercepted = true,
            IsOnline = true,
            NavigationId = "navigation-123",
            NavigationPhase = "LocationChanging",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            OriginUri = "https://example.test/claims/manage/64469",
            RenderMode = "WebAssembly",
            TargetUri = "https://example.test/claims/recovery/64469",
            Uri = "https://example.test/claims"
        };

        using var activity = new Activity("client-report-test").Start();
        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logger, report, httpContext);

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.NotNull(entry.Exception);
        Assert.Contains("Blazor client report Exception", entry.Message, StringComparison.Ordinal);
        Assert.Equal("Boundary", entry.ScopeValues["BlazorBoundaryName"]);
        Assert.Equal("4.13.0", entry.ScopeValues["ApplicationVersion"]);
        Assert.Contains("LocationChanging", Assert.IsType<string>(entry.ScopeValues["BlazorBreadcrumbs"]), StringComparison.Ordinal);
        Assert.Equal("session-123", entry.ScopeValues["BlazorClientSessionId"]);
        Assert.Equal("complete exception", entry.ScopeValues["BlazorExceptionDetails"]);
        Assert.Equal("client failure", entry.ScopeValues["BlazorExceptionMessage"]);
        Assert.Equal("stack", entry.ScopeValues["BlazorExceptionStackTrace"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ScopeValues["BlazorExceptionType"]);
        Assert.Equal(true, entry.ScopeValues["BlazorIsInteractive"]);
        Assert.Equal(true, entry.ScopeValues["BlazorIsNavigationIntercepted"]);
        Assert.Equal(true, entry.ScopeValues["BlazorIsOnline"]);
        Assert.Equal("navigation-123", entry.ScopeValues["BlazorNavigationId"]);
        Assert.Equal("LocationChanging", entry.ScopeValues["BlazorNavigationPhase"]);
        Assert.Equal("https://example.test/claims/manage/64469", entry.ScopeValues["BlazorOriginUri"]);
        Assert.Equal("WebAssembly", entry.ScopeValues["BlazorRenderMode"]);
        Assert.Equal(NTBlazorReportKinds.Exception, entry.ScopeValues["BlazorReportKind"]);
        Assert.Equal("https://example.test/claims/recovery/64469", entry.ScopeValues["BlazorTargetUri"]);
        Assert.Equal("https://example.test/claims", entry.ScopeValues["BlazorUri"]);
        Assert.Equal("request-123", entry.ScopeValues["RequestId"]);
        Assert.Equal(HttpMethods.Post, entry.ScopeValues["RequestMethod"]);
        Assert.Equal("/api/v1/Telemetry/BlazorError", entry.ScopeValues["RequestPath"]);
        Assert.Equal("user-123", entry.ScopeValues["UserId"]);
        Assert.Equal("Telemetry Test Browser", entry.ScopeValues["UserAgent"]);
        Assert.Equal(activity.TraceId.ToString(), entry.ScopeValues["TraceId"]);
        Assert.Equal(activity.SpanId.ToString(), entry.ScopeValues["SpanId"]);
    }

    [Fact]
    public void LogClientReport_WhenReportFieldsExceedMaximum_TruncatesStructuredFields() {
        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger("test");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/errors";
        var report = new NTBlazorErrorReport {
            BoundaryName = new string('b', 200),
            ExceptionMessage = new string('m', 200),
            ExceptionStackTrace = new string('s', 200),
            ExceptionType = typeof(InvalidOperationException).FullName,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Uri = $"https://example.test/{new string('u', 200)}"
        };

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logger, report, httpContext, 64);

        var entry = Assert.Single(loggerProvider.Entries);
        var message = Assert.IsType<string>(entry.ScopeValues["BlazorExceptionMessage"]);
        var stackTrace = Assert.IsType<string>(entry.ScopeValues["BlazorExceptionStackTrace"]);
        var uri = Assert.IsType<string>(entry.ScopeValues["BlazorUri"]);
        Assert.True(message.Length <= 64);
        Assert.True(stackTrace.Length <= 64);
        Assert.True(uri.Length <= 64);
        Assert.Contains("report field truncated", message, StringComparison.Ordinal);
    }
}
