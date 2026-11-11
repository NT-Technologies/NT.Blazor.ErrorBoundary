using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.Tests.Services;

public sealed class NTBlazorUnhandledExceptionLoggerProviderTests {
    private const string RendererCategory = "Microsoft.AspNetCore.Components.WebAssembly.Rendering.WebAssemblyRenderer";

    private readonly NTBlazorErrorBoundaryHttpClientOptions _options = new() { ReportUri = "/api/errors" };
    private readonly CapturingReporter _reporter = new();

    [Fact]
    public void Log_WhenRendererLogsError_ReportsException() {
        var exception = new InvalidOperationException("unhandled");

        CreateLogger(RendererCategory).LogError(exception, "Unhandled exception rendering component");

        Assert.Same(exception, _reporter.Exception);
        Assert.Equal(RendererCategory, _reporter.Context?.BoundaryName);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{8}$", _reporter.Context?.ReferenceId);
    }

    [Fact]
    public void Log_WhenReportingIsDisabled_DoesNotReport() {
        _options.ReportUnhandledExceptions = false;

        CreateLogger(RendererCategory).LogError(new InvalidOperationException("unhandled"), "failure");

        Assert.Null(_reporter.Exception);
    }

    [Theory]
    [InlineData(RendererCategory, LogLevel.Warning, true)]
    [InlineData(RendererCategory, LogLevel.Error, false)]
    [InlineData("Some.Other.Category", LogLevel.Error, true)]
    [InlineData("NT.Blazor.ErrorBoundary.Services.NTHttpClientBlazorErrorReporter", LogLevel.Error, true)]
    public void Log_WhenEntryIsNotForwardable_DoesNotReport(string category, LogLevel level, bool hasException) {
        CreateLogger(category).Log(level, new EventId(1), "state", hasException ? new InvalidOperationException("x") : null, (state, _) => state);

        Assert.Null(_reporter.Exception);
    }

    [Fact]
    public void Log_WhenReporterLogsWhileReporting_DoesNotRecurse() {
        var logger = CreateLogger(RendererCategory);
        _reporter.OnReport = () => logger.LogError(new InvalidOperationException("nested"), "nested");

        logger.LogError(new InvalidOperationException("outer"), "outer");

        Assert.Equal(1, _reporter.Count);
    }

    [Fact]
    public void Log_WhenReporterThrowsOrFaults_DoesNotThrow() {
        _reporter.OnReport = () => throw new InvalidOperationException("reporter failure");

        var exception = Record.Exception(() => CreateLogger(RendererCategory).LogError(new InvalidOperationException("unhandled"), "failure"));

        Assert.Null(exception);
    }

    [Fact]
    public void Log_WhenReporterIsNotRegistered_DoesNotThrow() {
        using var provider = new NTBlazorUnhandledExceptionLoggerProvider(new ServiceCollection().BuildServiceProvider(), Options.Create(_options));

        var exception = Record.Exception(() => provider.CreateLogger(RendererCategory).LogError(new InvalidOperationException("unhandled"), "failure"));

        Assert.Null(exception);
    }

    // WebAssemblyHostBuilder validates scopes in Development, where resolving a scoped service from the root provider throws.
    [Fact]
    public void Log_WhenReporterIsScopedAndScopesAreValidated_ReportsException() {
        var services = new ServiceCollection().AddScoped<INTBlazorErrorReporter>(_ => _reporter).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var provider = new NTBlazorUnhandledExceptionLoggerProvider(services, Options.Create(_options));
        var exception = new InvalidOperationException("unhandled");

        provider.CreateLogger(RendererCategory).LogError(exception, "failure");

        Assert.Same(exception, _reporter.Exception);
    }

    private ILogger CreateLogger(string category) {
        var services = new ServiceCollection().AddSingleton<INTBlazorErrorReporter>(_reporter).BuildServiceProvider();
        return new NTBlazorUnhandledExceptionLoggerProvider(services, Options.Create(_options)).CreateLogger(category);
    }

    private sealed class CapturingReporter : INTBlazorErrorReporter {
        public NTBlazorErrorBoundaryContext? Context { get; private set; }

        public int Count { get; private set; }

        public Exception? Exception { get; private set; }

        public Action? OnReport { get; set; }

        public Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
            Count++;
            Exception = exception;
            Context = context;
            OnReport?.Invoke();
            return Task.CompletedTask;
        }
    }
}
