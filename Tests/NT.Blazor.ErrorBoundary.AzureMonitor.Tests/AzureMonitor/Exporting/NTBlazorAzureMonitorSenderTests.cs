using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;
using System.Net;
using System.Net.Http.Headers;
using static NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.TestServices;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.Exporting;

public sealed class NTBlazorAzureMonitorSenderTests {
    [Fact]
    public async Task TransientFailures_AreRetriedWithIncreasingBackoff() {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests);
        var time = new ImmediateTimeProvider();
        await using var provider = CreateServices(handler, time);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);
    }

    [Fact]
    public async Task PersistentFailure_StopsAfterMaxAttemptsAndLogsWarning() {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(NTBlazorAzureMonitorSender.MaxAttempts, handler.Requests.Count);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("503", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task NonRetryableResponse_IsSentOnceAndLogged(HttpStatusCode status) {
        var handler = new RecordingHandler(status);
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Credential_SendsBearerTokenForAzureMonitorScope() {
        var handler = new RecordingHandler();
        var credential = new FakeTokenCredential();
        await using var provider = CreateServices(handler, configure: options => options.Credential = credential);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal("Bearer test-token", Assert.Single(handler.Requests).Authorization);
        Assert.Equal(["https://monitor.azure.com//.default"], Assert.Single(credential.RequestedScopes));
    }

    [Fact]
    public async Task ConnectionStringWithoutIngestionEndpoint_UsesGlobalEndpoint() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler, connectionString: "InstrumentationKey=ikey");

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://dc.services.visualstudio.com/v2.1/track"), Assert.Single(handler.Requests).Uri);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task RetryAfterSeconds_SetsTheRetryDelay(int status) {
        var handler = new ScriptedHandler((index, _) => index == 0 ? RespondRetryAfter((HttpStatusCode)status, new RetryConditionHeaderValue(TimeSpan.FromSeconds(7))) : Respond(HttpStatusCode.OK));
        var time = new ImmediateTimeProvider();
        await using var provider = CreateServices(handler, time);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Bodies.Length);
        Assert.Equal([TimeSpan.FromSeconds(7)], time.Delays);
    }

    [Fact]
    public async Task RetryAfterDate_WaitsUntilThatTime() {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = new ScriptedHandler((index, _) => index == 0 ? RespondRetryAfter(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(now.AddSeconds(30))) : Respond(HttpStatusCode.OK));
        var time = new ImmediateTimeProvider { Now = now };
        await using var provider = CreateServices(handler, time);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(30)], time.Delays);
    }

    [Fact]
    public async Task RetryAfterBeyondMaximum_IsCapped() {
        var handler = new ScriptedHandler((index, _) => index == 0 ? RespondRetryAfter(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromHours(1))) : Respond(HttpStatusCode.OK));
        var time = new ImmediateTimeProvider();
        await using var provider = CreateServices(handler, time);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal([NTBlazorAzureMonitorSender.MaxRetryDelay], time.Delays);
    }

    [Fact]
    public async Task RetryAfterInThePast_FallsBackToBackoff() {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = new ScriptedHandler((index, _) => index == 0 ? RespondRetryAfter(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(now.AddSeconds(-5))) : Respond(HttpStatusCode.OK));
        var time = new ImmediateTimeProvider { Now = now };
        await using var provider = CreateServices(handler, time);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(2)], time.Delays);
    }

    [Fact]
    public async Task CredentialWithPlainHttpIngestionEndpoint_FailsBeforeAnyTokenIsSent() {
        await using var provider = CreateServices(new RecordingHandler(), configure: options => options.Credential = new FakeTokenCredential(), connectionString: "InstrumentationKey=ikey;IngestionEndpoint=http://ingest.test/");

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<NTBlazorAzureMonitorExporter>());

        Assert.Contains("https", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainHttpIngestionEndpointWithoutCredential_IsAllowed() {
        await using var provider = CreateServices(new RecordingHandler(), connectionString: "InstrumentationKey=ikey;IngestionEndpoint=http://ingest.test/");

        Assert.NotNull(provider.GetRequiredService<NTBlazorAzureMonitorExporter>());
    }

    [Fact]
    public async Task LongFailureResponse_IsTruncatedInTheWarning() {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.BadRequest, new string('x', 5000)));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        var message = Assert.Single(Warnings(logs)).Message;
        Assert.Contains(new string('x', NTBlazorAzureMonitorSender.MaxLoggedResponseChars), message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', NTBlazorAzureMonitorSender.MaxLoggedResponseChars + 1), message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task RetryableStatus_IsAttemptedThreeTimes(int status) {
        var handler = new ScriptedHandler((_, _) => Respond((HttpStatusCode)status));
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(NTBlazorAzureMonitorSender.MaxAttempts, handler.Bodies.Length);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);
        Assert.Contains($"Status: {status}.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(499)]
    public async Task FinalStatus_IsAttemptedOnce(int status) {
        var handler = new ScriptedHandler((_, _) => Respond((HttpStatusCode)status));
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Single(handler.Bodies);
        Assert.Empty(time.Delays);
        Assert.Contains($"Status: {status}.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkException_IsRetriedAndLoggedAsNoResponse() {
        var handler = new ScriptedHandler((_, _) => throw new HttpRequestException("Connection refused (ingest.test:443)"));
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(NTBlazorAzureMonitorSender.MaxAttempts, handler.Bodies.Length);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);
        var warning = Assert.Single(Warnings(logs)).Message;
        Assert.Contains("Status: no response.", warning, StringComparison.Ordinal);
        Assert.Contains("Connection refused", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("faults")]
    [InlineData("cancels")]
    public async Task CredentialFailure_IsRetriedWithoutPosting(string failure) {
        var credential = failure switch {
            "throws" => new ThrowingTokenCredential(new InvalidOperationException("No managed identity endpoint."), _faultTask: false),
            "faults" => new ThrowingTokenCredential(new InvalidOperationException("No managed identity endpoint."), _faultTask: true),
            _ => new ThrowingTokenCredential(new OperationCanceledException("The token request was canceled."), _faultTask: true)
        };
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.OK));
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs, options => options.Credential = credential);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["{}"], TestContext.Current.CancellationToken);

        Assert.Equal(NTBlazorAzureMonitorSender.MaxAttempts, credential.Calls);
        Assert.Empty(handler.Bodies);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);
        Assert.Contains("Status: no response.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialContentWithRetryableItemErrors_ResendsOnlyThoseItems() {
        // Item indexes refer to the batch that was sent, so the second answer's index 1 is "c" of the resent "a", "c", "d".
        var handler = new ScriptedHandler((index, _) => index switch {
            0 => Respond(HttpStatusCode.PartialContent, """{"itemsReceived":5,"itemsAccepted":1,"errors":[{"index":0,"statusCode":500},{"index":1,"statusCode":400},{"index":2,"statusCode":429},{"index":3,"statusCode":408},{"index":7,"statusCode":503}]}"""),
            1 => Respond(HttpStatusCode.PartialContent, """{"itemsReceived":3,"itemsAccepted":2,"errors":[{"index":1,"statusCode":503}]}"""),
            _ => Respond(HttpStatusCode.OK)
        });
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["a", "b", "c", "d", "e"], TestContext.Current.CancellationToken);

        Assert.Equal(["a\nb\nc\nd\ne", "a\nc\nd", "c"], handler.Bodies);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], time.Delays);
        Assert.Empty(Warnings(logs));
    }

    [Fact]
    public async Task PartialContentWithUnparseableBody_IsFinal() {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.PartialContent, "<html>Bad gateway</html>"));
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["a", "b"], TestContext.Current.CancellationToken);

        Assert.Single(handler.Bodies);
        Assert.Empty(time.Delays);
        Assert.Contains("Status: 206.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    // Valid JSON in an unexpected shape must still end in a logged warning: an exception escaping SendAsync is swallowed by the send loop,
    // which would drop the batch without any log. The test does not assume whether a null status code is retried; either way one 206 warning is logged.
    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"errors":[1]}""")]
    [InlineData("""{"errors":[{"index":"0","statusCode":500}]}""")]
    [InlineData("""{"errors":[{"index":0,"statusCode":"500"}]}""")]
    [InlineData("""{"errors":[{"index":0,"statusCode":null}]}""")]
    public async Task PartialContentWithUnexpectedErrorShape_LogsWarningInsteadOfThrowing(string body) {
        var handler = new ScriptedHandler((_, _) => Respond(HttpStatusCode.PartialContent, body));
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, logs: logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["a"], TestContext.Current.CancellationToken);

        Assert.Contains("Status: 206.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public async Task FinalStatusWithUnreadableBody_IsNotRetried(int status) {
        // HttpClient buffers the bytes, then decoding them fails on the unknown charset.
        var handler = new ScriptedHandler((_, _) => {
            var content = new ByteArrayContent("rejected"u8.ToArray());
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain; charset=not-a-charset");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content });
        });
        var time = new ImmediateTimeProvider();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateServices(handler, time, logs);

        await provider.GetRequiredService<NTBlazorAzureMonitorExporter>().Sender.SendAsync(["a"], TestContext.Current.CancellationToken);

        Assert.Single(handler.Bodies);
        Assert.Empty(time.Delays);
        Assert.Contains($"Status: {status}.", Assert.Single(Warnings(logs)).Message, StringComparison.Ordinal);
    }
}
