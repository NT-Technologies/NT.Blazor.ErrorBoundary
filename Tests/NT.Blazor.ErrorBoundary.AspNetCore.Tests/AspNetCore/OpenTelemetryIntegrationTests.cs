using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class OpenTelemetryIntegrationTests {
    [Fact]
    public async Task ServerException_ExportsOriginalExceptionScopesAndTraceCorrelation() {
        var logs = new List<LogRecord>();
        var spans = new List<Activity>();
        using var source = new ActivitySource("ErrorBoundary.Tests.Server");
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .AddInMemoryExporter(spans)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddOpenTelemetry(options => {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.AddInMemoryExporter(logs);
        }));
        services.AddBlazorErrorBoundaryServerLogging("/api/errors");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reporter = scope.ServiceProvider.GetRequiredService<INTBlazorErrorReporter>();
        var exception = Assert.Throws<InvalidOperationException>((Action)(() => throw new InvalidOperationException("Server render failed")));

        using (var activity = source.StartActivity("Render component")) {
            Assert.NotNull(activity);
            await reporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext {
                BoundaryName = "OrderPage",
                IsInteractive = true,
                RenderMode = "Server",
                OriginUri = "https://example.test/orders",
                Uri = "https://example.test/orders/42"
            }, TestContext.Current.CancellationToken);
        }

        var log = Assert.Single(logs);
        var span = Assert.Single(spans);
        Assert.Equal(LogLevel.Error, log.LogLevel);
        Assert.Same(exception, log.Exception);
        Assert.NotNull(log.Exception);
        Assert.NotEmpty(log.Exception.StackTrace!);
        Assert.Contains("OrderPage", log.FormattedMessage, StringComparison.Ordinal);
        Assert.Equal(span.TraceId, log.TraceId);
        Assert.Equal(span.SpanId, log.SpanId);
        var fields = ReadScopes(log);
        Assert.Equal("OrderPage", fields["BlazorBoundaryName"]);
        Assert.Equal(true, fields["BlazorIsInteractive"]);
        Assert.Equal("Server", fields["BlazorRenderMode"]);
        Assert.Equal("https://example.test/orders", fields["BlazorOriginUri"]);
        Assert.Equal("https://example.test/orders/42", fields["BlazorUri"]);
    }

    [Fact]
    public async Task ClientReportEndpoint_ExportsDiagnosticFieldsAndUploadTraceCorrelation() {
        var logs = new List<LogRecord>();
        var spans = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAspNetCoreInstrumentation()
            .AddInMemoryExporter(spans)
            .Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("NT.Blazor.ErrorBoundary.ClientReport", LogLevel.Error);
        builder.Logging.AddOpenTelemetry(options => {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.AddInMemoryExporter(logs);
        });
        builder.Services.AddBlazorErrorBoundaryServerLogging("/api/errors");
        await using var app = builder.Build();
        app.MapBlazorErrorBoundaryTelemetry();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("traceparent", "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01");
        var report = new NTBlazorErrorReport {
            BoundaryName = "OrderPage",
            RenderMode = "WebAssembly",
            ExceptionType = "System.InvalidOperationException",
            ExceptionMessage = "Browser render failed",
            ExceptionStackTrace = "at OrderPage.Render()",
            ExceptionDetails = "System.InvalidOperationException: Browser render failed\n at OrderPage.Render()",
            ApplicationVersion = "2.0.0",
            ClientSessionId = "session-123",
            NavigationId = "navigation-456",
            OccurredAtUtc = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            Uri = "https://example.test/orders/42"
        };

        using var response = await client.PostAsJsonAsync("/api/errors", report, TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var log = Assert.Single(logs, entry => entry.CategoryName == "NT.Blazor.ErrorBoundary.ClientReport");
        Assert.Equal(LogLevel.Error, log.LogLevel);
        Assert.NotNull(log.Exception);
        Assert.Equal("ClientBlazorErrorReportException", log.Exception.GetType().Name);
        Assert.Contains(report.ExceptionMessage, log.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("OrderPage", log.FormattedMessage, StringComparison.Ordinal);
        Assert.Equal("1234567890abcdef1234567890abcdef", log.TraceId.ToString());
        var span = Assert.Single(spans, entry => entry.TraceId == log.TraceId && entry.SpanId == log.SpanId);
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal("1234567890abcdef", span.ParentSpanId.ToString());
        var fields = ReadScopes(log);
        Assert.Equal(report.ExceptionType, fields["BlazorExceptionType"]);
        Assert.Equal(report.ExceptionMessage, fields["BlazorExceptionMessage"]);
        Assert.Equal(report.ExceptionStackTrace, fields["BlazorExceptionStackTrace"]);
        Assert.Equal(report.ExceptionDetails, fields["BlazorExceptionDetails"]);
        Assert.Equal(report.BoundaryName, fields["BlazorBoundaryName"]);
        Assert.Equal(report.RenderMode, fields["BlazorRenderMode"]);
        Assert.Equal(report.ApplicationVersion, fields["ApplicationVersion"]);
        Assert.Equal(report.ClientSessionId, fields["BlazorClientSessionId"]);
        Assert.Equal(report.NavigationId, fields["BlazorNavigationId"]);
        Assert.Equal(report.OccurredAtUtc, fields["BlazorOccurredAtUtc"]);
        Assert.Equal("/api/errors", fields["RequestPath"]);
        Assert.Equal(log.TraceId.ToString(), fields["TraceId"]);
        Assert.Equal(log.SpanId.ToString(), fields["SpanId"]);
    }

    private static Dictionary<string, object?> ReadScopes(LogRecord log) {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        log.ForEachScope((scope, state) => {
            foreach (var field in scope) {
                state[field.Key] = field.Value;
            }
        }, fields);
        return fields;
    }
}
