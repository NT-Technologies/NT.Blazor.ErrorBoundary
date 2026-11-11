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
        // A scope of its own, because this provider holds the root provider, and WebAssemblyHostBuilder validates scopes in Development,
        // where a scoped reporter would not resolve from the root. The scope lives until the report finishes sending.
        var scope = _services.CreateScope();
        try {
            var report = scope.ServiceProvider.GetService<INTBlazorErrorReporter>()?.ReportAsync(exception, new NTBlazorErrorBoundaryContext {
                BoundaryName = category,
                ReferenceId = NTBlazorErrorReference.Create()
            }) ?? Task.CompletedTask;
            report.ContinueWith(static (task, state) => {
                _ = task.Exception;
                ((IDisposable)state!).Dispose();
            }, scope, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch {
            // Reporting must never throw back into the logging pipeline.
            scope.Dispose();
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
