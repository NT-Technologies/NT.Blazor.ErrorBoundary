using Azure.Monitor.OpenTelemetry.AspNetCore;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor;
using NT.Blazor.ErrorBoundary.Sample.Components;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");

// Sends logs, traces, and metrics to Application Insights when a connection string is configured.
// AddNTBlazorErrorBoundaryServerLogging adds its own meter and activity source; Blazor's built-in telemetry is opted into here.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"])) {
    // Every Blazor error and event goes to Application Insights directly, as native exceptions and custom events, unaffected by the distro's sampling.
    builder.Services.AddNTBlazorErrorBoundaryAzureMonitor();
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .WithMetrics(metrics => metrics.AddMeter("Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Server.Circuits"))
        .WithTracing(tracing => tracing.AddSource("Microsoft.AspNetCore.Components"));
}

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(NT.Blazor.ErrorBoundary.Sample.Client.Components.BombButton).Assembly);

app.MapNTBlazorErrorBoundaryTelemetry();

app.Run();

/// <summary>Exposes the entry point to the end-to-end tests.</summary>
public partial class Program;
