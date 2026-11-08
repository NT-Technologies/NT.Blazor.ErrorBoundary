using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.Sample.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");

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
