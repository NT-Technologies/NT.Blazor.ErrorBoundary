using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;

namespace NT.Blazor.ErrorBoundary.Services;

internal sealed class NTBlazorUnhandledExceptionLoggerProvider(IServiceProvider _services, IOptions<NTBlazorErrorBoundaryHttpClientOptions> _options) : ILoggerProvider {
    [ThreadStatic]
    private static bool _isReporting;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() {
    }

    private void Forward(string category, Exception exception) {
        if (_isReporting || !_options.Value.ReportUnhandledExceptions) {
            return;
        }

        _isReporting = true;
        try {
            _services.GetService<INTBlazorErrorReporter>()?.ReportAsync(exception, new NTBlazorErrorBoundaryContext {
                BoundaryName = category,
                ReferenceId = NTBlazorErrorReference.Create()
            }).ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch {
            // Reporting must never throw back into the logging pipeline.
        }
        finally {
            _isReporting = false;
        }
    }

    private sealed class Logger(NTBlazorUnhandledExceptionLoggerProvider _provider, string _category) : ILogger {
        private readonly bool _isForwarded = _category.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _isForwarded && logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (exception is not null && IsEnabled(logLevel)) {
                _provider.Forward(_category, exception);
            }
        }
    }
}
