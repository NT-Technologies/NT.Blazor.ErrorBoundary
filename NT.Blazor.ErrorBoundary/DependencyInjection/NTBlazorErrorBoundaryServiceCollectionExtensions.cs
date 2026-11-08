using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Blazor error boundary services for client-side reporting.
/// </summary>
public static class NTBlazorErrorBoundaryServiceCollectionExtensions {
    /// <summary>
    /// Adds an HTTP reporter for errors caught by <see cref="NT.Blazor.ErrorBoundary.Components.NTErrorBoundary"/>
    /// and for unhandled component exceptions logged by the Blazor renderer.
    /// </summary>
    public static IHttpClientBuilder AddNTBlazorErrorBoundaryHttpClient(this IServiceCollection services, string reportUri, string? environmentName = null) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportUri);

        services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
            options.ReportUri = reportUri;
        });
        services.Configure<NTErrorBoundaryOptions>(options => {
            options.IsDevelopmentEnvironment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, NTBlazorUnhandledExceptionLoggerProvider>());
        return services.AddHttpClient<INTBlazorErrorReporter, NTHttpClientBlazorErrorReporter>();
    }
}
