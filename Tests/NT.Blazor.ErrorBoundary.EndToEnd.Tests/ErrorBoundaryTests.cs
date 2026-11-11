using Microsoft.Playwright;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

public sealed class ErrorBoundaryTests(E2EFixture fixture) : E2ETestBase(fixture) {
    [Fact]
    public async Task StaticSsrComponentThrows_FallbackReferenceMatchesServerLog() {
        await OpenAsync("/ssr-error");

        await Assertions.Expect(Fallback.Locator(".nt-blazor-error-title")).ToHaveTextAsync("Something went wrong.");
        await Assertions.Expect(Fallback.GetByRole(AriaRole.Link, new() { Name = "Reload page" })).ToBeVisibleAsync();
        var reference = await ReadReferenceAsync();

        var log = await WaitForReferenceLogAsync(reference);
        Assert.Equal("SsrBoundary", log.FieldText("blazor.boundary.name"));
        Assert.Contains("ssr boom", log.Exception?.ToString());
    }

    [Fact]
    public async Task InteractiveServerComponentThrows_FallbackReferenceMatchesServerLog() {
        await OpenAsync("/server-error");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explode" }).ClickAsync();
        var reference = await ReadReferenceAsync();

        var log = await WaitForReferenceLogAsync(reference);
        Assert.Equal("ServerBoundary", log.FieldText("blazor.boundary.name"));
        Assert.Contains("server boom", log.Exception?.ToString());
    }

    [Fact]
    public async Task WebAssemblyComponentThrows_FallbackReferenceMatchesServerClientReportLog() {
        await OpenAsync("/wasm-error");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explode" }).ClickAsync();
        var reference = await ReadReferenceAsync();

        var log = await WaitForReferenceLogAsync(reference);
        Assert.Equal("NT.Blazor.ErrorBoundary.ClientReport", log.Category);
        Assert.StartsWith("Blazor client report", log.Message);
        Assert.Equal("WasmBoundary", log.FieldText("blazor.boundary.name"));
    }

    [Fact]
    public async Task WebAssemblyComponentThrows_ReportCarriesClientProjectVersion() {
        await OpenAsync("/wasm-error");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explode" }).ClickAsync();

        // The sample's WebAssembly project sets its own <Version>; the server's differs, so this proves the version is read inside the browser's runtime.
        var log = await WaitForReferenceLogAsync(await ReadReferenceAsync());
        Assert.StartsWith("2.0.0-client", log.FieldText("blazor.app.version"));
    }

    [Fact]
    public async Task WebAssemblyComponentThrowsInsideBoundary_NotAlsoReportedAsUnhandled() {
        await OpenAsync("/wasm-error");
        var explode = Page.GetByRole(AriaRole.Button, new() { Name = "Explode" });
        await explode.ClickAsync();
        var session = (await WaitForReferenceLogAsync(await ReadReferenceAsync())).FieldText("session.id");
        Assert.NotNull(session);

        // A second failure on the same runtime orders the check after any report the first failure could have produced.
        await Fallback.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await explode.ClickAsync();
        await WaitForReferenceLogAsync(await ReadReferenceAsync());

        Assert.False(Fixture.Exists(e => e.FieldText("session.id") == session && e.FieldText("blazor.exception.message") == "wasm boom" && e.FieldText("blazor.boundary.name") != "WasmBoundary"));
    }

    [Theory]
    [InlineData("/server-error")]
    [InlineData("/wasm-error")]
    public async Task InteractiveFallbackShown_TryAgainRecovers(string path) {
        await OpenAsync(path);
        var explode = Page.GetByRole(AriaRole.Button, new() { Name = "Explode" });
        await explode.ClickAsync();
        await Assertions.Expect(Fallback).ToBeVisibleAsync();

        await Fallback.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();

        await Assertions.Expect(Fallback).ToHaveCountAsync(0);
        await Assertions.Expect(explode).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/server-error")]
    [InlineData("/wasm-error")]
    public async Task InteractiveFallbackShown_NavigationRecovers(string path) {
        await OpenAsync(path);
        var explode = Page.GetByRole(AriaRole.Button, new() { Name = "Explode" });
        await explode.ClickAsync();
        await Assertions.Expect(Fallback).ToBeVisibleAsync();

        await Page.Locator("#same-page").ClickAsync();

        await Assertions.Expect(Fallback).ToHaveCountAsync(0);
        await Assertions.Expect(explode).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FallbackRendered_ScopedStylesFromCssBundleApply() {
        await OpenAsync("/ssr-error");

        await Assertions.Expect(Fallback).ToBeVisibleAsync();
        var style = await Fallback.EvaluateAsync<string[]>("e => { const s = getComputedStyle(e); return [s.backgroundColor, s.display]; }");

        Assert.Equal(["rgb(255, 244, 244)", "flex"], style);
    }
}
