using Microsoft.Extensions.DependencyInjection;
using NT.Blazor.ErrorBoundary.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Blazor error boundary services for client-side reporting.
/// </summary>
public static class NTBlazorErrorBoundaryServiceCollectionExtensions {
    /// <summary>
    /// Adds an HTTP reporter for errors caught by <see cref="Components.NTErrorBoundary"/>.
    /// </summary>
    public static IHttpClientBuilder AddNTBlazorErrorBoundary(this IServiceCollection services, string reportUri, string? environmentName = null) {

        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportUri);

        services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
            options.ReportUri = reportUri;
        });
        services.Configure<NTErrorBoundaryOptions>(options => {
            options.IsDevelopmentEnvironment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        });
        return services.AddHttpClient<INTBlazorErrorReporter, NTHttpClientBlazorErrorReporter>();
    }
}
