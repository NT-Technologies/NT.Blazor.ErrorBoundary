namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

internal sealed record RecordedRequest(Uri Uri, string? MediaType, string? Authorization, string Body, bool InstrumentationSuppressed);
