using Microsoft.Playwright;

namespace NT.Blazor.ErrorBoundary.EndToEnd.Tests;

public sealed class TelemetryTests(E2EFixture fixture) : E2ETestBase(fixture) {
    [Fact]
    public async Task WebAssemblyExceptionOutsideBoundary_LoggedServerSideAsClientReport() {
        await OpenAsync("/wasm-unhandled");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explode" }).ClickAsync();

        var log = await Fixture.WaitForLogAsync(e => e.Category == "NT.Blazor.ErrorBoundary.ClientReport" && e.FieldText("blazor.exception.message")?.Contains("wasm unhandled boom") == true, "unhandled WebAssembly exception");
        Assert.StartsWith("Blazor client report", log.Message);
        await Assertions.Expect(Fallback).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task JavaScriptErrorThrown_ReportedToServer() {
        await OpenAsync("/");

        await Page.Locator("#js-error").ClickAsync();

        var log = await Fixture.WaitForLogAsync(e => e.FieldText("blazor.report.kind") == "JavaScriptError" && e.FieldText("blazor.exception.message")?.Contains("sample javascript failure") == true, "JavaScriptError report");
        Assert.Equal("NT.Blazor.ErrorBoundary.ClientReport", log.Category);
    }

    [Fact]
    public async Task JavaScriptErrorThenImmediateNavigation_ReportedOnce() {
        await OpenAsync("/");

        await Page.Locator("#js-error").ClickAsync();
        // Navigating while the upload is in flight makes the old page see it fail; it must not be queued and resent by the next page.
        await Page.GotoAsync("/stall");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var reference = await Page.EvaluateAsync<string?>("() => JSON.parse(sessionStorage.getItem('nt.blazor.error-boundary.sent') ?? '[]').at(-1) ?? null");

        Assert.NotNull(reference);
        await WaitForReferenceLogAsync(reference);
        Assert.Equal(1, Fixture.Count(e => e.FieldText("blazor.error.reference_id") == reference));
        Assert.Equal("[]", await Page.EvaluateAsync<string>("() => sessionStorage.getItem('nt.blazor.error-boundary.queue') ?? '[]'"));
    }

    [Fact]
    public async Task WebAssemblyErrorThenImmediateNavigation_ReportedOnce() {
        await OpenAsync("/wasm-error");
        var held = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Holding the upload in the browser until the page has gone puts it in flight across a full navigation, which shuts down the .NET runtime that sent it.
        await Page.RouteAsync("**/api/blazor-errors", route => {
            held.TrySetResult(route);
            return Task.CompletedTask;
        });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explode" }).ClickAsync();
        var reference = await ReadReferenceAsync();
        var upload = await held.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await Page.GotoAsync("/stall");
        await upload.ContinueAsync();

        await WaitForReferenceLogAsync(reference);
        Assert.Equal(1, Fixture.Count(e => e.FieldText("blazor.error.reference_id") == reference));
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
        entry.FieldText("blazor.report.kind") == "NavigationStalled"
        && entry.FieldText("session.id") == session
        && entry.FieldText("blazor.navigation.target_url")?.EndsWith(target, StringComparison.Ordinal) == true;

    private async Task<string> ReadSessionIdAsync() {
        // The telemetry script creates the session id when it loads.
        await Page.WaitForFunctionAsync("() => !!sessionStorage.getItem('nt.blazor.error-boundary.session')");
        return await Page.EvaluateAsync<string>("() => sessionStorage.getItem('nt.blazor.error-boundary.session')");
    }
}
