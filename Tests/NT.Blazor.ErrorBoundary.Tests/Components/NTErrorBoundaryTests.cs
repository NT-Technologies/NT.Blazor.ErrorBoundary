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

    [Fact]
    public void ChildThrows_ReportsReferenceIdAndRendersIt() {
        var cut = RenderBoundary();

        var referenceId = _reporter.Context?.ReferenceId;
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{8}$", referenceId);
        Assert.Contains($"Reference: {referenceId}", cut.Find(".nt-blazor-error-reference").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenCustomLabels_RendersCustomText() {
        var cut = RenderBoundary(extra: [
            (nameof(NTErrorBoundary.Title), "Oops"),
            (nameof(NTErrorBoundary.Message), "Please retry."),
            (nameof(NTErrorBoundary.RetryText), "Again"),
            (nameof(NTErrorBoundary.ReferenceLabel), "Ticket")
        ]);

        Assert.Equal("Oops", cut.Find(".nt-blazor-error-title").TextContent);
        Assert.Equal("Please retry.", cut.Find(".nt-blazor-error-message").TextContent);
        Assert.Equal("Again", cut.Find("button").TextContent);
        Assert.StartsWith("Ticket:", cut.Find(".nt-blazor-error-reference").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildThrows_WhenNotInteractive_RendersCustomReloadText() {
        Renderer.SetRendererInfo(new RendererInfo("Static", false));

        var cut = RenderBoundary(extra: [(nameof(NTErrorBoundary.ReloadText), "Reload now")]);

        Assert.Equal("Reload now", cut.Find("a").TextContent);
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(2, "2")]
    [InlineData(4, "4")]
    [InlineData(9, "6")]
    public void ChildThrows_RendersTitleAsClampedHeading(int headingLevel, string expectedLevel) {
        var cut = RenderBoundary(extra: [(nameof(NTErrorBoundary.HeadingLevel), headingLevel)]);

        var title = cut.Find(".nt-blazor-error-title");
        Assert.Equal("heading", title.GetAttribute("role"));
        Assert.Equal(expectedLevel, title.GetAttribute("aria-level"));
        Assert.Empty(cut.FindAll("h1,h2,h3,h4,h5,h6"));
    }

    [Fact]
    public void ChildThrows_WhenFallbackContentSet_RendersItInsteadOfErrorContent() {
        var cut = RenderBoundary(extra: [
            (nameof(NTErrorBoundary.FallbackContent), (RenderFragment<NTErrorBoundaryFailure>)(failure => builder => builder.AddContent(0, $"custom {failure.ReferenceId} {failure.Exception.Message}"))),
            (nameof(NTErrorBoundary.ErrorContent), (RenderFragment<Exception>)(_ => builder => builder.AddContent(0, "error content")))
        ]);

        Assert.Equal($"custom {_reporter.Context?.ReferenceId} {ThrowingComponent.Exception.Message}", cut.Markup);
    }

    [Fact]
    public void TryAgainClicked_WhenChildFailsAgain_GeneratesNewReferenceId() {
        var cut = RenderBoundary();
        var first = _reporter.Context?.ReferenceId;

        cut.Find("button").Click();

        Assert.NotEqual(first, _reporter.Context?.ReferenceId);
    }

    [Fact]
    public void Navigate_WhenErrorShowing_RecoversByDefault() {
        var shouldThrow = true;
        var cut = RenderBoundary(child: ToggleChild(() => shouldThrow));
        Assert.Contains("Something went wrong.", cut.Markup, StringComparison.Ordinal);
        shouldThrow = false;

        Services.GetRequiredService<NavigationManager>().NavigateTo("/elsewhere");

        cut.WaitForAssertion(() => Assert.Equal("recovered", cut.Markup));
    }

    [Fact]
    public void Navigate_WhenRecoverOnNavigationDisabled_KeepsFallback() {
        var shouldThrow = true;
        var cut = RenderBoundary(child: ToggleChild(() => shouldThrow), extra: [(nameof(NTErrorBoundary.RecoverOnNavigation), false)]);
        shouldThrow = false;

        Services.GetRequiredService<NavigationManager>().NavigateTo("/elsewhere");

        Assert.Contains("Something went wrong.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromNavigation() {
        var shouldThrow = true;
        var cut = RenderBoundary(child: ToggleChild(() => shouldThrow));
        await DisposeComponentsAsync();
        shouldThrow = false;

        var exception = Record.Exception(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/elsewhere"));

        Assert.Null(exception);
    }

    private static RenderFragment ToggleChild(Func<bool> shouldThrow) => builder => {
        builder.OpenComponent<ToggleComponent>(0);
        builder.AddAttribute(1, nameof(ToggleComponent.ShouldThrow), shouldThrow);
        builder.CloseComponent();
    };

    private IRenderedComponent<IComponent> RenderBoundary(ExceptionDetailsMode? renderExceptionDetails = null, string? name = null, RenderFragment? child = null, (string Name, object Value)[]? extra = null) => Render(builder => {
        builder.OpenComponent<NTErrorBoundary>(0);
        builder.AddAttribute(1, nameof(NTErrorBoundary.ChildContent), child ?? (RenderFragment)(childBuilder => {
            childBuilder.OpenComponent<ThrowingComponent>(0);
            childBuilder.CloseComponent();
        }));
        if (renderExceptionDetails.HasValue) {
            builder.AddAttribute(2, nameof(NTErrorBoundary.RenderExceptionDetails), renderExceptionDetails.Value);
        }
        if (name is not null) {
            builder.AddAttribute(3, nameof(NTErrorBoundary.Name), name);
        }
        for (var i = 0; i < (extra?.Length ?? 0); i++) {
            builder.AddAttribute(10 + i, extra![i].Name, extra[i].Value);
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

    private sealed class ToggleComponent : ComponentBase {
        [Parameter]
        public Func<bool> ShouldThrow { get; set; } = () => false;

        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            if (ShouldThrow()) {
                throw new InvalidOperationException("toggled failure");
            }

            builder.AddContent(0, "recovered");
        }
    }
}
