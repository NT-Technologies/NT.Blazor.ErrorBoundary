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
            RenderMode = "WebAssembly",
            Uri = "https://example.test/claims"
        };

        await reporter.ReportAsync(exception, context, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Request?.Method);
        Assert.Equal("https://example.test/api/errors", handler.Request?.RequestUri?.ToString());
        Assert.NotNull(handler.Content);

        var report = JsonSerializer.Deserialize<NTBlazorErrorReport>(handler.Content, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(report);
        Assert.Equal("Boundary", report.BoundaryName);
        Assert.Equal("client failure", report.ExceptionMessage);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.ExceptionType);
        Assert.Contains("client failure", report.ExceptionDetails, StringComparison.Ordinal);
        Assert.True(report.IsInteractive);
        Assert.Equal("WebAssembly", report.RenderMode);
        Assert.Equal("https://example.test/claims", report.Uri);
    }

    [Fact]
    public async Task ReportAsync_WhenBrowserBridgeAvailable_EnrichesAndSubmitsDurably() {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
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
                OriginUri = "https://example.test/Claims/Manage/64469",
                Uri = "https://example.test/Claims/Recovery/64469"
            },
            TestContext.Current.CancellationToken);

        Assert.Null(handler.Request);
        var report = Assert.IsType<NTBlazorErrorReport>(jsRuntime.SubmittedReport);
        Assert.Equal("4.13.0", report.ApplicationVersion);
        Assert.Equal("Claims.Management", report.BoundaryName);
        Assert.Equal("navigation-123", report.NavigationId);
        Assert.Equal("LocationChanging", report.NavigationPhase);
        Assert.Equal("https://example.test/Claims/Manage/64469", report.OriginUri);
        Assert.Equal("https://example.test/Claims/Recovery/64469", report.TargetUri);
        Assert.Single(report.Breadcrumbs);
    }

    [Fact]
    public async Task ReportAsync_WhenTelemetryPostFails_DoesNotThrow() {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var reporter = CreateReporter(handler);

        await reporter.ReportAsync(
            new InvalidOperationException("client failure"),
            new NTBlazorErrorBoundaryContext(),
            TestContext.Current.CancellationToken);
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

        var report = JsonSerializer.Deserialize<NTBlazorErrorReport>(handler.Content!, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(report);
        Assert.True(report.ExceptionMessage?.Length <= 64);
        Assert.True(report.BoundaryName?.Length <= 64);
        Assert.True(report.Uri?.Length <= 64);
        Assert.Contains("report field truncated", report.ExceptionMessage, StringComparison.Ordinal);
    }

    private static NTHttpClientBlazorErrorReporter CreateReporter(CapturingHandler handler, int? maxReportFieldLength = null, IJSRuntime? jsRuntime = null) {
        var httpClient = new HttpClient(handler) {
            BaseAddress = new Uri("https://example.test/")
        };

        return new NTHttpClientBlazorErrorReporter(
            httpClient,
            jsRuntime ?? new CapturingJsRuntime(),
            Options.Create(new NTBlazorErrorBoundaryHttpClientOptions {
                ReportUri = "/api/errors",
                MaxReportFieldLength = maxReportFieldLength ?? NTBlazorErrorBoundaryHttpClientOptions.DefaultMaxReportFieldLength
            }),
            NullLogger<NTHttpClientBlazorErrorReporter>.Instance);
    }

    private sealed class CapturingJsRuntime(NTBlazorClientContext? context = null) : IJSRuntime {
        public object? SubmittedReport { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) {
            if (identifier == "NTBlazorErrorBoundary.getContext" && context is not null) {
                return ValueTask.FromResult((TValue)(object)context);
            }

            if (identifier == "NTBlazorErrorBoundary.submitReport" && context is not null) {
                SubmittedReport = args?[0];
                return ValueTask.FromResult((TValue)(object)true);
            }

            throw new JSException("Browser bridge unavailable.");
        }
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler {
        public string? Content { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Request = request;
            Content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode);
        }
    }
}
