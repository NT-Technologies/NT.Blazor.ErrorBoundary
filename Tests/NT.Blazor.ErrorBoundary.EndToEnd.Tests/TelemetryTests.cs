using Microsoft.Playwright;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

public sealed class TelemetryTests(E2EFixture fixture) : E2ETestBase(fixture) {
    [Fact]
    public async Task WebAssemblyExceptionOutsideBoundary_LoggedServerSideAsClientReport() {
        await OpenAsync("/wasm-unhandled");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explode" }).ClickAsync();

        var log = await Fixture.WaitForLogAsync(e => e.Category == "NT.Blazor.ErrorBoundary.ClientReport" && e.ScopeText("BlazorExceptionMessage")?.Contains("wasm unhandled boom") == true, "unhandled WebAssembly exception");
        Assert.StartsWith("Blazor client report", log.Message);
        await Assertions.Expect(Fallback).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task JavaScriptErrorThrown_ReportedToServer() {
        await OpenAsync("/");

        await Page.Locator("#js-error").ClickAsync();

        var log = await Fixture.WaitForLogAsync(e => e.ScopeText("BlazorReportKind") == "JavaScriptError" && e.ScopeText("BlazorExceptionMessage")?.Contains("sample javascript failure") == true, "JavaScriptError report");
        Assert.Equal("NT.Blazor.ErrorBoundary.ClientReport", log.Category);
    }

    [Fact]
    public async Task LinkToPageWithoutInteractiveReady_NavigationStallReported() {
        await OpenAsync("/stall");
        var session = await ReadSessionIdAsync();

        await Page.Locator("#stall-link").ClickAsync();

        await WaitForStallAsync(session, "/stall-target");
    }

    [Fact]
    public async Task LinkToPageWithInteractiveReady_NoStallReported() {
        await OpenAsync("/stall");
        var session = await ReadSessionIdAsync();
        await Page.Locator("#ready-link").ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Ready target" })).ToBeVisibleAsync();

        // The stall timer would have fired for the marked navigation by the time this later stall is reported.
        await Page.GotoAsync("/stall");
        await Page.Locator("#stall-link").ClickAsync();
        await WaitForStallAsync(session, "/stall-target");

        Assert.False(Fixture.Exists(e => IsStall(e, session, "/ready-target")));
    }

    private Task<CapturedLog> WaitForStallAsync(string session, string target) => Fixture.WaitForLogAsync(e => IsStall(e, session, target), "NavigationStalled for " + target);

    private static bool IsStall(CapturedLog entry, string session, string target) =>
        entry.ScopeText("BlazorReportKind") == "NavigationStalled"
        && entry.ScopeText("BlazorClientSessionId") == session
        && entry.ScopeText("BlazorTargetUri")?.EndsWith(target, StringComparison.Ordinal) == true;

    private async Task<string> ReadSessionIdAsync() {
        // The telemetry script creates the session id when it loads.
        await Page.WaitForFunctionAsync("() => !!sessionStorage.getItem('nt.blazor.error-boundary.session')");
        return await Page.EvaluateAsync<string>("() => sessionStorage.getItem('nt.blazor.error-boundary.session')");
    }
}
