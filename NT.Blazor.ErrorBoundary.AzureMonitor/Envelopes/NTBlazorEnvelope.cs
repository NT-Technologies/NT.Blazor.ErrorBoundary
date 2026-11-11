using NT.Blazor.ErrorBoundary.Models;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;

// Builds Application Insights envelopes (the schema the JavaScript SDK sends) from what the package logs.
// Unlike the .NET exporter, which reads an exception's type and frames from a thrown .NET object, this takes them from the report's text,
// so browser exceptions keep their original type and stack.
internal static class NTBlazorEnvelope {
    private const int MaxKeyLength = 150;
    private const int MaxValueLength = 8192;
    private const int MaxFieldLength = 1024;
    private const int MaxMessageLength = 32768;

    private static readonly string SdkVersion = "ntbeb:" + typeof(NTBlazorEnvelope).Assembly.GetName().Version?.ToString(3);

    // These attributes become context tags, duplicate one under an Azure-specific name, or name the event itself, so they are not repeated as properties.
    private static readonly HashSet<string> TagAttributes = new(StringComparer.Ordinal) { "microsoft.session.id", "enduser.id", "user_agent.original", "microsoft.custom_event.name" };

    internal static string CreateException(NTBlazorErrorReport report, IReadOnlyDictionary<string, object?> attributes, Activity? activity, string instrumentationKey, string roleName, string roleInstance) {
        var type = Limit(report.ExceptionType ?? report.ReportKind, MaxFieldLength);
        var frames = NTBlazorStackFrameParser.Parse(report.ExceptionStackTrace);
        var properties = Properties(attributes);
        // Server-rendered reports do not log the full exception text as an attribute; keep it, with inner exceptions, for the exception's details.
        if (!properties.ContainsKey("blazor.exception.details") && report.ExceptionDetails is { Length: > 0 } details) {
            properties["blazor.exception.details"] = Limit(details, MaxValueLength);
        }

        return Envelope("Exception", report.OccurredAtUtc, attributes, activity, instrumentationKey, roleName, roleInstance, "ExceptionData", new JsonObject {
            ["ver"] = 2,
            ["exceptions"] = new JsonArray(new JsonObject {
                ["id"] = 1,
                ["typeName"] = type,
                ["message"] = Limit(report.ExceptionMessage is { Length: > 0 } message ? message : type, MaxMessageLength),
                ["hasFullStack"] = frames.Count < NTBlazorStackFrameParser.MaxFrames,
                // The parser caps each frame line at NTBlazorStackFrameParser.MaxFrameLength, well under the ingestion limits, so frame fields need no limit here.
                ["parsedStack"] = new JsonArray([.. frames.Select((frame, level) => (JsonNode)new JsonObject {
                    ["level"] = level,
                    ["method"] = frame.Method,
                    ["assembly"] = frame.Raw,
                    ["fileName"] = frame.File,
                    ["line"] = frame.Line
                })])
            }),
            ["severityLevel"] = "Error",
            // The same "type at method" form Application Insights computes for exceptions the JavaScript SDK sends.
            ["problemId"] = Limit(frames.Count > 0 ? $"{type} at {frames[0].Method}" : type, MaxFieldLength),
            ["properties"] = properties
        });
    }

    internal static string CreateEvent(string name, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> attributes, Activity? activity, string instrumentationKey, string roleName, string roleInstance) =>
        Envelope("Event", occurredAt, attributes, activity, instrumentationKey, roleName, roleInstance, "EventData", new JsonObject {
            ["ver"] = 2,
            ["name"] = Limit(name, 512),
            ["properties"] = Properties(attributes)
        });

    private static string Envelope(string name, DateTimeOffset time, IReadOnlyDictionary<string, object?> attributes, Activity? activity, string instrumentationKey, string roleName, string roleInstance, string baseType, JsonObject baseData) {
        var tags = new JsonObject {
            ["ai.cloud.role"] = Limit(roleName, 256),
            ["ai.cloud.roleInstance"] = Limit(roleInstance, 256),
            ["ai.internal.sdkVersion"] = SdkVersion
        };
        AddTag(tags, "ai.operation.id", activity?.TraceId.ToHexString());
        AddTag(tags, "ai.operation.parentId", activity?.SpanId.ToHexString());
        AddTag(tags, "ai.session.id", Text(attributes, "session.id"), 64);
        AddTag(tags, "ai.user.authUserId", Text(attributes, "user.id"));
        AddTag(tags, "ai.user.userAgent", Text(attributes, "user_agent.original"));
        AddTag(tags, "ai.application.ver", Text(attributes, "blazor.app.version"));

        return new JsonObject {
            ["name"] = name,
            ["time"] = time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["iKey"] = instrumentationKey,
            ["tags"] = tags,
            ["data"] = new JsonObject {
                ["baseType"] = baseType,
                ["baseData"] = baseData
            }
        }.ToJsonString();
    }

    private static JsonObject Properties(IReadOnlyDictionary<string, object?> attributes) {
        var properties = new JsonObject();
        foreach (var (key, value) in attributes) {
            if (!TagAttributes.Contains(key) && key.Length <= MaxKeyLength && Format(value) is { Length: > 0 } text) {
                properties[key] = Limit(text, MaxValueLength);
            }
        }

        return properties;
    }

    private static void AddTag(JsonObject tags, string name, string? value, int maxLength = MaxFieldLength) {
        if (!string.IsNullOrEmpty(value)) {
            tags[name] = Limit(value, maxLength);
        }
    }

    private static string? Text(IReadOnlyDictionary<string, object?> attributes, string key) => attributes.TryGetValue(key, out var value) ? Format(value) : null;

    private static string? Format(object? value) => value switch {
        null => null,
        DateTimeOffset time => time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
