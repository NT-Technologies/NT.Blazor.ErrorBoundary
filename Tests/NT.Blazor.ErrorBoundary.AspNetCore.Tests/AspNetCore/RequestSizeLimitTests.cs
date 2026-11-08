using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Net;
using System.Net.Http.Json;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

// TestServer does not enforce Kestrel body limits, so these tests run a real Kestrel host on a dynamic port.
public sealed class RequestSizeLimitTests {
    private const long Limit = 2_048;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBody_Returns413(bool chunked) {
        await using var app = await StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var response = await PostAsync(client, new string('x', 10_000), chunked);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyWithinLimit_Returns204(bool chunked) {
        await using var app = await StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var response = await PostAsync(client, "small", chunked);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string message, bool chunked) {
        var report = new NTBlazorErrorReport { ExceptionMessage = message, OccurredAtUtc = DateTimeOffset.UtcNow };
        var content = JsonContent.Create(report);
        if (chunked) {
            // Content without a computable length is sent chunked, so there is no Content-Length to check.
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors") { Content = new UnknownLengthContent(content) };
            request.Headers.TransferEncodingChunked = true;
            return client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        return client.PostAsync("/api/errors", content, TestContext.Current.CancellationToken);
    }

    private static async Task<WebApplication> StartAsync() {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");
        builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => options.MaxReportRequestBytes = Limit);
        var app = builder.Build();
        app.MapNTBlazorErrorBoundaryTelemetry();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class UnknownLengthContent : HttpContent {
        private readonly HttpContent _inner;

        public UnknownLengthContent(HttpContent inner) {
            _inner = inner;
            Headers.ContentType = inner.Headers.ContentType;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _inner.CopyToAsync(stream, context);

        protected override bool TryComputeLength(out long length) {
            length = -1;
            return false;
        }
    }
}
