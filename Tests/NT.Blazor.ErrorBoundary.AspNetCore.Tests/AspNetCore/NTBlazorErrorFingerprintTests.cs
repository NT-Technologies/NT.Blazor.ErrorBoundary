using NT.Blazor.ErrorBoundary.Models;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class NTBlazorErrorFingerprintTests {
    [Theory]
    [InlineData("   at App.Orders.Load() in C:\\build\\a\\Orders.cs:line 12\n   at App.Page.Render() in C:\\build\\a\\Page.cs:line 40", "   at App.Orders.Load() in /src/Orders.cs:line 99\n   at App.Page.Render() in /src/Page.cs:line 7")]
    [InlineData("TypeError: x is undefined\n    at load (https://staging.example.test/app.js?v=1:10:5)", "TypeError: x is undefined for order 42\n    at load (https://www.example.test/app.js?v=2:11:9)")]
    [InlineData("load@https://staging.example.test/app.js?v=1:10:5", "load@https://www.example.test/app.js?v=2:3:1")]
    [InlineData("  at App.Orders.Load () <0x2a1b0> + 0x0001a in <filename unknown>:0", "  at App.Orders.Load () <0x9f000> + 0x00042 in <filename unknown>:0")]
    [InlineData("  at App.Orders.Load () <0x2a1b0 + 0x0001a> in <filename unknown>:0", "  at App.Orders.Load () <0x9f000 + 0x00042> in <filename unknown>:0")]
    [InlineData("    at Fe (https://app.test/_framework/blazor.web.xmryko9e6j.js:1:2345)", "    at Fe (https://app.test/_framework/blazor.web.4k2l8q0z1m.js:1:2345)")]
    [InlineData("Fe@https://app.test/_framework/dotnet.runtime.5nc8i5dl74.js:3:10", "Fe@https://app.test/_framework/dotnet.runtime.a1b2c3d4e5.js:3:10")]
    [InlineData("   at App.Orders.<Load>d__5.MoveNext()", "   at App.Orders.<Load>d__12.MoveNext()")]
    [InlineData("   at App.Page.<>c__DisplayClass0_0.<Render>b__0()", "   at App.Page.<>c__DisplayClass3_1.<Render>b__2()")]
    [InlineData("   at App.Page.<Render>b__0_1()", "   at App.Page.<Render>b__4_0()")]
    [InlineData("   at App.Page.<Run>g__Inner|3_0()", "   at App.Page.<Run>g__Inner|7_1()")]
    [InlineData("  at App.Orders.Load () [0x00012] in <filename unknown>:0", "  at App.Orders.Load () [0x0ff99] in <filename unknown>:0")]
    [InlineData("    at Fe (https://app.test/_framework/dotnet.native.xmryko9e6j.wasm:1:2)", "    at Fe (https://app.test/_framework/dotnet.native.4k2l8q0z1m.wasm:1:2)")]
    [InlineData("    at Fe (https://app.test/_framework/blazor.web.xmryko9e6j.mjs:1:2)", "    at Fe (https://app.test/_framework/blazor.web.4k2l8q0z1m.mjs:1:2)")]
    [InlineData("    at Fe (https://app.test/css/site.xmryko9e6j.css:1:2)", "    at Fe (https://app.test/css/site.4k2l8q0z1m.css:1:2)")]
    public void SameFramesAtDifferentLocations_ProduceSameFingerprint(string first, string second) {
        Assert.Equal(Fingerprint(stack: first), Fingerprint(stack: second));
    }

    [Theory]
    [InlineData("at App.Orders.Load()", "at App.Orders.Save()")]
    [InlineData("at load (https://example.test/app.js:1:1)", "at load (https://example.test/other.js:1:1)")]
    [InlineData("at Fe (https://app.test/_framework/blazor.web.xmryko9e6j.js:1:1)", "at Fe (https://app.test/_framework/blazor.server.xmryko9e6j.js:1:1)")]
    [InlineData("   at App.Orders.<Load>d__5.MoveNext()", "   at App.Orders.<Save>d__5.MoveNext()")]
    [InlineData("   at App.Reports.Report2024Builder.Run()", "   at App.Reports.Report2025Builder.Run()")]
    public void DifferentFrames_ProduceDifferentFingerprints(string first, string second) {
        Assert.NotEqual(Fingerprint(stack: first), Fingerprint(stack: second));
    }

    [Fact]
    public void DifferentReportKinds_ProduceDifferentFingerprints() {
        Assert.NotEqual(Fingerprint(kind: NTBlazorReportKinds.Exception), Fingerprint(kind: NTBlazorReportKinds.JavaScriptError));
    }

    [Fact]
    public void MessageLineContainingAtSign_IsNotCountedAsFrame() {
        Assert.Equal(
            Fingerprint(stack: "Error: no user bob@x.test\n    at load (https://app.test/app.js:1:1)"),
            Fingerprint(stack: "Error: no user carol@y.test\n    at load (https://app.test/app.js:1:1)"));
    }

    [Fact]
    public void FrameLinesBeyondMaxLength_AreCappedBeforeFingerprinting() {
        var head = "at " + new string('a', 509);

        Assert.Equal(Fingerprint(stack: head + new string('x', 200)), Fingerprint(stack: head + new string('y', 200)));
    }

    [Fact]
    public void FrameLinesDifferingWithinMaxLength_ProduceDifferentFingerprints() {
        Assert.NotEqual(Fingerprint(stack: "at " + new string('a', 500) + "x" + new string('a', 200)), Fingerprint(stack: "at " + new string('a', 500) + "y" + new string('a', 200)));
    }

    [Fact]
    public void FixedReport_ProducesPinnedFingerprint() {
        var fingerprint = Fingerprint(stack: "   at App.Orders.Load(Int32 id) in C:\\src\\Orders.cs:line 12\n   at App.Page.Render()");

        Assert.Equal("3d7a961c6cc92cad", fingerprint);
    }

    [Fact]
    public void NonAsciiMessageWithoutFrames_ProducesPinnedFingerprint() {
        var fingerprint = Fingerprint(stack: null, message: "Échec de la commande 日本語 😀");

        Assert.Equal("e2773449eb241238", fingerprint);
    }

    [Fact]
    public void DifferentExceptionTypes_ProduceDifferentFingerprints() {
        Assert.NotEqual(Fingerprint(type: "System.InvalidOperationException"), Fingerprint(type: "System.ArgumentException"));
    }

    [Fact]
    public void FramesBeyondTheFifth_AreIgnored() {
        const string Top = "at A()\nat B()\nat C()\nat D()\nat E()\n";

        Assert.Equal(Fingerprint(stack: Top + "at F()"), Fingerprint(stack: Top + "at G()"));
    }

    [Fact]
    public void WithFrames_MessageIsIgnored() {
        Assert.Equal(Fingerprint(message: "order 1"), Fingerprint(message: "order 2"));
    }

    [Fact]
    public void WithoutFrames_MessageDistinguishesErrors() {
        Assert.NotEqual(Fingerprint(stack: null, message: "Script error."), Fingerprint(stack: null, message: "Other error."));
    }

    [Fact]
    public void Fingerprint_IsSixteenLowercaseHexCharacters() {
        Assert.Matches("^[0-9a-f]{16}$", Fingerprint());
    }

    private static string Fingerprint(string? stack = "at App.Orders.Load()", string type = "System.InvalidOperationException", string message = "failed", string kind = NTBlazorReportKinds.Exception) => NTBlazorErrorFingerprint.Compute(new NTBlazorErrorReport {
        ReportKind = kind,
        ExceptionMessage = message,
        ExceptionStackTrace = stack,
        ExceptionType = type
    });
}
