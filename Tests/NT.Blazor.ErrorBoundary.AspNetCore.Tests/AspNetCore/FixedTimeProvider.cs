namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

internal sealed class FixedTimeProvider(DateTimeOffset _now) : TimeProvider {
    public override DateTimeOffset GetUtcNow() => _now;
}
