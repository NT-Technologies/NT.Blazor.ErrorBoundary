using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;
using OpenTelemetry.Logs;

namespace NT.Blazor.ErrorBoundary.AzureMonitor;

/// <summary>
/// Registers the Application Insights exporter for NT.Blazor.ErrorBoundary errors and events.
/// </summary>
public static class NTBlazorErrorBoundaryAzureMonitorExtensions {
    // The categories NT.Blazor.ErrorBoundary.AspNetCore logs errors and events under: browser reports, server-rendered errors, and circuit events.
    private static readonly string[] LogCategories = ["NT.Blazor.ErrorBoundary.ClientReport", "NT.Blazor.ErrorBoundary.AspNetCore"];

    /// <summary>
    /// Sends every error and event NT.Blazor.ErrorBoundary reports to Application Insights directly: server-rendered and browser exceptions as native exceptions,
    /// with their original type, parsed stack, and a problem ID the Failures view groups by, and stalled navigation, failed reconnection, and circuit connection
    /// changes as custom events. Direct delivery is not subject to the host's trace sampling. The same log entries are kept out of the OpenTelemetry log pipeline,
    /// so Application Insights records each once; other logging providers, such as the console, still receive them.
    /// </summary>
    public static IServiceCollection AddNTBlazorErrorBoundaryAzureMonitor(this IServiceCollection services, Action<NTBlazorAzureMonitorOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null) {
            services.Configure(configure);
        }

        // A second call only adds configuration: one exporter and one send loop, whose queue supports a single reader.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(NTBlazorAzureMonitorExporter))) {
            return services;
        }

        // Defaults fill only what the host left unset, whichever order the registrations ran in.
        services.AddOptions<NTBlazorAzureMonitorOptions>().PostConfigure<IConfiguration>((options, configuration) => {
            options.ConnectionString ??= configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            options.CloudRoleName ??= configuration["OTEL_SERVICE_NAME"];
        });
        services.Configure<LoggerFilterOptions>(options => {
            foreach (var category in LogCategories) {
                options.AddFilter<OpenTelemetryLoggerProvider>(category, LogLevel.None);
            }
        });
        // The default per-request HttpClient logs would flow back into the app's own logs and Application Insights; failures are logged by the exporter instead.
        // The short timeout keeps one unresponsive attempt from holding the single send loop for HttpClient's 100-second default.
        services.AddHttpClient(NTBlazorAzureMonitorSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10)).RemoveAllLoggers();
        services.AddSingleton<NTBlazorAzureMonitorExporter>();
        services.AddSingleton<INTBlazorErrorBoundaryExporter>(provider => provider.GetRequiredService<NTBlazorAzureMonitorExporter>());
        services.AddHostedService(provider => provider.GetRequiredService<NTBlazorAzureMonitorExporter>());
        return services;
    }
}
