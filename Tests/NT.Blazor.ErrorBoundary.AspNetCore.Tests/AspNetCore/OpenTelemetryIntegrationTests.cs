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
    public async Task ServerException_ExportsOriginalExceptionFieldsAndTraceCorrelation() {
        var logs = new List<LogRecord>();
        var spans = new List<Activity>();
        using var source = new ActivitySource("ErrorBoundary.Tests.Server");
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .AddInMemoryExporter(spans)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddOpenTelemetry(options => {
            options.IncludeFormattedMessage = true;
            options.AddInMemoryExporter(logs);
        }));
        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
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
        var fields = ReadAttributes(log);
        Assert.Equal("OrderPage", fields["blazor.boundary.name"]);
        Assert.Equal(true, fields["blazor.interactive"]);
        Assert.Equal("Server", fields["blazor.render_mode"]);
        Assert.Equal("https://example.test/orders", fields["blazor.navigation.origin_url"]);
        Assert.Equal("https://example.test/orders/42", fields["url.full"]);
    }

    [Fact]
    public async Task ClientReportEndpoint_ExportsDiagnosticFieldsAndUploadTraceCorrelation() {
        var logs = new List<LogRecord>();
        var exporter = new TestActivityExporter(ActivityTraceId.CreateFromString("1234567890abcdef1234567890abcdef"));
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAspNetCoreInstrumentation()
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("NT.Blazor.ErrorBoundary.ClientReport", LogLevel.Error);
        builder.Logging.AddOpenTelemetry(options => {
            options.IncludeFormattedMessage = true;
            options.AddInMemoryExporter(logs);
        });
        builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        await using var app = builder.Build();
        app.MapNTBlazorErrorBoundaryTelemetry();
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
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Uri = "https://example.test/orders/42"
        };

        using var response = await client.PostAsJsonAsync("/api/errors", report, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // TestServer can return the response before request teardown ends and exports the activity.
        var span = await exporter.ExportedActivity.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        var log = Assert.Single(logs, entry => entry.CategoryName == "NT.Blazor.ErrorBoundary.ClientReport");
        Assert.Equal(LogLevel.Error, log.LogLevel);
        Assert.NotNull(log.Exception);
        Assert.Equal("ClientBlazorErrorReportException", log.Exception.GetType().Name);
        Assert.Equal(report.ExceptionMessage, log.Exception.Message);
        Assert.Equal(report.ExceptionStackTrace, log.Exception.StackTrace);
        Assert.Equal(report.ExceptionDetails, log.Exception.ToString());
        Assert.Contains("OrderPage", log.FormattedMessage, StringComparison.Ordinal);
        Assert.Equal("1234567890abcdef1234567890abcdef", log.TraceId.ToString());
        Assert.Equal(log.TraceId, span.TraceId);
        Assert.Equal(log.SpanId, span.SpanId);
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal("1234567890abcdef", span.ParentSpanId.ToString());
        var fields = ReadAttributes(log);
        Assert.Equal(report.ExceptionType, fields["blazor.exception.type"]);
        Assert.Equal(report.ExceptionMessage, fields["blazor.exception.message"]);
        Assert.Equal(report.ExceptionStackTrace, fields["blazor.exception.stacktrace"]);
        Assert.Equal(report.ExceptionDetails, fields["blazor.exception.details"]);
        Assert.Equal(report.BoundaryName, fields["blazor.boundary.name"]);
        Assert.Equal(report.RenderMode, fields["blazor.render_mode"]);
        Assert.Equal(report.ApplicationVersion, fields["blazor.app.version"]);
        Assert.Equal(report.ClientSessionId, fields["microsoft.session.id"]);
        Assert.Matches("^[0-9a-f]{16}$", Assert.IsType<string>(fields["blazor.error.fingerprint"]));
        Assert.Equal(report.NavigationId, fields["blazor.navigation.id"]);
        Assert.Equal(report.OccurredAtUtc, fields["blazor.error.occurred_at"]);
    }

    // Scopes stay disabled in these tests: the fields must arrive as attributes because exporters such as Azure Monitor omit scopes by default.
    private static Dictionary<string, object?> ReadAttributes(LogRecord log) => log.Attributes!.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
}
