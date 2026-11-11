using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

internal sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Fields) {
    public string? FieldText(string key) => Fields.TryGetValue(key, out var value) ? value?.ToString() : null;
}
