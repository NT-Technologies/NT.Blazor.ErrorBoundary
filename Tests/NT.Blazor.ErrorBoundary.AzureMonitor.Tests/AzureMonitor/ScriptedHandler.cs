namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

// Answers each request through a test-supplied script, given the request's zero-based index, and records each body and the most requests ever in flight at once.
internal sealed class ScriptedHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> _respond) : HttpMessageHandler {
    private readonly List<string> _bodies = [];
    private int _inFlight;
    private int _maxInFlight;

    public string[] Bodies {
        get {
            lock (_bodies) {
                return [.. _bodies];
            }
        }
    }

    public int MaxInFlight {
        get {
            lock (_bodies) {
                return _maxInFlight;
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        int index;
        lock (_bodies) {
            index = _bodies.Count;
            _bodies.Add(body);
            _maxInFlight = Math.Max(_maxInFlight, ++_inFlight);
        }

        try {
            return await _respond(index, cancellationToken);
        }
        finally {
            lock (_bodies) {
                _inFlight--;
            }
        }
    }
}
