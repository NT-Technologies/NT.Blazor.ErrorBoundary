using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services
    .AddNTBlazorErrorBoundaryHttpClient("/api/blazor-errors", builder.HostEnvironment.Environment)
    .ConfigureHttpClient(client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
