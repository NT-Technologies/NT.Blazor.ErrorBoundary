using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;
using NT.Blazor.ErrorBoundary.Models;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;

// Queues envelopes on the reporting thread and sends them from one background loop, so a slow or failing ingestion endpoint never delays the report endpoint.
internal sealed class NTBlazorAzureMonitorExporter : INTBlazorErrorBoundaryExporter, IHostedService {
    internal const int MaxBatchSize = 100;
    internal const int QueueCapacity = 1000;
    // Events stop being queued above this, so a burst of them (a reconnect storm during an outage) cannot crowd out exceptions.
    internal const int EventQueueLimit = 800;
    // Bounds queue memory when reports are large: about 32 MB of UTF-16 text.
    internal const long MaxQueuedChars = 16L * 1024 * 1024;
    private const string DefaultIngestionEndpoint = "https://dc.services.visualstudio.com/";

    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueCapacity) { SingleReader = true });
    // Not disposed: it has no timer, and disposing it would make a late StopAsync throw.
    private readonly CancellationTokenSource _abort = new();
    private readonly ILogger<NTBlazorAzureMonitorExporter> _logger;
    private readonly string _instrumentationKey;
    private readonly string _roleName;
    private readonly string _roleInstance = Dns.GetHostName();
    internal NTBlazorAzureMonitorSender Sender { get; }
    private Task? _sending;
    private int _dropping;
    private int _pending;
    private long _queuedChars;

    public NTBlazorAzureMonitorExporter(IHttpClientFactory httpClientFactory, IOptions<NTBlazorAzureMonitorOptions> options, ILogger<NTBlazorAzureMonitorExporter> logger, TimeProvider? timeProvider = null) {
        _logger = logger;
        var value = options.Value;
        _roleName = value.CloudRoleName ?? $"unknown_service:{Process.GetCurrentProcess().ProcessName}";

        var settings = (value.ConnectionString ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2 && pair[1].Length > 0)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
        _instrumentationKey = settings.GetValueOrDefault("InstrumentationKey")
            ?? throw new InvalidOperationException("NT.Blazor.ErrorBoundary.AzureMonitor needs an Application Insights connection string with an InstrumentationKey. Set APPLICATIONINSIGHTS_CONNECTION_STRING or NTBlazorAzureMonitorOptions.ConnectionString.");
        // Appended rather than resolved, so an endpoint with a path but no trailing slash keeps its last segment.
        var trackUri = new Uri((settings.GetValueOrDefault("IngestionEndpoint") ?? DefaultIngestionEndpoint).TrimEnd('/') + "/v2.1/track");
        // The sender attaches a bearer token to every request, which must not travel over plain HTTP.
        if (value.Credential is not null && trackUri.Scheme != Uri.UriSchemeHttps) {
            throw new InvalidOperationException("NTBlazorAzureMonitorOptions.Credential sends an Entra ID bearer token, so the connection string's IngestionEndpoint must use https.");
        }

        Sender = new(httpClientFactory, value.Credential, trackUri, timeProvider ?? TimeProvider.System, _logger);
    }

    public void ExportException(NTBlazorErrorReport report, IReadOnlyDictionary<string, object?> attributes) => Enqueue(NTBlazorEnvelope.CreateException(report, attributes, Activity.Current, _instrumentationKey, _roleName, _roleInstance), isEvent: false);

    public void ExportEvent(string name, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> attributes) => Enqueue(NTBlazorEnvelope.CreateEvent(name, occurredAt, attributes, Activity.Current, _instrumentationKey, _roleName, _roleInstance), isEvent: true);

    // One warning when dropping starts, not one per dropped item, so an outage does not flood the logs.
    private void Enqueue(string envelope, bool isEvent) {
        if (!(isEvent && _queue.Reader.Count >= EventQueueLimit) && TryReserve(envelope.Length)) {
            if (_queue.Writer.TryWrite(envelope)) {
                Volatile.Write(ref _dropping, 0);
                return;
            }

            Interlocked.Add(ref _queuedChars, -envelope.Length);
        }

        if (Interlocked.Exchange(ref _dropping, 1) == 0) {
            _logger.LogWarning("The Application Insights queue is full or stopped; Blazor errors and events are dropped until it has room.");
        }
    }

    private bool TryReserve(int length) {
        if (Interlocked.Add(ref _queuedChars, length) <= MaxQueuedChars) {
            return true;
        }

        Interlocked.Add(ref _queuedChars, -length);
        return false;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        _sending ??= SendQueuedAsync();
        return Task.CompletedTask;
    }

    // Drains what is queued; the host's shutdown timeout aborts sends that are still in flight.
    public async Task StopAsync(CancellationToken cancellationToken) {
        _queue.Writer.TryComplete();
        using (cancellationToken.Register(_abort.Cancel)) {
            await (_sending ?? Task.CompletedTask);
        }
    }

    private async Task SendQueuedAsync() {
        try {
            while (await _queue.Reader.WaitToReadAsync(_abort.Token)) {
                var batch = new List<string>(MaxBatchSize);
                while (batch.Count < MaxBatchSize && _queue.Reader.TryRead(out var envelope)) {
                    batch.Add(envelope);
                    Interlocked.Add(ref _queuedChars, -envelope.Length);
                }

                _pending = batch.Count;
                try {
                    await Sender.SendAsync(batch, _abort.Token);
                }
                // Only a failing logger can get here; delivery must not stop for the rest of the process because of it.
                catch (Exception) when (!_abort.IsCancellationRequested) {
                }

                _pending = 0;
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested) {
            if (_pending + _queue.Reader.Count is > 0 and var dropped) {
                _logger.LogWarning("Shutdown ended before {Count} Blazor errors or events were sent to Application Insights.", dropped);
            }
        }
    }
}
