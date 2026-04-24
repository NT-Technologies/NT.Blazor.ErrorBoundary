using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.Tests.Services;

public sealed class NTBlazorErrorBoundaryServiceCollectionExtensionsTests {
    [Fact]
    public void AddNTBlazorErrorBoundary_WhenReportUriIsBlank_Throws() {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddNTBlazorErrorBoundary(" "));
    }

    [Fact]
    public void AddNTBlazorErrorBoundary_ConfiguresRequiredReportUri() {
        var services = new ServiceCollection();

        services.AddNTBlazorErrorBoundary("/api/errors");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NTBlazorErrorBoundaryHttpClientOptions>>();

        Assert.Equal("/api/errors", options.Value.ReportUri);
    }

    [Fact]
    public void AddNTBlazorErrorBoundary_WhenEnvironmentIsDevelopment_ConfiguresDevelopmentExceptionDetails() {
        var services = new ServiceCollection();

        services.AddNTBlazorErrorBoundary("/api/errors", "Development");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NTErrorBoundaryOptions>>();

        Assert.True(options.Value.IsDevelopmentEnvironment);
    }
}
