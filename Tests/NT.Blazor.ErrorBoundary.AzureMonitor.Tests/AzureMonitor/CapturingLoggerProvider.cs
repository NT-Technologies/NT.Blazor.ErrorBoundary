using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

internal sealed class CapturingLoggerProvider : ILoggerProvider {
    public List<CapturedLog> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void Dispose() {
    }

    private sealed class CapturingLogger(string _category, List<CapturedLog> _entries) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            lock (_entries) {
                _entries.Add(new CapturedLog(_category, logLevel, eventId, formatter(state, exception), exception));
            }
        }
    }
}
