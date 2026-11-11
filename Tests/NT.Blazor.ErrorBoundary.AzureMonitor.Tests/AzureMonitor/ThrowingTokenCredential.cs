using Azure.Core;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

// Fails every token request, either by throwing from the call itself or by returning a faulted task, and counts the requests.
internal sealed class ThrowingTokenCredential(Exception _exception, bool _faultTask) : TokenCredential {
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw _exception;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) {
        Interlocked.Increment(ref _calls);
        return _faultTask ? ValueTask.FromException<AccessToken>(_exception) : throw _exception;
    }
}
