using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Services;
using NT.Blazor.ErrorBoundary.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

// The host only configures exporters; AddNTBlazorErrorBoundaryServerLogging adds the meter and activity source itself.
public sealed class OpenTelemetryInstrumentationTests {
    [Fact]
    public void HostTracing_ExportsReportSpanUnderPageTraceWithoutExtraRegistration() {
        var spans = new List<Activity>();
        using var provider = CreateServices(otel => otel.WithTracing(tracing => tracing.AddInMemoryExporter(spans)));
        provider.GetRequiredService<TracerProvider>();
        var logs = new TestLoggerProvider();
        // A trace ID of this test's own, because parallel tests share the activity source and export into this provider too.
        var traceId = ActivityTraceId.CreateRandom();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), Report(traceId), new DefaultHttpContext());

        var span = Assert.Single(spans, span => span.TraceId == traceId);
        Assert.Equal("blazor.client_report", span.OperationName);
        Assert.Equal("fedcba0987654321", span.ParentSpanId.ToString());
        Assert.Equal(span.TraceId.ToString(), Assert.Single(logs.Entries).TraceId);
    }

    [Fact]
    public void NoHostTracing_StillLogsUnderPageTrace() {
        var logs = new TestLoggerProvider();
        var traceId = ActivityTraceId.CreateRandom();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), Report(traceId), new DefaultHttpContext());

        Assert.Equal(traceId.ToString(), Assert.Single(logs.Entries).TraceId);
        Assert.Null(Activity.Current);
    }

    [Fact]
    public async Task HostMetrics_ExportErrorCounterCountingOneReportWithoutExtraRegistration() {
        var metrics = new List<Metric>();
        using var provider = CreateServices(otel => otel.WithMetrics(meters => meters.AddInMemoryExporter(metrics)));
        var meterProvider = provider.GetRequiredService<MeterProvider>();
        // The counter is shared by parallel tests, so the report carries a boundary name of this test's own to pick out its data point.
        var boundary = Guid.NewGuid().ToString("N");
        var reporter = new NTBlazorServerErrorReporter(NullLogger<NTBlazorServerErrorReporter>.Instance, Options.Create(new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }), new NTBlazorCircuitContext());

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext { BoundaryName = boundary }, TestContext.Current.CancellationToken);
        meterProvider.ForceFlush();

        var counter = Assert.Single(metrics, metric => metric.Name == "blazor.errors");
        Assert.Equal("{error}", counter.Unit);
        Assert.Equal(NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName, counter.MeterName);
        var total = 0L;
        foreach (var point in counter.GetMetricPoints()) {
            foreach (var tag in point.Tags) {
                if (tag.Key == "blazor.boundary.name" && Equals(tag.Value, boundary)) {
                    total += point.GetSumLong();
                }
            }
        }

        Assert.Equal(1, total);
    }

    private static ServiceProvider CreateServices(Action<OpenTelemetry.OpenTelemetryBuilder> configure) {
        var services = new ServiceCollection();
        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        configure(services.AddOpenTelemetry());
        return services.BuildServiceProvider();
    }

    private static NTBlazorErrorReport Report(ActivityTraceId traceId) => new() {
        ExceptionType = "System.InvalidOperationException",
        OccurredAtUtc = DateTimeOffset.UtcNow,
        TraceParent = $"00-{traceId}-fedcba0987654321-01"
    };
}
