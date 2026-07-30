using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NT.Blazor.ErrorBoundary.Components;

namespace NT.Blazor.ErrorBoundary.Tests.Components;

public sealed class NTBlazorInteractiveReadyTests : BunitContext {
    public NTBlazorInteractiveReadyTests() {
        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
    }

    [Fact]
    public void FirstInteractiveRender_MarksRouteReady() {
        JSInterop.SetupVoid("NTBlazorErrorBoundary.markInteractiveReady").SetVoidResult();

        Render<NTBlazorInteractiveReady>();

        JSInterop.VerifyInvoke("NTBlazorErrorBoundary.markInteractiveReady", 1);
    }

    [Fact]
    public void Navigation_RecordsLocationChangingContext() {
        JSInterop.SetupVoid("NTBlazorErrorBoundary.markInteractiveReady").SetVoidResult();
        JSInterop.SetupVoid("NTBlazorErrorBoundary.markLocationChanging", _ => true).SetVoidResult();
        Render<NTBlazorInteractiveReady>();
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        navigationManager.NavigateTo("/claims/recovery/64469");

        JSInterop.VerifyInvoke("NTBlazorErrorBoundary.markLocationChanging", 1);
    }
}
