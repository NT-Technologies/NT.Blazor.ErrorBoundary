using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Net;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.Tests.Services;

public sealed class NTHttpClientBlazorErrorReporterTests {
    [Fact]
    public async Task ReportAsync_PostsExpectedReport() {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var reporter = CreateReporter(handler);
        var exception = new InvalidOperationException("client failure");
        var context = new NTBlazorErrorBoundaryContext {
            BoundaryName = "Boundary",
            IsInteractive = true,
            OriginUri = "https://example.test/reset?token=secret#step",
            RenderMode = "WebAssembly",
            Uri = "https://example.test/claims#section"
        };

        await reporter.ReportAsync(exception, context, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Request?.Method);
        Assert.Equal("https://example.test/api/errors", handler.Request?.RequestUri?.ToString());

        var report = ReadReport(handler);
        Assert.Equal("Boundary", report.BoundaryName);
        Assert.Equal("client failure", report.ExceptionMessage);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.ExceptionType);
        Assert.Contains("client failure", report.ExceptionDetails, StringComparison.Ordinal);
        Assert.True(report.IsInteractive);
        Assert.Equal("WebAssembly", report.RenderMode);
        Assert.Equal("https://example.test/reset", report.OriginUri);
        Assert.Equal("https://example.test/claims", report.Uri);
    }

    [Fact]
    public async Task ReportAsync_WhenBrowserBridgeAvailable_EnrichesAndPostsReport() {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var jsRuntime = new CapturingJsRuntime(new NTBlazorClientContext {
            ApplicationVersion = "4.13.0",
            Breadcrumbs = [
                new NTBlazorBreadcrumb {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    Phase = "LocationChanging",
                    TargetUri = "https://example.test/Claims/Recovery/64469",
                    Uri = "https://example.test/Claims/Manage/64469"
                }
            ],
            ClientSessionId = "session-123",
            CurrentUri = "https://example.test/Claims/Recovery/64469",
            IsNavigationIntercepted = true,
            IsOnline = true,
            NavigationId = "navigation-123",
            NavigationPhase = "LocationChanging",
            OriginUri = "https://example.test/Claims/Manage/64469",
            TargetUri = "https://example.test/Claims/Recovery/64469"
        });
        var reporter = CreateReporter(handler, jsRuntime: jsRuntime);

        await reporter.ReportAsync(
            new InvalidOperationException("client failure"),
            new NTBlazorErrorBoundaryContext {
                BoundaryName = "Claims.Management",
                ReferenceId = "7F3K2QAB",
                OriginUri = "https://example.test/Claims/Manage/64469",
                Uri = "https://example.test/Claims/Recovery/64469"
            },
            TestContext.Current.CancellationToken);

        Assert.Null(jsRuntime.QueuedReport);
        var report = ReadReport(handler);
        Assert.Equal("4.13.0", report.ApplicationVersion);
        Assert.Equal("Claims.Management", report.BoundaryName);
        Assert.Equal("7F3K2QAB", report.ReferenceId);
        Assert.Equal("navigation-123", report.NavigationId);
        Assert.Equal("LocationChanging", report.NavigationPhase);
        Assert.Equal("https://example.test/Claims/Manage/64469", report.OriginUri);
        Assert.Equal("https://example.test/Claims/Recovery/64469", report.TargetUri);
        Assert.Single(report.Breadcrumbs);
    }

    [Fact]
    public async Task ReportAsync_WhenReportHasTraceParent_SendsTraceParentHeader() {
        const string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var reporter = CreateReporter(handler, jsRuntime: new CapturingJsRuntime(new NTBlazorClientContext { TraceParent = traceParent }));

        await reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Equal(traceParent, Assert.Single(handler.Request!.Headers.GetValues("traceparent")));
    }

    [Fact]
    public async Task ReportAsync_WhenFilterReturnsNull_DoesNotSend() {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var jsRuntime = new CapturingJsRuntime();
        var reporter = CreateReporter(handler, jsRuntime: jsRuntime, filterReport: _ => null);

        await reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Null(handler.Request);
        Assert.Null(jsRuntime.QueuedReport);
    }

    [Fact]
    public async Task ReportAsync_WhenFilterReplacesReport_PostsFilteredReport() {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var reporter = CreateReporter(handler, filterReport: report => report.WithTruncatedFields(8));

        await reporter.ReportAsync(new InvalidOperationException(new string('m', 100)), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.True(ReadReport(handler).ExceptionMessage?.Length <= 8);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReportAsync_WhenServerIsTransientlyUnavailable_QueuesReportInBrowser(HttpStatusCode statusCode) {
        var jsRuntime = new CapturingJsRuntime();
        var reporter = CreateReporter(new CapturingHandler(statusCode), jsRuntime: jsRuntime);

        await reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Equal("client failure", Assert.IsType<NTBlazorErrorReport>(jsRuntime.QueuedReport).ExceptionMessage);
    }

    [Fact]
    public async Task ReportAsync_WhenPostThrows_QueuesReportInBrowser() {
        var jsRuntime = new CapturingJsRuntime();
        var reporter = CreateReporter(new CapturingHandler(HttpStatusCode.NoContent, new HttpRequestException("offline")), jsRuntime: jsRuntime);

        await reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(jsRuntime.QueuedReport);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    public async Task ReportAsync_WhenServerRejectsReport_DoesNotQueue(HttpStatusCode statusCode) {
        var jsRuntime = new CapturingJsRuntime();
        var reporter = CreateReporter(new CapturingHandler(statusCode), jsRuntime: jsRuntime);

        await reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);

        Assert.Null(jsRuntime.QueuedReport);
    }

    [Fact]
    public async Task ReportAsync_WhenPostFailsAndBrowserUnavailable_DoesNotThrow() {
        var reporter = CreateReporter(new CapturingHandler(HttpStatusCode.InternalServerError));

        await reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportAsync_WhenCallerCancels_Throws() {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var reporter = CreateReporter(new CapturingHandler(HttpStatusCode.NoContent));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reporter.ReportAsync(new InvalidOperationException("client failure"), new NTBlazorErrorBoundaryContext(), cancellation.Token));
    }

    [Fact]
    public async Task ReportAsync_WhenReportFieldsExceedMaximum_TruncatesReportFields() {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var reporter = CreateReporter(handler, maxReportFieldLength: 64);
        var exception = new InvalidOperationException(new string('m', 200));
        var context = new NTBlazorErrorBoundaryContext {
            BoundaryName = new string('b', 200),
            Uri = $"https://example.test/{new string('u', 200)}"
        };

        await reporter.ReportAsync(exception, context, TestContext.Current.CancellationToken);

        var report = ReadReport(handler);
        Assert.True(report.ExceptionMessage?.Length <= 64);
        Assert.True(report.BoundaryName?.Length <= 64);
        Assert.True(report.Uri?.Length <= 64);
        Assert.Contains("report field truncated", report.ExceptionMessage, StringComparison.Ordinal);
    }

    private static NTBlazorErrorReport ReadReport(CapturingHandler handler) {
        var report = JsonSerializer.Deserialize<NTBlazorErrorReport>(handler.Content!, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });
        return Assert.IsType<NTBlazorErrorReport>(report);
    }

    private static NTHttpClientBlazorErrorReporter CreateReporter(CapturingHandler handler, int? maxReportFieldLength = null, IJSRuntime? jsRuntime = null, Func<NTBlazorErrorReport, NTBlazorErrorReport?>? filterReport = null) {
        var httpClient = new HttpClient(handler) {
            BaseAddress = new Uri("https://example.test/")
        };

        return new NTHttpClientBlazorErrorReporter(
            httpClient,
            jsRuntime ?? new CapturingJsRuntime(),
            Options.Create(new NTBlazorErrorBoundaryHttpClientOptions {
                FilterReport = filterReport,
                ReportUri = "/api/errors",
                MaxReportFieldLength = maxReportFieldLength ?? NTBlazorErrorBoundaryHttpClientOptions.DefaultMaxReportFieldLength
            }),
            NullLogger<NTHttpClientBlazorErrorReporter>.Instance);
    }

    // BL0016 (.NET 11) guards interop call sites; this fake is the callee, so its overload forwarding has nothing to guard.
#pragma warning disable BL0016
    private sealed class CapturingJsRuntime(NTBlazorClientContext? context = null) : IJSRuntime {
        public object? QueuedReport { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) {
            if (identifier == "NTBlazorErrorBoundary.getContext" && context is not null) {
                return ValueTask.FromResult((TValue)(object)context);
            }

            if (identifier == "NTBlazorErrorBoundary.queueReport" && context is null) {
                QueuedReport = args?[0];
                return ValueTask.FromResult(default(TValue)!);
            }

            throw new JSException("Browser bridge unavailable.");
        }
    }
#pragma warning restore BL0016

    private sealed class CapturingHandler(HttpStatusCode statusCode, Exception? failure = null) : HttpMessageHandler {
        public string? Content { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Request = request;
            if (failure is not null) {
                throw failure;
            }

            Content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode);
        }
    }
}
