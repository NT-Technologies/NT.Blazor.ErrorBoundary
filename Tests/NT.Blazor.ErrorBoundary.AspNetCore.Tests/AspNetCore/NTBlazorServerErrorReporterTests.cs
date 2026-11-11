using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.AspNetCore;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Reflection;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class NTBlazorServerErrorReporterTests {
    [Fact]
    public async Task ReportAsync_LogsExceptionWithBoundaryContext() {
        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(loggerProvider));
        var reporter = new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>(), Options.Create(new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }), new NTBlazorCircuitContext());
        var exception = new InvalidOperationException("server failure");
        var context = new NTBlazorErrorBoundaryContext {
            BoundaryName = "Boundary",
            IsInteractive = true,
            RenderMode = "Server",
            Uri = "https://example.test/dashboard"
        };

        await reporter.ReportAsync(exception, context, TestContext.Current.CancellationToken);

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Same(exception, entry.Exception);
        Assert.Contains("Unhandled Blazor component exception", entry.Message, StringComparison.Ordinal);
        Assert.Equal("Boundary", entry.Values["blazor.boundary.name"]);
        Assert.Equal(true, entry.Values["blazor.interactive"]);
        Assert.Equal("Server", entry.Values["blazor.render_mode"]);
        Assert.Equal("https://example.test/dashboard", entry.Values["url.full"]);
    }

    [Fact]
    public async Task ThrowingFilter_DropsReportAndLogsOneWarningWithoutThrowing() {
        var (reporter, logs) = CreateReporter(new NTBlazorCircuitContext(), options => options.FilterReport = _ => throw new InvalidOperationException("filter bug"));

        await reporter.ReportAsync(new InvalidOperationException("server failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("filter bug", entry.Exception?.Message);
        Assert.StartsWith("FilterReport threw", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoCircuit_OmitsCircuitIdField() {
        var (reporter, logs) = CreateReporter(new NTBlazorCircuitContext());

        await reporter.ReportAsync(new InvalidOperationException("server failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("aspnetcore.components.circuit.id", Assert.Single(logs.Entries).Values.Keys);
    }

    [Fact]
    public async Task ActiveCircuit_LogsCircuitIdField() {
        var (reporter, logs) = CreateReporter(new NTBlazorCircuitContext { CircuitId = "circuit-9" });

        await reporter.ReportAsync(new InvalidOperationException("server failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Equal("circuit-9", Assert.Single(logs.Entries).Values["aspnetcore.components.circuit.id"]);
    }

    [Fact]
    public async Task FilteredReport_IsTheSourceOfEveryLoggedField() {
        var filtered = new NTBlazorErrorReport {
            BoundaryName = "redacted-boundary",
            ExceptionType = "Redacted.Type",
            IsInteractive = false,
            OriginUri = "https://example.test/redacted-origin",
            ReferenceId = "redacted-ref",
            RenderMode = "redacted-mode",
            Uri = "https://example.test/redacted-url"
        };
        var (reporter, logs) = CreateReporter(new NTBlazorCircuitContext(), options => options.FilterReport = _ => filtered);
        var context = new NTBlazorErrorBoundaryContext { BoundaryName = "secret-boundary", IsInteractive = true, OriginUri = "https://example.test/secret-origin", ReferenceId = "secret-ref", RenderMode = "secret-mode", Uri = "https://example.test/secret-url" };

        await reporter.ReportAsync(new InvalidOperationException("server failure"), context, TestContext.Current.CancellationToken);

        var values = Assert.Single(logs.Entries).Values;
        Assert.Equal("redacted-boundary", values["blazor.boundary.name"]);
        Assert.Equal(false, values["blazor.interactive"]);
        Assert.Equal("https://example.test/redacted-origin", values["blazor.navigation.origin_url"]);
        Assert.Equal("redacted-ref", values["blazor.error.reference_id"]);
        Assert.Equal("redacted-mode", values["blazor.render_mode"]);
        Assert.Equal("https://example.test/redacted-url", values["url.full"]);
        Assert.Equal(NTBlazorErrorFingerprint.Compute(filtered), values["blazor.error.fingerprint"]);
    }

    [Fact]
    public async Task ServerError_LogsServerAppVersion() {
        var (reporter, logs) = CreateReporter(new NTBlazorCircuitContext());

        await reporter.ReportAsync(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Equal(typeof(NTBlazorServerErrorReporterTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, Assert.Single(logs.Entries).Values["blazor.app.version"]);
    }

    private static (NTBlazorServerErrorReporter Reporter, TestLoggerProvider Logs) CreateReporter(NTBlazorCircuitContext circuit, Action<NTBlazorErrorBoundaryHttpClientOptions>? configure = null) {
        var logs = new TestLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        var options = new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" };
        configure?.Invoke(options);
        return (new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>(), Options.Create(options), circuit), logs);
    }
}
