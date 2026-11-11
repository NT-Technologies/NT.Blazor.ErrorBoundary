using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics.Metrics;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class MetricsAndCircuitTests {
    [Fact]
    public async Task ServerReport_IncrementsCounterWithBoundaryTag() {
        using var recorder = new CounterRecorder();
        var (reporter, _) = CreateReporter();

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext { BoundaryName = "Boundary" }, TestContext.Current.CancellationToken);

        var measurement = Assert.Single(recorder.Measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("server", measurement.Tags["blazor.error.source"]);
        Assert.Equal(NTBlazorReportKinds.Exception, measurement.Tags["blazor.report.kind"]);
        Assert.Equal("Boundary", measurement.Tags["blazor.boundary.name"]);
    }

    [Fact]
    public void ClientReport_IncrementsCounterWithoutBoundaryTag() {
        using var recorder = new CounterRecorder();
        var report = new NTBlazorErrorReport { BoundaryName = "attacker-controlled", ReportKind = NTBlazorReportKinds.JavaScriptError, OccurredAtUtc = DateTimeOffset.UtcNow };

        NTBlazorErrorBoundaryAspNetCoreExtensions.HandleClientReport(new TestLoggerProvider().CreateLogger("test"), report, CreateHttpContext(), Options());

        var measurement = Assert.Single(recorder.Measurements);
        Assert.Equal("client", measurement.Tags["blazor.error.source"]);
        Assert.Equal(NTBlazorReportKinds.JavaScriptError, measurement.Tags["blazor.report.kind"]);
        Assert.DoesNotContain("blazor.boundary.name", measurement.Tags.Keys);
    }

    [Fact]
    public void RejectedClientReport_DoesNotIncrementCounter() {
        using var recorder = new CounterRecorder();
        var report = new NTBlazorErrorReport { ReportKind = "bogus", OccurredAtUtc = DateTimeOffset.UtcNow };

        NTBlazorErrorBoundaryAspNetCoreExtensions.HandleClientReport(new TestLoggerProvider().CreateLogger("test"), report, CreateHttpContext(), Options());

        Assert.Empty(recorder.Measurements);
    }

    [Fact]
    public async Task FilteredServerReport_DoesNotIncrementCounterOrLog() {
        using var recorder = new CounterRecorder();
        var (reporter, logs) = CreateReporter(options => options.FilterReport = _ => null);

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Measurements);
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public async Task ServerReport_StripsQueryAndLogsReferenceId() {
        var (reporter, logs) = CreateReporter();

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext { ReferenceId = "ref-9", Uri = "https://example.test/a?token=secret#frag" }, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal("https://example.test/a", entry.Values["url.full"]);
        Assert.Equal("ref-9", entry.Values["blazor.error.reference_id"]);
    }

    [Fact]
    public async Task ServerReport_FilterRedactions_AreLoggedButOriginalExceptionIsKept() {
        var (reporter, logs) = CreateReporter(options => options.FilterReport = report => new NTBlazorErrorReport { BoundaryName = "redacted", ReportKind = report.ReportKind });
        var exception = new InvalidOperationException("x");

        await reporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext { BoundaryName = "secret" }, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries);
        Assert.Same(exception, entry.Exception);
        Assert.Equal("redacted", entry.Values["blazor.boundary.name"]);
    }

    [Fact]
    public async Task ThrowingFilter_DoesNotIncrementCounter() {
        using var recorder = new CounterRecorder();
        var (reporter, _) = CreateReporter(options => options.FilterReport = _ => throw new InvalidOperationException("filter bug"));

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Measurements);
    }

    [Fact]
    public void ConnectionUp_LogsInformationEventWithCircuitId() {
        var (handler, _, logs) = CreateHandler();

        handler.OnConnectionChanged("circuit-1", isUp: true);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Equal("blazor.circuit.connection_up", entry.EventId.Name);
        Assert.Equal("blazor.circuit.connection_up", entry.Values["microsoft.custom_event.name"]);
        Assert.Equal("circuit-1", entry.Values["aspnetcore.components.circuit.id"]);
        Assert.Equal("Blazor circuit circuit-1 connection up", entry.Message);
    }

    [Fact]
    public void ConnectionDown_LogsWarningEventWithCircuitId() {
        var (handler, _, logs) = CreateHandler();

        handler.OnConnectionChanged("circuit-1", isUp: false);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("blazor.circuit.connection_down", entry.EventId.Name);
        Assert.Equal("blazor.circuit.connection_down", entry.Values["microsoft.custom_event.name"]);
        Assert.Equal("circuit-1", entry.Values["aspnetcore.components.circuit.id"]);
        Assert.Equal("Blazor circuit circuit-1 connection down", entry.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectionChange_StoresCircuitIdInContext(bool isUp) {
        var (handler, context, _) = CreateHandler();

        handler.OnConnectionChanged("circuit-1", isUp);

        Assert.Equal("circuit-1", context.CircuitId);
    }

    [Fact]
    public async Task ServerReport_AfterConnectionChange_CarriesCircuitId() {
        var (handler, context, logs) = CreateHandler();
        handler.OnConnectionChanged("circuit-1", isUp: true);
        var reporter = new NTBlazorServerErrorReporter(LoggerFactory.Create(builder => builder.AddProvider(logs)).CreateLogger<NTBlazorServerErrorReporter>(), Microsoft.Extensions.Options.Options.Create(Options()), context);

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Equal("circuit-1", logs.Entries.Single(entry => entry.LogLevel == LogLevel.Error).Values["aspnetcore.components.circuit.id"]);
    }

    [Fact]
    public void ServerLogging_RegistersCircuitHandlerOnceAndSharesContextPerScope() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors").AddNTBlazorErrorBoundaryServerLogging("/api/errors");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<NTBlazorCircuitDiagnosticsHandler>(Assert.Single(scope.ServiceProvider.GetServices<CircuitHandler>()));
        Assert.Same(scope.ServiceProvider.GetRequiredService<NTBlazorCircuitContext>(), scope.ServiceProvider.GetRequiredService<NTBlazorCircuitContext>());
    }

    [Fact]
    public void ObsoleteForwarders_StillRegisterServices() {
        var services = new ServiceCollection();

#pragma warning disable CS0618
        services.AddBlazorErrorBoundaryServerLogging("/api/errors");
#pragma warning restore CS0618

        using var provider = services.BuildServiceProvider();
        Assert.Equal("/api/errors", provider.GetRequiredService<IOptions<NTBlazorErrorBoundaryHttpClientOptions>>().Value.ReportUri);
    }

    private static (NTBlazorCircuitDiagnosticsHandler Handler, NTBlazorCircuitContext Context, TestLoggerProvider Logs) CreateHandler() {
        var logs = new TestLoggerProvider();
        var context = new NTBlazorCircuitContext();
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        return (new NTBlazorCircuitDiagnosticsHandler(loggerFactory.CreateLogger<NTBlazorCircuitDiagnosticsHandler>(), context), context, logs);
    }

    private static NTBlazorErrorBoundaryHttpClientOptions Options() => new() { ReportUri = "/api/errors" };

    private static DefaultHttpContext CreateHttpContext() => new() { RequestServices = new ServiceCollection().BuildServiceProvider() };

    private static (NTBlazorServerErrorReporter Reporter, TestLoggerProvider Logs) CreateReporter(Action<NTBlazorErrorBoundaryHttpClientOptions>? configure = null) {
        var logs = new TestLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        var options = Options();
        configure?.Invoke(options);
        return (new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>(), Microsoft.Extensions.Options.Options.Create(options), new NTBlazorCircuitContext()), logs);
    }

    // The meter is static and shared by parallel tests, so only measurements recorded on the creating thread are kept.
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
