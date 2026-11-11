using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using System.Globalization;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class NTBlazorLogTests {
    [Fact]
    public void PlaceholderWithoutField_IsKeptLiterallyInMessage() {
        var logs = new TestLoggerProvider();

        NTBlazorLog.Write(logs.CreateLogger("test"), LogLevel.Error, new InvalidOperationException("x"), Fields(("known", "value")), "A {known} and {unknown}");

        Assert.Equal("A value and {unknown}", Assert.Single(logs.Entries).Message);
    }

    [Fact]
    public void LineBreaksInFieldValue_AreRemovedFromMessageButKeptInState() {
        var logs = new TestLoggerProvider();
        const string Forged = "Boundary\r\nfail: forged";

        NTBlazorLog.Write(logs.CreateLogger("test"), LogLevel.Error, new InvalidOperationException("x"), Fields(("known", Forged)), "Caught by {known}");

        var entry = Assert.Single(logs.Entries);
        Assert.DoesNotContain('\r', entry.Message);
        Assert.DoesNotContain('\n', entry.Message);
        Assert.Equal(Forged, entry.Values["known"]);
    }

    [Fact]
    public void NullFieldValue_IsRenderedAsNullMarker() {
        var logs = new TestLoggerProvider();

        NTBlazorLog.Write(logs.CreateLogger("test"), LogLevel.Error, new InvalidOperationException("x"), Fields(("known", null)), "Value is {known}");

        Assert.Equal("Value is (null)", Assert.Single(logs.Entries).Message);
    }

    [Fact]
    public void State_CarriesFieldsAndTheTemplateAsOriginalFormat() {
        var logs = new TestLoggerProvider();
        const string Template = "A {known}";

        NTBlazorLog.Write(logs.CreateLogger("test"), LogLevel.Error, new InvalidOperationException("x"), Fields(("known", "value")), Template);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(Template, entry.Values["{OriginalFormat}"]);
        Assert.Equal("value", entry.Values["known"]);
    }

    [Fact]
    public void NumbersAndDates_AreFormattedInvariantlyUnderGermanCulture() {
        var logs = new TestLoggerProvider();
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try {
            NTBlazorLog.Write(logs.CreateLogger("test"), LogLevel.Error, new InvalidOperationException("x"), Fields(("number", 1234.5), ("when", new DateTime(2026, 11, 9, 13, 5, 0, DateTimeKind.Utc))), "{number} at {when}");
        }
        finally {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Equal("1234.5 at 11/09/2026 13:05:00", Assert.Single(logs.Entries).Message);
    }

    [Fact]
    public void WriteEvent_AddsEventNameFieldAndEventId() {
        var logs = new TestLoggerProvider();

        NTBlazorLog.WriteEvent(logs.CreateLogger("test"), LogLevel.Warning, NTBlazorLog.CircuitConnectionDown, Fields(), "Down");

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(NTBlazorLog.CircuitConnectionDown, entry.EventId);
        Assert.Equal("blazor.circuit.connection_down", entry.Values["microsoft.custom_event.name"]);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public void Export_WithoutExporter_DoesNothing() {
        var logs = new TestLoggerProvider();
        var called = false;

        NTBlazorLog.Export(logs.CreateLogger("test"), null, _ => called = true);

        Assert.False(called);
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public void Export_WhenExporterThrows_LogsOneWarningAndDoesNotThrow() {
        var logs = new TestLoggerProvider();
        var failure = new InvalidOperationException("exporter down");

        NTBlazorLog.Export(logs.CreateLogger("test"), new ThrowingExporter(failure), exporter => exporter.ExportEvent("e", DateTimeOffset.UnixEpoch, new Dictionary<string, object?>()));

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Same(failure, entry.Exception);
        Assert.Contains("INTBlazorErrorBoundaryExporter", entry.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Fields(params (string Key, object? Value)[] fields) => fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);

    private sealed class ThrowingExporter(Exception _failure) : INTBlazorErrorBoundaryExporter {
        public void ExportException(NTBlazorErrorReport report, IReadOnlyDictionary<string, object?> attributes) => throw _failure;

        public void ExportEvent(string name, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> attributes) => throw _failure;
    }
}
