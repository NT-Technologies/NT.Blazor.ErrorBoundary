using NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;
using NT.Blazor.ErrorBoundary.Models;
using System.Diagnostics;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.Envelopes;

public sealed class NTBlazorEnvelopeTests {
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 9, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAtPlusTwo = new(2026, 11, 9, 14, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void FullReport_SetsEnvelopeTimeNameAndContextTags() {
        using var activity = new Activity("test").SetParentId("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01").Start();

        var envelope = Parse(NTBlazorEnvelope.CreateException(Report(), FullAttributes(), activity, "ikey", "role", "instance"));

        Assert.Equal("Exception", envelope.GetProperty("name").GetString());
        Assert.Equal("ikey", envelope.GetProperty("iKey").GetString());
        Assert.Equal("2026-11-09T12:30:00.0000000Z", envelope.GetProperty("time").GetString());
        var tags = envelope.GetProperty("tags");
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", tags.GetProperty("ai.operation.id").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), tags.GetProperty("ai.operation.parentId").GetString());
        Assert.Equal("session-1", tags.GetProperty("ai.session.id").GetString());
        Assert.Equal("user-7", tags.GetProperty("ai.user.authUserId").GetString());
        Assert.Equal("TestBrowser/1.0", tags.GetProperty("ai.user.userAgent").GetString());
        Assert.Equal("2.1.0", tags.GetProperty("ai.application.ver").GetString());
        Assert.Equal("role", tags.GetProperty("ai.cloud.role").GetString());
        Assert.Equal("instance", tags.GetProperty("ai.cloud.roleInstance").GetString());
        Assert.StartsWith("ntbeb:", tags.GetProperty("ai.internal.sdkVersion").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FullReport_BuildsExceptionDetailWithProblemIdAndParsedStack() {
        var data = Parse(NTBlazorEnvelope.CreateException(Report(), FullAttributes(), null, "ikey", "role", "instance")).GetProperty("data").GetProperty("baseData");

        Assert.Equal("Error", data.GetProperty("severityLevel").GetString());
        Assert.Equal("System.InvalidOperationException at App.Orders.Load", data.GetProperty("problemId").GetString());
        var detail = Assert.Single(data.GetProperty("exceptions").EnumerateArray());
        Assert.Equal("System.InvalidOperationException", detail.GetProperty("typeName").GetString());
        Assert.Equal("Order 42 failed", detail.GetProperty("message").GetString());
        Assert.True(detail.GetProperty("hasFullStack").GetBoolean());
        var frames = detail.GetProperty("parsedStack").EnumerateArray().ToList();
        Assert.Equal(2, frames.Count);
        Assert.Equal(0, frames[0].GetProperty("level").GetInt32());
        Assert.Equal("App.Orders.Load", frames[0].GetProperty("method").GetString());
        Assert.Equal("/src/Orders.cs", frames[0].GetProperty("fileName").GetString());
        Assert.Equal(12, frames[0].GetProperty("line").GetInt32());
    }

    [Fact]
    public void FullReport_KeepsOnlyUntaggedAttributesAsProperties() {
        var properties = Properties(NTBlazorEnvelope.CreateException(Report(), FullAttributes(), null, "ikey", "role", "instance"));

        Assert.Equal("a668f750feec0c1d", properties.GetProperty("blazor.error.fingerprint").GetString());
        Assert.Equal("True", properties.GetProperty("blazor.interactive").GetString());
        Assert.Equal("2026-11-09T12:30:00.0000000Z", properties.GetProperty("blazor.error.occurred_at").GetString());
        Assert.Equal("session-1", properties.GetProperty("session.id").GetString());
        Assert.Equal("user-7", properties.GetProperty("user.id").GetString());
        foreach (var excluded in new[] { "microsoft.session.id", "enduser.id", "user_agent.original", "blazor.navigation.id" }) {
            Assert.False(properties.TryGetProperty(excluded, out _), excluded);
        }
    }

    [Fact]
    public void ExceptionTime_IsConvertedToUtcFromOffset() {
        var report = new NTBlazorErrorReport { ExceptionType = "T", OccurredAtUtc = OccurredAtPlusTwo };

        var envelope = Parse(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance"));

        Assert.Equal("2026-11-09T12:30:00.0000000Z", envelope.GetProperty("time").GetString());
    }

    [Fact]
    public void EventTime_IsConvertedToUtcFromOffset() {
        var envelope = Parse(NTBlazorEnvelope.CreateEvent("e", OccurredAtPlusTwo, new Dictionary<string, object?>(), null, "ikey", "role", "instance"));

        Assert.Equal("2026-11-09T12:30:00.0000000Z", envelope.GetProperty("time").GetString());
    }

    [Fact]
    public void DateTimeOffsetAttribute_IsFormattedAsUtcFromOffset() {
        var attributes = new Dictionary<string, object?> { ["blazor.error.occurred_at"] = OccurredAtPlusTwo };

        var properties = Properties(NTBlazorEnvelope.CreateEvent("e", OccurredAt, attributes, null, "ikey", "role", "instance"));

        Assert.Equal("2026-11-09T12:30:00.0000000Z", properties.GetProperty("blazor.error.occurred_at").GetString());
    }

    [Fact]
    public void Event_BuildsCustomEventEnvelopeWithContextTags() {
        using var activity = new Activity("test").SetParentId("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01").Start();
        var attributes = new Dictionary<string, object?> {
            ["microsoft.custom_event.name"] = "blazor.navigation.stalled",
            ["blazor.navigation.target_url"] = "https://app.test/orders",
            ["session.id"] = "session-1"
        };

        var envelope = Parse(NTBlazorEnvelope.CreateEvent("blazor.navigation.stalled", OccurredAt, attributes, activity, "ikey", "role", "instance"));

        Assert.Equal("Event", envelope.GetProperty("name").GetString());
        Assert.Equal("2026-11-09T12:30:00.0000000Z", envelope.GetProperty("time").GetString());
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", envelope.GetProperty("tags").GetProperty("ai.operation.id").GetString());
        Assert.Equal("session-1", envelope.GetProperty("tags").GetProperty("ai.session.id").GetString());
        var data = envelope.GetProperty("data");
        Assert.Equal("EventData", data.GetProperty("baseType").GetString());
        Assert.Equal("blazor.navigation.stalled", data.GetProperty("baseData").GetProperty("name").GetString());
        var properties = data.GetProperty("baseData").GetProperty("properties");
        Assert.Equal("https://app.test/orders", properties.GetProperty("blazor.navigation.target_url").GetString());
        Assert.False(properties.TryGetProperty("microsoft.custom_event.name", out _));
    }

    [Fact]
    public void ServerReportWithoutDetailsAttribute_KeepsExceptionDetailsAsProperty() {
        var report = new NTBlazorErrorReport { ExceptionType = "T", ExceptionDetails = "System.InvalidOperationException: Order 42 failed ---> inner", OccurredAtUtc = OccurredAt };

        var properties = Properties(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance"));

        Assert.Equal("System.InvalidOperationException: Order 42 failed ---> inner", properties.GetProperty("blazor.exception.details").GetString());
    }

    [Fact]
    public void DetailsAttribute_WinsOverReportExceptionDetails() {
        var report = new NTBlazorErrorReport { ExceptionType = "T", ExceptionDetails = "from report", OccurredAtUtc = OccurredAt };
        var attributes = new Dictionary<string, object?> { ["blazor.exception.details"] = "from attribute" };

        var properties = Properties(NTBlazorEnvelope.CreateException(report, attributes, null, "ikey", "role", "instance"));

        Assert.Equal("from attribute", properties.GetProperty("blazor.exception.details").GetString());
    }

    [Fact]
    public void EmptyExceptionDetails_AreOmittedFromProperties() {
        var report = new NTBlazorErrorReport { ExceptionType = "T", ExceptionDetails = string.Empty, OccurredAtUtc = OccurredAt };

        var properties = Properties(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance"));

        Assert.False(properties.TryGetProperty("blazor.exception.details", out _));
    }

    [Fact]
    public void EmptyExceptionMessage_FallsBackToType() {
        var report = new NTBlazorErrorReport { ExceptionType = "System.InvalidOperationException", ExceptionMessage = string.Empty, OccurredAtUtc = OccurredAt };

        var detail = Parse(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance")).GetProperty("data").GetProperty("baseData").GetProperty("exceptions")[0];

        Assert.Equal("System.InvalidOperationException", detail.GetProperty("message").GetString());
    }

    [Fact]
    public void ReportWithoutTypeMessageOrFrames_FallsBackToReportKind() {
        var report = new NTBlazorErrorReport { ReportKind = NTBlazorReportKinds.ResourceLoadFailed, OccurredAtUtc = OccurredAt };

        var data = Parse(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance")).GetProperty("data").GetProperty("baseData");

        Assert.Equal(NTBlazorReportKinds.ResourceLoadFailed, data.GetProperty("problemId").GetString());
        var detail = Assert.Single(data.GetProperty("exceptions").EnumerateArray());
        Assert.Equal(NTBlazorReportKinds.ResourceLoadFailed, detail.GetProperty("typeName").GetString());
        Assert.Equal(NTBlazorReportKinds.ResourceLoadFailed, detail.GetProperty("message").GetString());
        Assert.Empty(detail.GetProperty("parsedStack").EnumerateArray());
    }

    [Fact]
    public void NoActivity_OmitsOperationTags() {
        var tags = Parse(NTBlazorEnvelope.CreateException(Report(), new Dictionary<string, object?>(), null, "ikey", "role", "instance")).GetProperty("tags");

        Assert.False(tags.TryGetProperty("ai.operation.id", out _));
        Assert.False(tags.TryGetProperty("ai.session.id", out _));
    }

    [Fact]
    public void EmptyStringAttributes_AreDroppedFromPropertiesAndTags() {
        var attributes = new Dictionary<string, object?> { ["blazor.boundary.name"] = string.Empty, ["session.id"] = string.Empty, ["blazor.client.app_version"] = string.Empty, ["kept"] = "value" };

        var envelope = Parse(NTBlazorEnvelope.CreateEvent("e", OccurredAt, attributes, null, "ikey", "role", "instance"));

        var properties = envelope.GetProperty("data").GetProperty("baseData").GetProperty("properties");
        Assert.False(properties.TryGetProperty("blazor.boundary.name", out _));
        Assert.Equal("value", properties.GetProperty("kept").GetString());
        Assert.False(envelope.GetProperty("tags").TryGetProperty("ai.session.id", out _));
        Assert.False(envelope.GetProperty("tags").TryGetProperty("ai.application.ver", out _));
    }

    [Fact]
    public void OversizedValues_AreTruncatedToIngestionLimits() {
        var attributes = new Dictionary<string, object?> {
            ["blazor.exception.details"] = new string('d', 10_000),
            [new string('k', 151)] = "dropped",
            ["session.id"] = new string('s', 100)
        };

        var envelope = Parse(NTBlazorEnvelope.CreateException(Report(), attributes, null, "ikey", "role", "instance"));

        var properties = envelope.GetProperty("data").GetProperty("baseData").GetProperty("properties");
        Assert.Equal(8192, properties.GetProperty("blazor.exception.details").GetString()!.Length);
        Assert.False(properties.TryGetProperty(new string('k', 151), out _));
        Assert.Equal(64, envelope.GetProperty("tags").GetProperty("ai.session.id").GetString()!.Length);
    }

    [Fact]
    public void TypeMessageAndProblemId_AreTruncatedToIngestionLimits() {
        var report = new NTBlazorErrorReport { ExceptionType = new string('t', 2000), ExceptionMessage = new string('m', 40_000), OccurredAtUtc = OccurredAt };

        var data = Parse(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance")).GetProperty("data").GetProperty("baseData");

        var detail = data.GetProperty("exceptions")[0];
        Assert.Equal(1024, detail.GetProperty("typeName").GetString()!.Length);
        Assert.Equal(32768, detail.GetProperty("message").GetString()!.Length);
        Assert.Equal(1024, data.GetProperty("problemId").GetString()!.Length);
    }

    [Fact]
    public void EventName_IsTruncatedTo512Characters() {
        var envelope = Parse(NTBlazorEnvelope.CreateEvent(new string('e', 600), OccurredAt, new Dictionary<string, object?>(), null, "ikey", "role", "instance"));

        Assert.Equal(512, envelope.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString()!.Length);
    }

    [Fact]
    public void RoleAndInstance_AreTruncatedTo256Characters() {
        var tags = Parse(NTBlazorEnvelope.CreateEvent("e", OccurredAt, new Dictionary<string, object?>(), null, "ikey", new string('r', 300), new string('i', 300))).GetProperty("tags");

        Assert.Equal(256, tags.GetProperty("ai.cloud.role").GetString()!.Length);
        Assert.Equal(256, tags.GetProperty("ai.cloud.roleInstance").GetString()!.Length);
    }

    [Theory]
    [InlineData(99, true)]
    [InlineData(100, false)]
    public void FullStackFlag_IsFalseOnceFrameCapIsReached(int frameCount, bool hasFullStack) {
        var stack = string.Join('\n', Enumerable.Range(0, frameCount).Select(index => $"at App.Frame{index}()"));
        var report = new NTBlazorErrorReport { ExceptionType = "T", ExceptionStackTrace = stack, OccurredAtUtc = OccurredAt };

        var detail = Parse(NTBlazorEnvelope.CreateException(report, new Dictionary<string, object?>(), null, "ikey", "role", "instance")).GetProperty("data").GetProperty("baseData").GetProperty("exceptions")[0];

        Assert.Equal(hasFullStack, detail.GetProperty("hasFullStack").GetBoolean());
    }

    private static Dictionary<string, object?> FullAttributes() => new(StringComparer.Ordinal) {
        ["blazor.error.fingerprint"] = "a668f750feec0c1d",
        ["blazor.interactive"] = true,
        ["blazor.error.occurred_at"] = OccurredAt,
        ["session.id"] = "session-1",
        ["microsoft.session.id"] = "session-1",
        ["user.id"] = "user-7",
        ["enduser.id"] = "user-7",
        ["user_agent.original"] = "TestBrowser/1.0",
        ["blazor.navigation.id"] = null,
        ["blazor.app.version"] = "2.1.0"
    };

    private static NTBlazorErrorReport Report() => new() {
        ApplicationVersion = "2.1.0",
        ExceptionMessage = "Order 42 failed",
        ExceptionStackTrace = "   at App.Orders.Load(Int32 id) in /src/Orders.cs:line 12\n   at App.Pages.OrderPage.Render()",
        ExceptionType = "System.InvalidOperationException",
        OccurredAtUtc = OccurredAt,
        ReportKind = NTBlazorReportKinds.Exception
    };

    private static JsonElement Properties(string envelope) => Parse(envelope).GetProperty("data").GetProperty("baseData").GetProperty("properties");

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
