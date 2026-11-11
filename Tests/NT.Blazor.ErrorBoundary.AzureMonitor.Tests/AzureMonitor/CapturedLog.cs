using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

internal sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);
