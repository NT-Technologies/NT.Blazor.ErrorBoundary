using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

public abstract class E2ETestBase(E2EFixture _fixture) : IAsyncLifetime {
    private IBrowserContext? _context;

    internal E2EFixture Fixture => _fixture;

    protected IPage Page { get; private set; } = null!;

    protected ILocator Fallback => Page.Locator("section.nt-blazor-error-boundary[role=alert]");

    protected ILocator Reference => Fallback.Locator(".nt-blazor-error-reference span");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() {
        if (_context is not null) {
            await _context.CloseAsync();
        }
    }

    /// <summary>Opens a fresh browser context and page, skipping the test when Chromium is unavailable.</summary>
    protected async Task OpenAsync(string path) {
        _context = await _fixture.NewContextAsync();
        Page = await _context.NewPageAsync();
        await Page.GotoAsync(path);
    }

    protected async Task<string> ReadReferenceAsync() {
        await Assertions.Expect(Reference).ToHaveTextAsync(new Regex("^[0-9A-Z]{8}$"));
        return (await Reference.TextContentAsync())!;
    }

    internal Task<CapturedLog> WaitForReferenceLogAsync(string reference) => _fixture.WaitForLogAsync(e => e.ScopeText("BlazorErrorReferenceId") == reference, "reference " + reference);
}
