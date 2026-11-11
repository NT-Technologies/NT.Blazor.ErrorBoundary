using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;
using System.Net;
using System.Net.Http.Headers;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

// Builds the registered services around a fake HTTP handler, and the helpers every exporter and sender test needs.
internal static class TestServices {
    internal const string ConnectionString = "InstrumentationKey=ikey;IngestionEndpoint=https://ingest.test/";

    internal static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body = "") => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    internal static Task<HttpResponseMessage> RespondRetryAfter(HttpStatusCode status, RetryConditionHeaderValue retryAfter) {
        var response = new HttpResponseMessage(status);
        response.Headers.RetryAfter = retryAfter;
        return Task.FromResult(response);
    }

    internal static CapturedLog[] Warnings(CapturingLoggerProvider logs) {
        lock (logs.Entries) {
            return [.. logs.Entries.Where(entry => entry.Level >= LogLevel.Warning)];
        }
    }

    internal static ServiceProvider CreateServices(HttpMessageHandler handler, TimeProvider? time = null, ILoggerProvider? logs = null, Action<NTBlazorAzureMonitorOptions>? configure = null, string? connectionString = ConnectionString, Dictionary<string, string?>? settings = null) {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs ?? new CapturingLoggerProvider()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(settings ?? []) { ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = connectionString }).Build());
        services.AddSingleton(time ?? new ImmediateTimeProvider());
        services.AddNTBlazorErrorBoundaryAzureMonitor(configure);
        services.AddHttpClient(NTBlazorAzureMonitorSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
}
