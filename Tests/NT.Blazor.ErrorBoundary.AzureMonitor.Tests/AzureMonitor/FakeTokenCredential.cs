using Azure.Core;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

internal sealed class FakeTokenCredential : TokenCredential {
    public List<string[]> RequestedScopes { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) {
        RequestedScopes.Add(requestContext.Scopes);
        return new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
