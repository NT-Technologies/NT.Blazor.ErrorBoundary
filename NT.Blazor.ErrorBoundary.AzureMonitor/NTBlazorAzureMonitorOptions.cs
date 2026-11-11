using Azure.Core;

namespace NT.Blazor.ErrorBoundary.AzureMonitor;

/// <summary>
/// Configures how browser exceptions are sent to Application Insights.
/// </summary>
public sealed class NTBlazorAzureMonitorOptions {
    /// <summary>
    /// Gets or sets the Application Insights connection string. Defaults to the <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> setting, which the Azure Monitor distro also reads.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the credential used for Microsoft Entra authentication. Required when the Application Insights resource disables local authentication; null sends with the connection string's instrumentation key only.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the cloud role name stamped on each exception. Defaults to the <c>OTEL_SERVICE_NAME</c> setting, then OpenTelemetry's <c>unknown_service:&lt;process name&gt;</c>, matching the Azure Monitor distro so browser exceptions sit under the same role as the host's other telemetry.
    /// </summary>
    public string? CloudRoleName { get; set; }
}
