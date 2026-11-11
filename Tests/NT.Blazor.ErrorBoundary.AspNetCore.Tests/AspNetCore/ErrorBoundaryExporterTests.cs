using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics.Metrics;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class ErrorBoundaryExporterTests {
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClientException_IsLoggedWithExceptionAndExported() {
        var exporter = new RecordingExporter();
        var logs = new TestLoggerProvider();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), Report(NTBlazorReportKinds.Exception), HttpContextWith(exporter));

        Assert.IsType<ClientBlazorErrorReportException>(Assert.Single(logs.Entries).Exception);
        var (report, attributes) = Assert.Single(exporter.Exceptions);
        Assert.Equal("boom", report.ExceptionMessage);
        Assert.Equal("session-1", attributes["session.id"]);
        Assert.Matches("^[0-9a-f]{16}$", Assert.IsType<string>(attributes["blazor.error.fingerprint"]));
        Assert.Empty(exporter.Events);
    }

    [Fact]
    public void ClientEvent_IsLoggedAsEventAndExportedWithBrowserTime() {
        var exporter = new RecordingExporter();
        var logs = new TestLoggerProvider();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), Report(NTBlazorReportKinds.NavigationStalled), HttpContextWith(exporter));

        Assert.Equal("blazor.navigation.stalled", Assert.Single(logs.Entries).EventId.Name);
        var (name, occurredAt, attributes) = Assert.Single(exporter.Events);
        Assert.Equal("blazor.navigation.stalled", name);
        Assert.Equal(OccurredAt, occurredAt);
        Assert.Equal("session-1", attributes["session.id"]);
        Assert.Empty(exporter.Exceptions);
    }

    [Fact]
    public async Task ServerException_IsLoggedAndExported() {
        var exporter = new RecordingExporter();
        var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var circuit = new NTBlazorCircuitContext { CircuitId = "circuit-1" };
        var reporter = new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>(), Microsoft.Extensions.Options.Options.Create(new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }), circuit, exporter);
        var exception = Assert.Throws<InvalidOperationException>((Action)(() => throw new InvalidOperationException("server boom")));

        await reporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext { BoundaryName = "ServerBoundary" }, TestContext.Current.CancellationToken);

        Assert.Same(exception, Assert.Single(logs.Entries).Exception);
        var (report, attributes) = Assert.Single(exporter.Exceptions);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.ExceptionType);
        Assert.Contains(nameof(ServerException_IsLoggedAndExported), report.ExceptionStackTrace, StringComparison.Ordinal);
        Assert.Equal("ServerBoundary", attributes["blazor.boundary.name"]);
        Assert.Equal("circuit-1", attributes["aspnetcore.components.circuit.id"]);
    }

    [Fact]
    public void CircuitEvent_IsLoggedAndExported() {
        var exporter = new RecordingExporter();
        var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var handler = new NTBlazorCircuitDiagnosticsHandler(loggerFactory.CreateLogger<NTBlazorCircuitDiagnosticsHandler>(), new NTBlazorCircuitContext(), exporter);

        handler.OnConnectionChanged("circuit-1", isUp: false);

        Assert.Single(logs.Entries);
        var (name, _, attributes) = Assert.Single(exporter.Events);
        Assert.Equal("blazor.circuit.connection_down", name);
        Assert.Equal("circuit-1", attributes["aspnetcore.components.circuit.id"]);
    }

    [Fact]
    public void ServerLogging_ResolvesReporterAndCircuitHandlerWithRegisteredExporter() {
        var exporter = new RecordingExporter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        services.AddSingleton<INTBlazorErrorBoundaryExporter>(exporter);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>().OfType<NTBlazorCircuitDiagnosticsHandler>().Single()
            .OnConnectionChanged("circuit-1", isUp: true);

        Assert.Equal("blazor.circuit.connection_up", Assert.Single(exporter.Events).Name);
        Assert.IsType<NTBlazorServerErrorReporter>(scope.ServiceProvider.GetRequiredService<INTBlazorErrorReporter>());
    }

    [Theory]
    [InlineData(NTBlazorReportKinds.Exception)]
    [InlineData(NTBlazorReportKinds.NavigationStalled)]
    public void ClientReportWithThrowingExporter_LogsOnceRecordsMetricAndReturnsNoContent(string reportKind) {
        using var recorder = new CounterRecorder();
        var exporter = new ThrowingExporter();
        var logs = new TestLoggerProvider();
        var httpContext = new DefaultHttpContext {
            RequestServices = new ServiceCollection().AddSingleton<INTBlazorErrorBoundaryExporter>(exporter).AddSingleton<TimeProvider>(new FixedTimeProvider(OccurredAt)).BuildServiceProvider()
        };

        var result = NTBlazorErrorBoundaryAspNetCoreExtensions.HandleClientReport(logs.CreateLogger("test"), Report(reportKind), httpContext, new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" });

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(1, exporter.Calls);
        Assert.Single(logs.Entries, entry => entry.LogLevel == LogLevel.Error);
        Assert.Same(exporter.Thrown, Assert.Single(logs.Entries, entry => entry.LogLevel == LogLevel.Warning).Exception);
        var measurement = Assert.Single(recorder.Measurements);
        Assert.Equal("client", measurement.Tags["blazor.error.source"]);
        Assert.Equal(reportKind, measurement.Tags["blazor.report.kind"]);
    }

    [Fact]
    public async Task ServerErrorWithThrowingExporter_DoesNotThrowAndRecordsMetric() {
        using var recorder = new CounterRecorder();
        var exporter = new ThrowingExporter();
        var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var reporter = new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>(), Microsoft.Extensions.Options.Options.Create(new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }), new NTBlazorCircuitContext(), exporter);
        var exception = new InvalidOperationException("server boom");

        await reporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext { BoundaryName = "ServerBoundary" }, TestContext.Current.CancellationToken);

        Assert.Equal(1, exporter.Calls);
        Assert.Same(exception, Assert.Single(logs.Entries, entry => entry.LogLevel == LogLevel.Error).Exception);
        Assert.Same(exporter.Thrown, Assert.Single(logs.Entries, entry => entry.LogLevel == LogLevel.Warning).Exception);
        var measurement = Assert.Single(recorder.Measurements);
        Assert.Equal("server", measurement.Tags["blazor.error.source"]);
        Assert.Equal("ServerBoundary", measurement.Tags["blazor.boundary.name"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CircuitEventWithThrowingExporter_DoesNotThrowAndStillLogs(bool isUp) {
        var exporter = new ThrowingExporter();
        var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var circuit = new NTBlazorCircuitContext();
        var handler = new NTBlazorCircuitDiagnosticsHandler(loggerFactory.CreateLogger<NTBlazorCircuitDiagnosticsHandler>(), circuit, exporter);

        handler.OnConnectionChanged("circuit-1", isUp);

        Assert.Equal(1, exporter.Calls);
        Assert.Equal("circuit-1", circuit.CircuitId);
        Assert.Collection(
            logs.Entries,
            logged => Assert.Equal(isUp ? "blazor.circuit.connection_up" : "blazor.circuit.connection_down", logged.EventId.Name),
            warning => {
                Assert.Equal(LogLevel.Warning, warning.LogLevel);
                Assert.Same(exporter.Thrown, warning.Exception);
            });
    }

    private static DefaultHttpContext HttpContextWith(INTBlazorErrorBoundaryExporter exporter) => new() {
        RequestServices = new ServiceCollection().AddSingleton(exporter).BuildServiceProvider()
    };

    private static NTBlazorErrorReport Report(string reportKind) => new() {
        ClientSessionId = "session-1",
        ExceptionMessage = "boom",
        ExceptionType = "System.InvalidOperationException",
        OccurredAtUtc = OccurredAt,
        ReportKind = reportKind
    };

    private sealed class RecordingExporter : INTBlazorErrorBoundaryExporter {
        public List<(NTBlazorErrorReport Report, IReadOnlyDictionary<string, object?> Attributes)> Exceptions { get; } = [];

        public List<(string Name, DateTimeOffset OccurredAt, IReadOnlyDictionary<string, object?> Attributes)> Events { get; } = [];

        public void ExportException(NTBlazorErrorReport report, IReadOnlyDictionary<string, object?> attributes) => Exceptions.Add((report, attributes));

        public void ExportEvent(string name, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> attributes) => Events.Add((name, occurredAt, attributes));
    }

    private sealed class ThrowingExporter : INTBlazorErrorBoundaryExporter {
        public InvalidOperationException Thrown { get; } = new("The exporter failed.");

        public int Calls { get; private set; }

        public void ExportException(NTBlazorErrorReport report, IReadOnlyDictionary<string, object?> attributes) {
            Calls++;
            throw Thrown;
        }

        public void ExportEvent(string name, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> attributes) {
            Calls++;
            throw Thrown;
        }
    }

    // As in MetricsAndCircuitTests: the meter is static and shared by parallel tests, so only measurements recorded on the creating thread are kept.
    // Every code path under test records synchronously on the calling thread.
    private sealed class CounterRecorder : IDisposable {
        private readonly MeterListener _listener = new();
        private readonly int _threadId = Environment.CurrentManagedThreadId;

        public List<(long Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];

        public CounterRecorder() {
            _listener.InstrumentPublished = (instrument, listener) => {
                if (instrument.Meter.Name == NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName && instrument.Name == NTBlazorErrorBoundaryTelemetry.ErrorsCounterName) {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => {
                if (Environment.CurrentManagedThreadId == _threadId) {
                    Measurements.Add((value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
                }
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }
}
