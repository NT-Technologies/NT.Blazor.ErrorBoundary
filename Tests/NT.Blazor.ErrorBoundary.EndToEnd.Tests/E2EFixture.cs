using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(NT.Blazor.ErrorBoundary.EndToEnd.Tests.E2EFixture))]

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

/// <summary>
/// Hosts the sample app on real Kestrel at a dynamic port and shares one Chromium instance across the assembly.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime {
    private static readonly TimeSpan LogTimeout = TimeSpan.FromSeconds(30);

    private readonly CapturingLoggerProvider _logs = new();
    private SampleApplicationFactory? _factory;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _browserError;

    internal string BaseUrl { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync() {
        _factory = new SampleApplicationFactory(_logs);
        _factory.UseKestrel(0);
        _factory.StartServer();
        BaseUrl = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');

        try {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync();
        }
        catch (Exception exception) {
            _browserError = exception.Message;
        }
    }

    public async ValueTask DisposeAsync() {
        if (_browser is not null) {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        if (_factory is not null) {
            await _factory.DisposeAsync();
        }
    }

    internal async Task<IBrowserContext> NewContextAsync() {
        if (_browser is null) {
            var message = "Playwright Chromium is not available. Install it with: pwsh Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests/bin/<Configuration>/net10.0/playwright.ps1 install chromium. Details: " + _browserError;
            if (string.Equals(Environment.GetEnvironmentVariable("NT_E2E_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase)) {
                Assert.Fail(message);
            }

            Assert.Skip(message);
        }

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });
        context.SetDefaultTimeout(60_000);
        return context;
    }

    /// <summary>
    /// Polls the captured server logs until a matching entry appears or the timeout elapses.
    /// </summary>
    internal async Task<CapturedLog> WaitForLogAsync(Func<CapturedLog, bool> predicate, string description) {
        using var timeout = new CancellationTokenSource(LogTimeout);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        do {
            if (_logs.Entries.FirstOrDefault(predicate) is { } entry) {
                return entry;
            }
        }
        while (await WaitAsync(timer, timeout.Token));

        var captured = string.Join(" | ", _logs.Entries.Where(IsInteresting).Select(e => $"[{e.Category}] {e.Message} ref={e.FieldText("blazor.error.reference_id")} kind={e.FieldText("blazor.report.kind")}"));
        throw new TimeoutException($"No server log matched: {description}. Captured: {captured}");
    }

    internal bool Exists(Func<CapturedLog, bool> predicate) => _logs.Entries.Any(predicate);

    internal int Count(Func<CapturedLog, bool> predicate) => _logs.Entries.Count(predicate);

    private static bool IsInteresting(CapturedLog entry) => entry.Level >= LogLevel.Error || entry.Fields.ContainsKey("blazor.error.reference_id");

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken) {
        try {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            return false;
        }
    }
}
