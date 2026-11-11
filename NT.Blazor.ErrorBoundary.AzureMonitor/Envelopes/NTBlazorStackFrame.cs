namespace NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;

// Raw is the whole frame line, sent as the frame's "assembly" the way the JavaScript SDK does.
internal sealed record NTBlazorStackFrame(string Method, string? File, int Line, string Raw);
