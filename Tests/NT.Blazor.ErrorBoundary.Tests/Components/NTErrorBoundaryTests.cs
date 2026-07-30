using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Components;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.Tests.Components;

public sealed class NTErrorBoundaryTests : BunitContext {
    private readonly NTErrorBoundaryOptions _options = new();
    private readonly CapturingBlazorErrorReporter _reporter = new();

    public NTErrorBoundaryTests() {
        Services.AddSingleton(Options.Create(_options));
        Services.AddSingleton<INTBlazorErrorReporter>(_reporter);
        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
    }

    [Fact]
    public void ChildThrows_ReportsExceptionWithBoundaryContext() {
        RenderBoundary();

        Assert.Same(ThrowingComponent.Exception, _reporter.Exception);
        Assert.NotNull(_reporter.Context);
        Assert.Equal(typeof(NTErrorBoundary).FullName, _reporter.Context.BoundaryName);
        Assert.Equal(Services.GetRequiredService<NavigationManager>().Uri, _reporter.Context.OriginUri);
        Assert.Equal("WebAssembly", _reporter.Context.RenderMode);
        Assert.True(_reporter.Context.IsInteractive);
        Assert.Equal(Services.GetRequiredService<NavigationManager>().Uri, _reporter.Context.Uri);
    }

    [Fact]
    public void ChildThrows_WithName_ReportsOwningComponentName() {
        RenderBoundary(name: "Claims.Management");

        Assert.NotNull(_reporter.Context);
        Assert.Equal("Claims.Management", _reporter.Context.BoundaryName);
    }

    [Fact]
    public void ChildThrows_RendersGenericFallbackWithoutExceptionDetails() {
        var cut = RenderBoundary();

        Assert.Contains("Something went wrong.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Refresh the page or try again.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(ThrowingComponent.Exception.Message, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenExceptionDetailsAlways_RendersExceptionDetails() {
        var cut = RenderBoundary(ExceptionDetailsMode.Always);

        Assert.Contains("Exception details", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(ThrowingComponent.Exception.Message, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenDevelopmentEnvironment_RendersExceptionDetailsByDefault() {
        _options.IsDevelopmentEnvironment = true;

        var cut = RenderBoundary();

        Assert.Contains("Exception details", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(ThrowingComponent.Exception.Message, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenDevelopmentEnvironmentAndExceptionDetailsDisabled_RendersGenericFallback() {
        _options.IsDevelopmentEnvironment = true;

        var cut = RenderBoundary(ExceptionDetailsMode.Never);

        Assert.DoesNotContain(ThrowingComponent.Exception.Message, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenExceptionDetailsExceedMaximum_TruncatesRenderedExceptionDetails() {
        _options.MaxExceptionDetailsLength = 96;

        var cut = RenderBoundary(ExceptionDetailsMode.Always);
        var details = cut.Find("pre").TextContent;

        Assert.True(details.Length <= _options.MaxExceptionDetailsLength);
        Assert.Contains("... exception details truncated ...", details, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenRendererIsInteractive_RendersRecoverButton() {
        var cut = RenderBoundary();

        var button = cut.Find("button");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("a"));
    }

    [Fact]
    public void ChildThrows_WhenRendererIsNotInteractive_RendersReloadLink() {
        Renderer.SetRendererInfo(new RendererInfo("Static", false));

        var cut = RenderBoundary();

        var link = cut.Find("a");
        Assert.Equal("Reload page", link.TextContent);
        Assert.Equal(Services.GetRequiredService<NavigationManager>().Uri, link.GetAttribute("href"));
        Assert.Empty(cut.FindAll("button"));
    }

    private IRenderedComponent<IComponent> RenderBoundary(ExceptionDetailsMode? renderExceptionDetails = null, string? name = null) => Render(builder => {
        builder.OpenComponent<NTErrorBoundary>(0);
        builder.AddAttribute(1, nameof(NTErrorBoundary.ChildContent), (RenderFragment)(childBuilder => {
            childBuilder.OpenComponent<ThrowingComponent>(0);
            childBuilder.CloseComponent();
        }));
        if (renderExceptionDetails.HasValue) {
            builder.AddAttribute(2, nameof(NTErrorBoundary.RenderExceptionDetails), renderExceptionDetails.Value);
        }
        if (name is not null) {
            builder.AddAttribute(3, nameof(NTErrorBoundary.Name), name);
        }
        builder.CloseComponent();
    });

    private sealed class CapturingBlazorErrorReporter : INTBlazorErrorReporter {
        public NTBlazorErrorBoundaryContext? Context { get; private set; }

        public Exception? Exception { get; private set; }

        public Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
            Exception = exception;
            Context = context;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingComponent : ComponentBase {
        public static InvalidOperationException Exception { get; } = new("sensitive failure detail");

        protected override void BuildRenderTree(RenderTreeBuilder builder) => throw Exception;
    }
}
