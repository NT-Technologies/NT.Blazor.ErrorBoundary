using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

internal sealed class SampleApplicationFactory(CapturingLoggerProvider _logs) : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(_logs));
    }
}
