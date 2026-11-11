using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;
using NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;
using NT.Blazor.ErrorBoundary.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using static NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.TestServices;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.Exporting;

public sealed class NTBlazorAzureMonitorExporterTests {
    private const string DropWarningText = "The Application Insights queue is full or stopped";

    [Fact]
    public async Task ExportedReport_IsPostedAsJsonStreamToTrackEndpointBeforeStopCompletes() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);

        provider.GetRequiredService<INTBlazorErrorBoundaryExporter>().ExportException(Report(), new Dictionary<string, object?>());
        await exporter.StopAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://ingest.test/v2.1/track"), request.Uri);
        Assert.Equal("application/x-json-stream", request.MediaType);
        Assert.Null(request.Authorization);
        Assert.True(request.InstrumentationSuppressed);
        var envelope = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("ikey", envelope.GetProperty("iKey").GetString());
        Assert.Equal("System.InvalidOperationException", envelope.GetProperty("data").GetProperty("baseData").GetProperty("exceptions")[0].GetProperty("typeName").GetString());
    }

    [Fact]
    public async Task ExportedEvent_IsPostedAsEventEnvelope() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);

        exporter.ExportEvent("blazor.circuit.connection_down", DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["aspnetcore.components.circuit.id"] = "circuit-1" });
        await exporter.StopAsync(TestContext.Current.CancellationToken);

        var envelope = JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement;
        Assert.Equal("Event", envelope.GetProperty("name").GetString());
        Assert.Equal("blazor.circuit.connection_down", envelope.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("IngestionEndpoint=https://ingest.test/")]
    public async Task MissingInstrumentationKey_FailsAtStartup(string? connectionString) {
        await using var provider = CreateServices(new RecordingHandler(), connectionString: connectionString);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<NTBlazorAzureMonitorExporter>());
        Assert.Contains("APPLICATIONINSIGHTS_CONNECTION_STRING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_DefaultFromConfigurationButKeepExplicitValues() {
        await using var provider = CreateServices(new RecordingHandler(), configure: options => options.CloudRoleName = "explicit-role", settings: new() { ["OTEL_SERVICE_NAME"] = "configured-role" });

        var options = provider.GetRequiredService<IOptions<NTBlazorAzureMonitorOptions>>().Value;

        Assert.Equal(ConnectionString, options.ConnectionString);
        Assert.Equal("explicit-role", options.CloudRoleName);
    }

    [Fact]
    public async Task ExportAfterStop_IsDroppedWithWarning() {
        var handler = new RecordingHandler();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await exporter.StopAsync(TestContext.Current.CancellationToken);

        exporter.ExportException(Report(), new Dictionary<string, object?>());

        Assert.Empty(handler.Requests);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("dropped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Registration_IsTheHostedServiceAndTheEndpointExporter() {
        await using var provider = CreateServices(new RecordingHandler());

        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();

        Assert.Same(exporter, provider.GetRequiredService<INTBlazorErrorBoundaryExporter>());
        Assert.Contains(exporter, provider.GetServices<IHostedService>());
    }

    [Fact]
    public async Task StopTokenCancelledDuringSend_AbortsInFlightSendAndReturns() {
        var started = Signal();
        var aborted = Signal();
        var handler = new ScriptedHandler(async (_, cancellationToken) => {
            started.TrySetResult();
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) {
                aborted.TrySetResult();
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var provider = CreateServices(handler);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        ExportEvents(exporter, 0, 1);
        await Within(started.Task);

        using var shutdown = new CancellationTokenSource();
        var stop = exporter.StopAsync(shutdown.Token);
        // The ingestion endpoint never answers, so only the host's shutdown token can end the stop.
        Assert.False(stop.IsCompleted);
        await shutdown.CancelAsync();
        await Within(stop);

        Assert.True(aborted.Task.IsCompleted);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task StopWithCancelledToken_LogsUndeliveredCount() {
        var started = Signal();
        var handler = new ScriptedHandler(async (_, cancellationToken) => {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        ExportEvents(exporter, 0, 150);
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(started.Task);

        await Within(exporter.StopAsync(new CancellationToken(canceled: true)));

        // One batch of 100 was in flight and 50 were still queued.
        Assert.Equal([100], BatchSizes(handler));
        Assert.Equal(ShutdownWarning(150), Assert.Single(Warnings(logs)).Message);
    }

    [Fact]
    public async Task QueuedBeyondBatchSize_StopDrainsInBatchesOfOneHundred() {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        ExportEvents(exporter, 0, 250);

        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([100, 100, 50], BatchSizes(handler));
        Assert.Equal(EventNames(0, 250), DeliveredNames(handler));
        Assert.Empty(Warnings(logs));
    }

    [Fact]
    public async Task QueueFull_WarnsOnceAndDeliversFirstThousand() {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();

        ExportErrors(exporter, 0, NTBlazorAzureMonitorExporter.QueueCapacity + 5);
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, DropWarnings(logs));
        Assert.Equal(ErrorNames(0, NTBlazorAzureMonitorExporter.QueueCapacity), DeliveredNames(handler));
    }

    [Fact]
    public async Task DroppingThenRoom_WarnsAgainOnlyAfterRecovery() {
        var started = Signal();
        var release = Signal();
        var handler = new ScriptedHandler(async (index, cancellationToken) => {
            if (index == 0) {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        ExportErrors(exporter, 0, 1002);
        Assert.Equal(1, DropWarnings(logs));

        // The loop holds the first batch of 100 in flight, which leaves room for exactly 100 more.
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(started.Task);
        ExportErrors(exporter, 1002, 100);
        Assert.Equal(1, DropWarnings(logs));
        ExportErrors(exporter, 1102, 2);
        Assert.Equal(2, DropWarnings(logs));

        release.TrySetResult();
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([.. ErrorNames(0, 1000), .. ErrorNames(1002, 100)], DeliveredNames(handler));
    }

    [Fact]
    public async Task EventsBeyondEventLimit_AreDroppedWhileExceptionsStillQueue() {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();

        ExportEvents(exporter, 0, NTBlazorAzureMonitorExporter.EventQueueLimit + 1);
        Assert.Equal(1, DropWarnings(logs));
        ExportErrors(exporter, 0, NTBlazorAzureMonitorExporter.QueueCapacity - NTBlazorAzureMonitorExporter.EventQueueLimit);
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([.. EventNames(0, NTBlazorAzureMonitorExporter.EventQueueLimit), .. ErrorNames(0, NTBlazorAzureMonitorExporter.QueueCapacity - NTBlazorAzureMonitorExporter.EventQueueLimit)], DeliveredNames(handler));
        Assert.Equal(1, DropWarnings(logs));
    }

    [Fact]
    public async Task QueuedTextBeyondSizeBudget_IsDropped() {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        var envelopeLength = ExportLarge(exporter, 1);
        var fitting = (int)(NTBlazorAzureMonitorExporter.MaxQueuedChars / envelopeLength);

        ExportLarge(exporter, fitting);
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal(fitting, handler.Bodies.Sum(body => body.Split('\n').Length));
        Assert.Equal(1, DropWarnings(logs));
    }

    [Fact]
    public async Task SentText_FreesSizeBudget() {
        var started = Signal();
        var release = Signal();
        var handler = new ScriptedHandler(async (index, cancellationToken) => {
            if (index == 0) {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        var envelopeLength = ExportLarge(exporter, 1);
        var fitting = (int)(NTBlazorAzureMonitorExporter.MaxQueuedChars / envelopeLength);
        ExportLarge(exporter, fitting);
        Assert.Equal(1, DropWarnings(logs));

        // Taking the first batch of 100 off the queue frees room for exactly 100 more.
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(started.Task);
        ExportLarge(exporter, NTBlazorAzureMonitorExporter.MaxBatchSize);
        Assert.Equal(1, DropWarnings(logs));
        ExportLarge(exporter, 1);
        Assert.Equal(2, DropWarnings(logs));

        release.TrySetResult();
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));
        Assert.Equal(fitting + NTBlazorAzureMonitorExporter.MaxBatchSize, handler.Bodies.Sum(body => body.Split('\n').Length));
    }

    [Fact]
    public async Task ExportsAfterStop_WarnOnce() {
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK)), logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        ExportEvents(exporter, 0, 3);

        Assert.Equal(1, DropWarnings(logs));
    }

    [Fact]
    public async Task HttpClientTimeout_IsRetriedNotTreatedAsShutdown() {
        // A cancellation the caller did not request is what HttpClient reports as its own timeout: it rethrows it as a TaskCanceledException
        // carrying a TimeoutException, exactly as when the named client's Timeout elapses, without the test waiting for that timeout.
        var handler = new ScriptedHandler((_, _) => throw new OperationCanceledException("The upload stalled."));
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);

        ExportEvents(exporter, 0, 1);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(10), provider.GetRequiredService<IHttpClientFactory>().CreateClient(NTBlazorAzureMonitorSender.HttpClientName).Timeout);
        Assert.Equal(NTBlazorAzureMonitorSender.MaxAttempts, handler.Bodies.Length);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);
        Assert.Contains("Status: no response.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalFailureOfOneBatch_DoesNotStopLaterBatches() {
        var handler = new ScriptedHandler((index, _) => Respond(index == 0 ? HttpStatusCode.BadRequest : HttpStatusCode.OK));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        ExportEvents(exporter, 0, 150);

        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([100, 50], BatchSizes(handler));
        Assert.Contains("Status: 400.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoggerThrowingInSendLoop_DoesNotStopDelivery() {
        var handler = new ScriptedHandler((index, _) => Respond(index == 0 ? HttpStatusCode.BadRequest : HttpStatusCode.OK));
        var logs = new ThrowingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        ExportEvents(exporter, 0, 150);

        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        // The first batch's failure warning threw from the logger; the second batch was still sent and stop did not fault.
        Assert.Equal(1, logs.Thrown);
        Assert.Equal([100, 50], BatchSizes(handler));
    }

    // Every interleaving of a producer with stop must account for each report: delivered when stop drains, or counted in the shutdown warning when it aborts,
    // with nothing duplicated. The queue never fills (at most QueueCapacity reports), so the only drop is the stopped writer, whose warning the producer sees
    // synchronously on the call that dropped; every earlier report was accepted.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRacingStop_EveryReportIsDeliveredOrCounted(bool abortShutdown) {
        for (var round = 0; round < 20; round++) {
            var started = Signal();
            var handler = new ScriptedHandler(async (_, cancellationToken) => {
                started.TrySetResult();
                if (abortShutdown) {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var logs = new CapturingLoggerProvider();
            await using var provider = CreateServices(handler, logs: logs);
            var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
            await exporter.StartAsync(TestContext.Current.CancellationToken);

            var producer = Task.Run(() => {
                for (var index = 0; index < NTBlazorAzureMonitorExporter.QueueCapacity; index++) {
                    ExportErrors(exporter, index, 1);
                    if (DropWarnings(logs) > 0) {
                        return index;
                    }
                }

                return NTBlazorAzureMonitorExporter.QueueCapacity;
            }, TestContext.Current.CancellationToken);
            await Within(started.Task);
            await Within(exporter.StopAsync(new CancellationToken(abortShutdown)));
            var accepted = await Within(producer);

            Assert.Equal(accepted < NTBlazorAzureMonitorExporter.QueueCapacity ? 1 : 0, DropWarnings(logs));
            if (abortShutdown) {
                Assert.Equal(ShutdownWarning(accepted), Assert.Single(Warnings(logs), entry => entry.Message.StartsWith("Shutdown", StringComparison.Ordinal)).Message);
            }
            else {
                Assert.Equal(ErrorNames(0, accepted), DeliveredNames(handler));
                Assert.DoesNotContain(Warnings(logs), entry => entry.Message.StartsWith("Shutdown", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task StartTwice_RunsOneSendLoop() {
        var started = Signal();
        var release = Signal();
        var handler = new ScriptedHandler(async (index, cancellationToken) => {
            if (index == 0) {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var provider = CreateServices(handler);
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        ExportEvents(exporter, 0, 150);

        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        await Within(started.Task);
        release.TrySetResult();
        await Within(exporter.StopAsync(TestContext.Current.CancellationToken));

        // A second loop would have sent the remaining 50 while the first batch was held.
        Assert.Equal(1, handler.MaxInFlight);
        Assert.Equal([100, 50], BatchSizes(handler));
    }

    [Fact]
    public async Task StopBeforeStartAndStopTwice_Complete() {
        var neverStarted = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        await using var neverStartedProvider = CreateServices(neverStarted);
        var idle = neverStartedProvider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await Within(idle.StopAsync(TestContext.Current.CancellationToken));
        await Within(idle.StopAsync(new CancellationToken(canceled: true)));

        var started = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        await using var startedProvider = CreateServices(started);
        var running = startedProvider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await running.StartAsync(TestContext.Current.CancellationToken);
        ExportEvents(running, 0, 1);
        await Within(running.StopAsync(TestContext.Current.CancellationToken));
        await Within(running.StopAsync(new CancellationToken(canceled: true)));

        Assert.Empty(neverStarted.Bodies);
        Assert.Equal(EventNames(0, 1), DeliveredNames(started));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Bounds a wait that should already be satisfied, so a hang fails the test instead of stalling the run; it never decides a passing outcome.
    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

    private static void ExportEvents(NTBlazorAzureMonitorExporter exporter, int first, int count) {
        for (var index = first; index < first + count; index++) {
            exporter.ExportEvent($"event-{index}", DateTimeOffset.UnixEpoch, new Dictionary<string, object?>());
        }
    }

    private static string[] EventNames(int first, int count) => [.. Enumerable.Range(first, count).Select(index => $"event-{index}")];

    private static void ExportErrors(NTBlazorAzureMonitorExporter exporter, int first, int count) {
        for (var index = first; index < first + count; index++) {
            exporter.ExportException(Report($"error-{index}"), new Dictionary<string, object?>());
        }
    }

    private static string[] ErrorNames(int first, int count) => [.. Enumerable.Range(first, count).Select(index => $"error-{index}")];

    // An exception envelope's message, or an event envelope's name.
    private static string DeliveredName(string line) {
        var baseData = JsonDocument.Parse(line).RootElement.GetProperty("data").GetProperty("baseData");
        return baseData.TryGetProperty("name", out var name) ? name.GetString()! : baseData.GetProperty("exceptions")[0].GetProperty("message").GetString()!;
    }

    private static int[] BatchSizes(ScriptedHandler handler) => [.. handler.Bodies.Select(body => body.Split('\n').Length)];

    private static string[] DeliveredNames(ScriptedHandler handler) => [.. handler.Bodies.SelectMany(body => body.Split('\n')).Select(DeliveredName)];

    // Queues count identical reports, which produce identical envelopes, and returns the length of one so tests can size the queue's text budget exactly.
    private static int ExportLarge(NTBlazorAzureMonitorExporter exporter, int count) {
        var report = Report(new string('x', 32 * 1024), DateTimeOffset.UnixEpoch);
        for (var index = 0; index < count; index++) {
            exporter.ExportException(report, new Dictionary<string, object?>());
        }

        return NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", $"unknown_service:{Process.GetCurrentProcess().ProcessName}", Dns.GetHostName()).Length;
    }

    private static string ShutdownWarning(int count) => $"Shutdown ended before {count} Blazor errors or events were sent to Application Insights.";

    private static int DropWarnings(CapturingLoggerProvider logs) => Warnings(logs).Count(entry => entry.Message.StartsWith(DropWarningText, StringComparison.Ordinal));

    private static NTBlazorErrorReport Report(string message = "boom", DateTimeOffset? occurredAt = null) => new() {
        ExceptionMessage = message,
        ExceptionType = "System.InvalidOperationException",
        OccurredAtUtc = occurredAt ?? DateTimeOffset.UtcNow

    };
}
