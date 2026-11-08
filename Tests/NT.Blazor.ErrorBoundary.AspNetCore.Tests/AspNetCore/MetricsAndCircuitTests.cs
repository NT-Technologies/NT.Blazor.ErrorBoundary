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
        Assert.Equal("server", measurement.Tags["source"]);
        Assert.Equal(NTBlazorReportKinds.Exception, measurement.Tags["report_kind"]);
        Assert.Equal("Boundary", measurement.Tags["boundary"]);
    }

    [Fact]
    public void ClientReport_IncrementsCounterWithoutBoundaryTag() {
        using var recorder = new CounterRecorder();
        var report = new NTBlazorErrorReport { BoundaryName = "attacker-controlled", ReportKind = NTBlazorReportKinds.JavaScriptError, OccurredAtUtc = DateTimeOffset.UtcNow };

        NTBlazorErrorBoundaryAspNetCoreExtensions.HandleClientReport(new TestLoggerProvider().CreateLogger("test"), report, CreateHttpContext(), Options());

        var measurement = Assert.Single(recorder.Measurements);
        Assert.Equal("client", measurement.Tags["source"]);
        Assert.Equal(NTBlazorReportKinds.JavaScriptError, measurement.Tags["report_kind"]);
        Assert.DoesNotContain("boundary", measurement.Tags.Keys);
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
        Assert.Equal("https://example.test/a", entry.ScopeValues["BlazorUri"]);
        Assert.Equal("ref-9", entry.ScopeValues["BlazorErrorReferenceId"]);
    }

    [Fact]
    public async Task ServerReport_FilterRedactions_AreLoggedButOriginalExceptionIsKept() {
        var (reporter, logs) = CreateReporter(options => options.FilterReport = report => new NTBlazorErrorReport { BoundaryName = "redacted", ReportKind = report.ReportKind });
        var exception = new InvalidOperationException("x");

        await reporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext { BoundaryName = "secret" }, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries);
        Assert.Same(exception, entry.Exception);
        Assert.Equal("redacted", entry.ScopeValues["BlazorBoundaryName"]);
    }

    [Fact]
    public async Task CircuitHandler_StoresIdLogsEventsAndFeedsReporterScope() {
        var logs = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        var circuit = new NTBlazorCircuitContext();
        var handler = new NTBlazorCircuitDiagnosticsHandler(loggerFactory.CreateLogger<NTBlazorCircuitDiagnosticsHandler>(), circuit);

        handler.Log("circuit-1", LogLevel.Information, "Blazor circuit {BlazorCircuitId} opened");
        handler.Log("circuit-1", LogLevel.Warning, "Blazor circuit {BlazorCircuitId} connection down");

        Assert.Equal("circuit-1", circuit.CircuitId);
        Assert.Collection(
            logs.Entries,
            opened => {
                Assert.Equal(LogLevel.Information, opened.LogLevel);
                Assert.Equal("circuit-1", opened.ScopeValues["BlazorCircuitId"]);
                Assert.Contains("circuit-1", opened.Message, StringComparison.Ordinal);
            },
            down => Assert.Equal(LogLevel.Warning, down.LogLevel));

        var reporter = new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>(), Microsoft.Extensions.Options.Options.Create(Options()), circuit);
        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);
        Assert.Equal("circuit-1", logs.Entries[^1].ScopeValues["BlazorCircuitId"]);
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
                if (instrument.Meter.Name == NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName && instrument.Name == NTBlazorErrorBoundaryMetrics.ErrorsCounterName) {
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
