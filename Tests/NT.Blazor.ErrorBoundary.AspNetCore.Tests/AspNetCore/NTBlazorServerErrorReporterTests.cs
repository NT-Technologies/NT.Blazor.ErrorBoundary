using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.Models;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class NTBlazorServerErrorReporterTests {
    [Fact]
    public async Task ReportAsync_LogsExceptionWithBoundaryContext() {
        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(loggerProvider));
        var reporter = new NTBlazorServerErrorReporter(loggerFactory.CreateLogger<NTBlazorServerErrorReporter>());
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
        Assert.Equal("Boundary", entry.ScopeValues["BlazorBoundaryName"]);
        Assert.Equal(true, entry.ScopeValues["BlazorIsInteractive"]);
        Assert.Equal("Server", entry.ScopeValues["BlazorRenderMode"]);
        Assert.Equal("https://example.test/dashboard", entry.ScopeValues["BlazorUri"]);
    }
}
