using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Components;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics;
using System.Reflection;

namespace NT.Blazor.ErrorBoundary.Tests.Components;

public sealed class NTBlazorErrorBoundaryScriptTests : BunitContext {
    public NTBlazorErrorBoundaryScriptTests() {
        Services.AddSingleton(Options.Create(new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }));
    }

    [Fact]
    public void Render_EmitsScriptWithReportUriAndServerVersionOnly() {
        var cut = Render<NTBlazorErrorBoundaryScript>();

        var script = cut.Find("script");
        Assert.Equal("_content/NT.Blazor.ErrorBoundary/nt-blazor-error-boundary.js", script.GetAttribute("src"));
        Assert.Equal("/api/errors", script.GetAttribute("data-report-uri"));
        Assert.Equal(typeof(NTBlazorErrorBoundaryScriptTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, script.GetAttribute("data-application-version"));
        Assert.False(script.HasAttribute("data-stall-timeout-ms"));
        Assert.False(script.HasAttribute("data-traceparent"));
    }

    [Fact]
    public void Render_WhenParametersSet_EmitsAttributes() {
        var cut = Render<NTBlazorErrorBoundaryScript>(parameters => parameters
            .Add(p => p.StallTimeoutMs, 7000));

        var script = cut.Find("script");
        Assert.Equal("7000", script.GetAttribute("data-stall-timeout-ms"));
    }

    [Fact]
    public void Render_WhenW3CActivityIsCurrent_EmitsTraceParent() {
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var cut = Render<NTBlazorErrorBoundaryScript>();

        Assert.Equal(activity.Id, cut.Find("script").GetAttribute("data-traceparent"));
    }

    [Fact]
    public void Render_WhenActivityIsNotW3C_OmitsTraceParent() {
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.Hierarchical);
        activity.Start();

        var cut = Render<NTBlazorErrorBoundaryScript>();

        Assert.False(cut.Find("script").HasAttribute("data-traceparent"));
    }
}
