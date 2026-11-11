using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope {
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<CapturedLog> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() {
    }

    private sealed class CapturingLogger(string _category, CapturingLoggerProvider _provider) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            _provider._scopes.ForEachScope(Add, fields);
            // Entry fields win over scope values, matching how exporters merge them.
            Add(state, fields);

            _provider._entries.Enqueue(new CapturedLog(_category, logLevel, formatter(state, exception), exception, fields));
        }

        private static void Add(object? value, Dictionary<string, object?> fields) {
            if (value is IEnumerable<KeyValuePair<string, object?>> pairs) {
                foreach (var pair in pairs) {
                    fields[pair.Key] = pair.Value;
                }
            }
        }
    }
}
