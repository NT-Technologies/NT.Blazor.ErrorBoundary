using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

// Throws from every warning or error, as a broken logging provider would, and counts the entries it refused.
internal sealed class ThrowingLoggerProvider : ILoggerProvider {
    private int _thrown;

    public int Thrown => Volatile.Read(ref _thrown);

    public ILogger CreateLogger(string categoryName) => new ThrowingLogger(this);

    public void Dispose() {
    }

    private sealed class ThrowingLogger(ThrowingLoggerProvider _provider) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (logLevel >= LogLevel.Warning) {
                Interlocked.Increment(ref _provider._thrown);
                throw new InvalidOperationException("The logging provider failed.");
            }
        }
    }
}
