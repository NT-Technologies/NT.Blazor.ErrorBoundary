using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

internal sealed class TestLoggerProvider : ILoggerProvider, ISupportExternalScope {
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public List<TestLogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, this);

    public void Dispose() {
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) {
        _scopeProvider = scopeProvider;
    }

    private sealed class TestLogger(string categoryName, TestLoggerProvider provider) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) {
            var scopes = new List<object?>();
            provider._scopeProvider.ForEachScope((scope, stateList) => stateList.Add(scope), scopes);

            provider.Entries.Add(new TestLogEntry(
                categoryName,
                logLevel,
                eventId,
                formatter(state, exception),
                exception,
                state,
                scopes));
        }
    }
}

internal sealed record TestLogEntry(
    string CategoryName,
    LogLevel LogLevel,
    EventId EventId,
    string Message,
    Exception? Exception,
    object? State,
    IReadOnlyList<object?> Scopes) {

    public IReadOnlyDictionary<string, object?> ScopeValues => Flatten(Scopes);

    private static IReadOnlyDictionary<string, object?> Flatten(IEnumerable<object?> values) {
        var flattened = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var value in values) {
            if (value is not IEnumerable<KeyValuePair<string, object?>> nullablePairs) {
                continue;
            }

            foreach (var pair in nullablePairs) {
                flattened[pair.Key] = pair.Value;
            }
        }

        return flattened;
    }
}
