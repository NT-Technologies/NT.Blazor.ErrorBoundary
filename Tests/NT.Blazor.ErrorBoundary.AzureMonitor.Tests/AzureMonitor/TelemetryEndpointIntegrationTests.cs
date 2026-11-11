using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using OpenTelemetry.Logs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

public sealed class TelemetryEndpointIntegrationTests {
    private const string ConnectionString = "InstrumentationKey=ikey;IngestionEndpoint=https://ingest.test/";

    [Fact]
    public async Task BrowserException_IsSentAsNativeExceptionAndKeptOutOfOpenTelemetryLogs() {
        var host = await PostAsync(NTBlazorReportKinds.Exception);

        var envelope = JsonDocument.Parse(Assert.Single(host.Handler.Requests).Body).RootElement;
        var data = envelope.GetProperty("data").GetProperty("baseData");
        Assert.Equal("System.InvalidOperationException at App.Orders.Load", data.GetProperty("problemId").GetString());
        Assert.Equal("System.InvalidOperationException", data.GetProperty("exceptions")[0].GetProperty("typeName").GetString());
        Assert.Matches("^[0-9a-f]{16}$", data.GetProperty("properties").GetProperty("blazor.error.fingerprint").GetString());
        Assert.Equal("session-1", envelope.GetProperty("tags").GetProperty("ai.session.id").GetString());
        Assert.Equal("1234567890abcdef1234567890abcdef", envelope.GetProperty("tags").GetProperty("ai.operation.id").GetString());

        // Other logging providers still get the full log; the OpenTelemetry pipeline does not, so Application Insights records the exception once.
        var log = Assert.Single(host.Logs.Entries, entry => entry.Category == "NT.Blazor.ErrorBoundary.ClientReport");
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.NotNull(log.Exception);
        Assert.DoesNotContain(host.OpenTelemetryLogs, record => record.CategoryName == "NT.Blazor.ErrorBoundary.ClientReport");
        Assert.Contains(host.OpenTelemetryLogs, record => record.CategoryName?.StartsWith("Microsoft.", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(NTBlazorReportKinds.NavigationStalled, "blazor.navigation.stalled")]
    [InlineData(NTBlazorReportKinds.ReconnectFailed, "blazor.reconnect.failed")]
    public async Task EventReport_IsSentAsCustomEvent(string reportKind, string eventName) {
        var host = await PostAsync(reportKind);

        var envelope = JsonDocument.Parse(Assert.Single(host.Handler.Requests).Body).RootElement;
        Assert.Equal("Event", envelope.GetProperty("name").GetString());
        Assert.Equal(eventName, envelope.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString());
        Assert.DoesNotContain(host.OpenTelemetryLogs, record => record.CategoryName == "NT.Blazor.ErrorBoundary.ClientReport");
    }

    [Fact]
    public async Task ServerError_IsSentAndKeptOutOfOpenTelemetryLogs() {
        var handler = new RecordingHandler();
        var openTelemetryLogs = new List<LogRecord>();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = ConnectionString }).Build());
        services.AddLogging(logging => logging.AddOpenTelemetry(options => options.AddInMemoryExporter(openTelemetryLogs)));
        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        services.AddNTBlazorErrorBoundaryAzureMonitor();
        services.AddHttpClient(NTBlazorAzureMonitorSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);

        using (var scope = provider.CreateScope()) {
            var exception = Assert.Throws<InvalidOperationException>((Action)(() => throw new InvalidOperationException("server boom")));
            await scope.ServiceProvider.GetRequiredService<INTBlazorErrorReporter>().ReportAsync(exception, new NTBlazorErrorBoundaryContext { BoundaryName = "ServerBoundary" }, TestContext.Current.CancellationToken);
        }

        await exporter.StopAsync(TestContext.Current.CancellationToken);

        var serverException = JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement.GetProperty("data").GetProperty("baseData");
        Assert.StartsWith("System.InvalidOperationException at NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.TelemetryEndpointIntegrationTests", serverException.GetProperty("problemId").GetString(), StringComparison.Ordinal);
        Assert.Contains("server boom", serverException.GetProperty("properties").GetProperty("blazor.exception.details").GetString(), StringComparison.Ordinal);
        // A control entry under another category proves the OpenTelemetry pipeline was capturing, so the absence check is not vacuous.
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("Control").LogError("control");
        Assert.Contains(openTelemetryLogs, record => record.CategoryName == "Control");
        Assert.DoesNotContain(openTelemetryLogs, record => record.CategoryName?.StartsWith("NT.Blazor.ErrorBoundary.AspNetCore", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CircuitEvent_IsKeptOutOfOpenTelemetryLogsButReachesOtherProviders() {
        var logs = new CapturingLoggerProvider();
        var openTelemetryLogs = new List<LogRecord>();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = ConnectionString }).Build());
        services.AddLogging(logging => {
            logging.AddProvider(logs);
            logging.AddOpenTelemetry(options => options.AddInMemoryExporter(openTelemetryLogs));
        });
        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        services.AddNTBlazorErrorBoundaryAzureMonitor();
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope()) {
            // Circuit cannot be constructed, so the test calls the handler's internal seam.
            scope.ServiceProvider.GetServices<CircuitHandler>().OfType<NTBlazorCircuitDiagnosticsHandler>().Single().OnConnectionChanged("circuit-1", isUp: false);
        }

        // A control entry under another category proves the OpenTelemetry pipeline was capturing, so the absence check is not vacuous.
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("Control").LogError("control");
        Assert.Contains(openTelemetryLogs, record => record.CategoryName == "Control");
        Assert.DoesNotContain(openTelemetryLogs, record => record.CategoryName?.StartsWith("NT.Blazor.ErrorBoundary.AspNetCore", StringComparison.Ordinal) == true);
        var circuitLog = Assert.Single(logs.Entries, entry => entry.Category.StartsWith("NT.Blazor.ErrorBoundary.AspNetCore", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, circuitLog.Level);
        Assert.Equal("blazor.circuit.connection_down", circuitLog.EventId.Name);
    }

    private static async Task<IntegrationHost> PostAsync(string reportKind) {
        var handler = new RecordingHandler();
        var logs = new CapturingLoggerProvider();
        var openTelemetryLogs = new List<LogRecord>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.AddOpenTelemetry(options => options.AddInMemoryExporter(openTelemetryLogs));
        builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        builder.Services.AddNTBlazorErrorBoundaryAzureMonitor(options => options.ConnectionString = ConnectionString);
        builder.Services.AddHttpClient(NTBlazorAzureMonitorSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var app = builder.Build();
        app.MapNTBlazorErrorBoundaryTelemetry();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using (var client = app.GetTestClient()) {
            using var response = await client.PostAsJsonAsync("/api/errors", new NTBlazorErrorReport {
                ClientSessionId = "session-1",
                ExceptionMessage = "boom",
                ExceptionStackTrace = "   at App.Orders.Load() in /src/Orders.cs:line 12",
                ExceptionType = "System.InvalidOperationException",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                ReportKind = reportKind,
                TraceParent = "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01"
            }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // Stopping drains the exporter's queue.
        await app.StopAsync(TestContext.Current.CancellationToken);
        return new IntegrationHost(handler, logs, openTelemetryLogs);
    }

    private sealed record IntegrationHost(RecordingHandler Handler, CapturingLoggerProvider Logs, List<LogRecord> OpenTelemetryLogs);
}
