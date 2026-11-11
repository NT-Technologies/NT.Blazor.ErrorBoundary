using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor.Exporting;
using NT.Blazor.ErrorBoundary.Models;
using System.Diagnostics;
using System.Text.Json;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor;

public sealed class NTBlazorAzureMonitorRegistrationTests {
    private const string ConnectionString = "InstrumentationKey=ikey;IngestionEndpoint=https://ingest.test/";

    [Fact]
    public async Task UnsetCloudRoleName_DefaultsToOtelServiceName() {
        await using var provider = CreateServices(new RecordingHandler(), settings: new() { ["OTEL_SERVICE_NAME"] = "configured-role" });

        Assert.Equal("configured-role", provider.GetRequiredService<IOptions<NTBlazorAzureMonitorOptions>>().Value.CloudRoleName);
    }

    [Fact]
    public async Task ConfiguredRole_ReachesEnvelopeCloudRoleTag() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler, settings: new() { ["OTEL_SERVICE_NAME"] = "configured-role" });

        await ExportAsync(provider);

        Assert.Equal("configured-role", Envelope(handler).GetProperty("tags").GetProperty("ai.cloud.role").GetString());
    }

    [Fact]
    public async Task ExplicitCloudRoleName_ReachesEnvelopeCloudRoleTagEvenWhenOtelServiceNameIsSet() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler, configure: options => options.CloudRoleName = "explicit-role", settings: new() { ["OTEL_SERVICE_NAME"] = "configured-role" });

        await ExportAsync(provider);

        Assert.Equal("explicit-role", Envelope(handler).GetProperty("tags").GetProperty("ai.cloud.role").GetString());
    }

    [Fact]
    public async Task NoRoleConfigured_UsesUnknownServiceWithProcessName() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler);

        await ExportAsync(provider);

        Assert.Equal($"unknown_service:{Process.GetCurrentProcess().ProcessName}", Envelope(handler).GetProperty("tags").GetProperty("ai.cloud.role").GetString());
    }

    [Fact]
    public async Task ExplicitConnectionString_WinsOverConfiguration() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler, configure: options => options.ConnectionString = "InstrumentationKey=explicit-key;IngestionEndpoint=https://explicit.test/");

        await ExportAsync(provider);

        Assert.Equal(new Uri("https://explicit.test/v2.1/track"), Assert.Single(handler.Requests).Uri);
        Assert.Equal("explicit-key", Envelope(handler).GetProperty("iKey").GetString());
    }

    [Fact]
    public async Task ConfigurationConnectionString_IsUsedWhenOptionUnset() {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler);

        await ExportAsync(provider);

        Assert.Equal(new Uri("https://ingest.test/v2.1/track"), Assert.Single(handler.Requests).Uri);
        Assert.Equal("ikey", Envelope(handler).GetProperty("iKey").GetString());
    }

    [Fact]
    public void RegisteredTwice_RegistersOneExporterOneEndpointExporterAndOneHostedService() {
        var services = new ServiceCollection();
        services.AddNTBlazorErrorBoundaryAzureMonitor().AddNTBlazorErrorBoundaryAzureMonitor();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(NTBlazorAzureMonitorExporter));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(INTBlazorErrorBoundaryExporter));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task RegisteredTwice_ResolvesTheSameExporterInstanceForEveryRole() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = ConnectionString }).Build());
        services.AddNTBlazorErrorBoundaryAzureMonitor().AddNTBlazorErrorBoundaryAzureMonitor();
        await using var provider = services.BuildServiceProvider();

        var exporter = Assert.Single(provider.GetServices<NTBlazorAzureMonitorExporter>());

        Assert.Same(exporter, Assert.Single(provider.GetServices<INTBlazorErrorBoundaryExporter>()));
        Assert.Same(exporter, Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public async Task SecondRegistration_StillAppliesItsConfiguration() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddNTBlazorErrorBoundaryAzureMonitor(options => options.ConnectionString = ConnectionString);
        services.AddNTBlazorErrorBoundaryAzureMonitor(options => options.CloudRoleName = "second-role");
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<NTBlazorAzureMonitorOptions>>().Value;

        Assert.Equal(ConnectionString, options.ConnectionString);
        Assert.Equal("second-role", options.CloudRoleName);
    }

    [Theory]
    [InlineData("InstrumentationKey=")]
    [InlineData("InstrumentationKey=;")]
    [InlineData("InstrumentationKey=  ;IngestionEndpoint=https://ingest.test/")]
    public async Task EmptyInstrumentationKey_FailsAtStartupWithDocumentedMessage(string connectionString) {
        await using var provider = CreateServices(new RecordingHandler(), connectionString: connectionString);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<NTBlazorAzureMonitorExporter>());

        Assert.Contains("needs an Application Insights connection string with an InstrumentationKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("APPLICATIONINSIGHTS_CONNECTION_STRING", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NTBlazorAzureMonitorOptions.ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("instrumentationkey=ikey;ingestionendpoint=https://i.test", "https://i.test/v2.1/track")]
    [InlineData(" InstrumentationKey = ikey ; IngestionEndpoint = https://i.test ;", "https://i.test/v2.1/track")]
    [InlineData("InstrumentationKey=ikey;IngestionEndpoint=https://i.test;", "https://i.test/v2.1/track")]
    [InlineData("InstrumentationKey=ikey;IngestionEndpoint=https://i.test/ai", "https://i.test/ai/v2.1/track")]
    [InlineData("InstrumentationKey=ikey;IngestionEndpoint=https://i.test/ai/", "https://i.test/ai/v2.1/track")]
    public async Task ConnectionStringVariants_AreAcceptedAndPostToTrackEndpoint(string connectionString, string expectedUri) {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler, connectionString: connectionString);

        await ExportAsync(provider);

        Assert.Equal(new Uri(expectedUri), Assert.Single(handler.Requests).Uri);
        Assert.Equal("ikey", Envelope(handler).GetProperty("iKey").GetString());
    }

    [Fact]
    public async Task NamedHttpClient_HasTenSecondTimeout() {
        await using var provider = CreateServices(new RecordingHandler());

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(NTBlazorAzureMonitorSender.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    private static async Task ExportAsync(ServiceProvider provider) {
        var exporter = provider.GetRequiredService<NTBlazorAzureMonitorExporter>();
        await exporter.StartAsync(TestContext.Current.CancellationToken);
        exporter.ExportEvent("test.event", DateTimeOffset.UtcNow, new Dictionary<string, object?>());
        await exporter.StopAsync(TestContext.Current.CancellationToken);
    }

    private static JsonElement Envelope(RecordingHandler handler) => JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement;

    private static ServiceProvider CreateServices(RecordingHandler handler, Action<NTBlazorAzureMonitorOptions>? configure = null, string? connectionString = ConnectionString, Dictionary<string, string?>? settings = null) {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(settings ?? []) { ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = connectionString }).Build());
        services.AddSingleton<TimeProvider>(new ImmediateTimeProvider());
        services.AddNTBlazorErrorBoundaryAzureMonitor(configure);
        services.AddHttpClient(NTBlazorAzureMonitorSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
}
