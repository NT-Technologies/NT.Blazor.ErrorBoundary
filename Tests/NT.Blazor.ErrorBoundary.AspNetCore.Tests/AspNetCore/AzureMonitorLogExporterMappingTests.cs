using Azure.Core.Pipeline;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using OpenTelemetry.Logs;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

// Runs the real Azure Monitor exporter against a captured transport, so the asserted envelopes are what Application Insights would ingest.
public sealed class AzureMonitorLogExporterMappingTests {
    private const string PageTraceId = "1234567890abcdef1234567890abcdef";

    [Fact]
    public async Task ReportsWithoutScopes_MapToApplicationInsightsTelemetry() {
        var envelopes = new List<JsonElement>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter((category, _) => category?.StartsWith("NT.Blazor.ErrorBoundary", StringComparison.Ordinal) == true);
        // IncludeScopes stays at its default (false), as with the Azure Monitor distro.
        builder.Logging.AddOpenTelemetry(options => options.AddAzureMonitorLogExporter(exporter => {
            exporter.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://localhost/";
            exporter.Transport = new HttpClientTransport(new HttpClient(new CapturingHandler(envelopes)));
            exporter.DisableOfflineStorage = true;
        }));
        builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        var app = builder.Build();
        app.Use((context, next) => {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-7")], "Test"));
            return next(context);
        });
        app.MapNTBlazorErrorBoundaryTelemetry();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using (var client = app.GetTestClient()) {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TestBrowser/1.0");
            using var exceptionResponse = await client.PostAsJsonAsync("/api/errors", Report(NTBlazorReportKinds.Exception), TestContext.Current.CancellationToken);
            using var stallResponse = await client.PostAsJsonAsync("/api/errors", Report(NTBlazorReportKinds.NavigationStalled), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, exceptionResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, stallResponse.StatusCode);
        }

        using (var scope = app.Services.CreateScope()) {
            scope.ServiceProvider.GetServices<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>().OfType<NTBlazorCircuitDiagnosticsHandler>().Single()
                .OnConnectionChanged("circuit-1", isUp: false);
            var exception = Assert.Throws<InvalidOperationException>((Action)(() => throw new InvalidOperationException("Server render failed")));
            await scope.ServiceProvider.GetRequiredService<INTBlazorErrorReporter>().ReportAsync(exception, new NTBlazorErrorBoundaryContext { BoundaryName = "OrderPage" }, TestContext.Current.CancellationToken);
        }

        // Disposing the host flushes the exporter.
        await app.DisposeAsync();

        var browserException = Assert.Single(envelopes, envelope => Name(envelope) == "Exception" && Property(envelope, "blazor.report.kind") == NTBlazorReportKinds.Exception);
        Assert.Equal(PageTraceId, Tag(browserException, "ai.operation.id"));
        Assert.Equal("session-1", Tag(browserException, "ai.session.id"));
        Assert.Equal("user-7", Tag(browserException, "ai.user.authUserId"));
        Assert.Equal("TestBrowser/1.0", Tag(browserException, "ai.user.userAgent"));
        Assert.Equal("Browser render failed", BaseData(browserException).GetProperty("exceptions")[0].GetProperty("message").GetString());
        Assert.Equal("OrderPage", Property(browserException, "blazor.boundary.name"));
        Assert.Matches("^[0-9a-f]{16}$", Property(browserException, "blazor.error.fingerprint"));

        var stall = Assert.Single(envelopes, envelope => Name(envelope) == "Event" && BaseData(envelope).GetProperty("name").GetString() == "blazor.navigation.stalled");
        Assert.Equal(PageTraceId, Tag(stall, "ai.operation.id"));
        Assert.Equal("session-1", Tag(stall, "ai.session.id"));
        Assert.Equal(NTBlazorReportKinds.NavigationStalled, Property(stall, "blazor.report.kind"));

        var circuitEvent = Assert.Single(envelopes, envelope => Name(envelope) == "Event" && BaseData(envelope).GetProperty("name").GetString() == "blazor.circuit.connection_down");
        Assert.Equal("circuit-1", Property(circuitEvent, "aspnetcore.components.circuit.id"));

        var serverException = Assert.Single(envelopes, envelope => Name(envelope) == "Exception" && Property(envelope, "blazor.boundary.name") == "OrderPage" && Property(envelope, "blazor.report.kind") is null);
        Assert.Equal(typeof(InvalidOperationException).FullName, BaseData(serverException).GetProperty("exceptions")[0].GetProperty("typeName").GetString());
        Assert.Equal("circuit-1", Property(serverException, "aspnetcore.components.circuit.id"));
        Assert.Matches("^[0-9a-f]{16}$", Property(serverException, "blazor.error.fingerprint"));
    }

    private static NTBlazorErrorReport Report(string reportKind) => new() {
        BoundaryName = "OrderPage",
        ClientSessionId = "session-1",
        ExceptionMessage = "Browser render failed",
        ExceptionStackTrace = "at OrderPage.Render()",
        ExceptionType = "System.InvalidOperationException",
        OccurredAtUtc = DateTimeOffset.UtcNow,
        ReportKind = reportKind,
        TraceParent = $"00-{PageTraceId}-1234567890abcdef-01"
    };

    private static string? Name(JsonElement envelope) => envelope.GetProperty("name").GetString();

    private static JsonElement BaseData(JsonElement envelope) => envelope.GetProperty("data").GetProperty("baseData");

    private static string? Tag(JsonElement envelope, string name) => envelope.GetProperty("tags").TryGetProperty(name, out var value) ? value.GetString() : null;

    private static string? Property(JsonElement envelope, string name) => BaseData(envelope).TryGetProperty("properties", out var properties) && properties.TryGetProperty(name, out var value) ? value.GetString() : null;

    private sealed class CapturingHandler(List<JsonElement> envelopes) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var body = await request.Content!.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(request.Content.Headers.ContentEncoding.Contains("gzip") ? new GZipStream(body, CompressionMode.Decompress) : body);
            var lines = (await reader.ReadToEndAsync(cancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lock (envelopes) {
                envelopes.AddRange(lines.Select(line => JsonDocument.Parse(line).RootElement.Clone()));
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { itemsReceived = lines.Length, itemsAccepted = lines.Length, errors = Array.Empty<object>() }) };
        }
    }
}
