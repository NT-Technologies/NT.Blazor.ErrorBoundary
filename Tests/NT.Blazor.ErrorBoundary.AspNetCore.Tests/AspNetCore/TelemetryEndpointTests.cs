using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Net;
using System.Net.Http.Json;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class TelemetryEndpointTests {
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidReport_Returns204AndLogsReferenceAndPageTraceParent() {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(host.ClientEntries);
        Assert.Equal("ref-1", entry.ScopeValues["BlazorErrorReferenceId"]);
        Assert.Equal("00-1234567890abcdef1234567890abcdef-1234567890abcdef-01", entry.ScopeValues["BlazorPageTraceParent"]);
    }

    [Fact]
    public async Task UnknownReportKind_Returns400WithoutLogging() {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(reportKind: "Made-Up"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.ClientEntries);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-8 * 24 * 60)]
    public async Task ImplausibleTimestamp_Returns400WithoutLogging(int offsetMinutes) {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(occurredAt: Now.AddMinutes(offsetMinutes)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.ClientEntries);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-6 * 24 * 60)]
    public async Task PlausibleTimestamp_Returns204(int offsetMinutes) {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(occurredAt: Now.AddMinutes(offsetMinutes)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task FilterReturnsNull_Returns204WithoutLogging() {
        await using var host = await StartAsync(options => options.FilterReport = _ => null);

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(host.ClientEntries);
    }

    [Fact]
    public async Task FilterThrows_Returns204AndLogsOnlyWarning() {
        await using var host = await StartAsync(options => options.FilterReport = _ => throw new InvalidOperationException("filter bug"));

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(host.ClientEntries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("filter bug", entry.Exception?.Message);
    }

    [Fact]
    public async Task FilterReturnsCopy_LogsFilteredReport() {
        await using var host = await StartAsync(options => options.FilterReport = report => new NTBlazorErrorReport { ExceptionMessage = "redacted", ReportKind = report.ReportKind, OccurredAtUtc = report.OccurredAtUtc });

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(host.ClientEntries);
        Assert.Equal("redacted", entry.ScopeValues["BlazorExceptionMessage"]);
    }

    [Fact]
    public async Task LoggedException_CarriesOriginalMessageStackAndDetails() {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/errors", Report(), TestContext.Current.CancellationToken);

        var exception = Assert.Single(host.ClientEntries).Exception!;
        Assert.Equal("original message", exception.Message);
        Assert.Equal("at Original.Method()", exception.StackTrace);
        Assert.Equal("System.Exception: original message\n at Original.Method()", exception.ToString());
    }

    [Fact]
    public void ClientException_WithoutDetails_FallsBackToTypeMessageAndStack() {
        var exception = new ClientBlazorErrorReportException(new NTBlazorErrorReport { ExceptionType = "T", ExceptionMessage = "m", ExceptionStackTrace = "s" });

        Assert.Equal($"T: m{Environment.NewLine}s", exception.ToString());
    }

    [Fact]
    public void ClientException_WithoutAnyFields_UsesReportKindMessage() {
        var exception = new ClientBlazorErrorReportException(new NTBlazorErrorReport());

        Assert.Contains("Exception", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.StackTrace);
    }

    private static NTBlazorErrorReport Report(string reportKind = NTBlazorReportKinds.Exception, DateTimeOffset? occurredAt = null) => new() {
        BoundaryName = "Boundary",
        ExceptionDetails = "System.Exception: original message\n at Original.Method()",
        ExceptionMessage = "original message",
        ExceptionStackTrace = "at Original.Method()",
        ExceptionType = "System.Exception",
        OccurredAtUtc = occurredAt ?? Now,
        ReferenceId = "ref-1",
        ReportKind = reportKind,
        TraceParent = "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01"
    };

    private static async Task<TestHost> StartAsync(Action<NTBlazorErrorBoundaryHttpClientOptions>? configure = null) {
        var logs = new TestLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        if (configure is not null) {
            builder.Services.Configure(configure);
        }

        var app = builder.Build();
        app.MapNTBlazorErrorBoundaryTelemetry();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new TestHost(app, app.GetTestClient(), logs);
    }

    private sealed class TestHost(WebApplication app, HttpClient client, TestLoggerProvider logs) : IAsyncDisposable {
        public HttpClient Client { get; } = client;

        public IReadOnlyList<TestLogEntry> ClientEntries => logs.Entries.Where(entry => entry.CategoryName == "NT.Blazor.ErrorBoundary.ClientReport").ToList();

        public async ValueTask DisposeAsync() {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
