namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

// Fires every timer immediately and records the delay it was asked for, so retry backoff is tested without waiting. Now, when set, fixes the clock.
internal sealed class ImmediateTimeProvider : TimeProvider {
    public List<TimeSpan> Delays { get; } = [];

    public DateTimeOffset? Now { get; init; }

    public override DateTimeOffset GetUtcNow() => Now ?? base.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
        lock (Delays) {
            Delays.Add(dueTime);
        }

        return base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
}
