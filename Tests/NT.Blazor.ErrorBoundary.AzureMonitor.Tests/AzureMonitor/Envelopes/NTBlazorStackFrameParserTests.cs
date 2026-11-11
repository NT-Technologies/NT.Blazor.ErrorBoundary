using NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Tests.AzureMonitor.Envelopes;

public sealed class NTBlazorStackFrameParserTests {
    [Theory]
    [InlineData("   at App.Orders.Load(Int32 id) in C:\\src\\Orders.cs:line 12", "App.Orders.Load", "C:\\src\\Orders.cs", 12)]
    [InlineData("   at App.Repository`1.Get[T](T key)", "App.Repository`1.Get[T]", null, 0)]
    [InlineData("  at App.Orders.Load () <0x2a1b0> + 0x0001a in <filename unknown>:0", "App.Orders.Load", null, 0)]
    [InlineData("  at App.Orders.Load () <0x2a1b0 + 0x0001a> in <filename unknown>:0", "App.Orders.Load", null, 0)]
    [InlineData("    at load (https://app.test/js/app.js?v=3:10:5)", "load", "https://app.test/js/app.js?v=3", 10)]
    [InlineData("    at HTMLButtonElement.onclick (https://app.test/:1:22)", "HTMLButtonElement.onclick", "https://app.test/", 1)]
    [InlineData("    at https://app.test/_framework/blazor.web.js:1:2345", "<anonymous>", "https://app.test/_framework/blazor.web.js", 1)]
    [InlineData("load@https://app.test/js/app.js:10:5", "load", "https://app.test/js/app.js", 10)]
    [InlineData("@https://app.test/js/app.js:20:1", "<anonymous>", "https://app.test/js/app.js", 20)]
    public void FrameLine_ParsesMethodFileAndLine(string line, string method, string? file, int number) {
        var frame = Assert.Single(NTBlazorStackFrameParser.Parse(line));

        Assert.Equal(method, frame.Method);
        Assert.Equal(file, frame.File);
        Assert.Equal(number, frame.Line);
        Assert.Equal(line.Trim(), frame.Raw);
    }

    [Fact]
    public void MessageAndSeparatorLines_AreSkipped() {
        var frames = NTBlazorStackFrameParser.Parse("TypeError: x is undefined\n    at load (https://app.test/app.js:10:5)\n--- End of stack trace from previous location ---\n   at App.Run()");

        Assert.Equal(["load", "App.Run"], frames.Select(frame => frame.Method));
    }

    [Fact]
    public void LongStack_IsCappedAtMaxFrames() {
        var stack = string.Join('\n', Enumerable.Range(0, 150).Select(index => $"at App.Frame{index}()"));

        Assert.Equal(NTBlazorStackFrameParser.MaxFrames, NTBlazorStackFrameParser.Parse(stack).Count);
    }

    [Fact]
    public void LineLongerThanMaxLength_IsCappedAndStillParsedWhenFrameStartsWithinIt() {
        var line = "at App.Orders.Load(Int32 id) in /src/" + new string('a', 600);

        var frame = Assert.Single(NTBlazorStackFrameParser.Parse(line));

        Assert.Equal(NTBlazorStackFrameParser.MaxFrameLength, frame.Raw.Length);
        Assert.Equal(line[..NTBlazorStackFrameParser.MaxFrameLength], frame.Raw);
        Assert.Equal("App.Orders.Load", frame.Method);
        Assert.StartsWith("/src/aaa", frame.File, StringComparison.Ordinal);
    }

    [Fact]
    public void LineWhoseClosingPartLiesBeyondMaxLength_IsNotParsed() {
        var line = "at load (https://app.test/" + new string('a', 600) + ".js:1:1)";

        Assert.Empty(NTBlazorStackFrameParser.Parse(line));
    }

    [Theory]
    [InlineData("    at load (https://app.test/app.js:99999999999:5)", "https://app.test/app.js")]
    [InlineData("   at App.Orders.Load() in /src/Orders.cs:line 99999999999", "/src/Orders.cs")]
    public void LineNumberBeyondInt32_ParsesAsZero(string line, string file) {
        var frame = Assert.Single(NTBlazorStackFrameParser.Parse(line));

        Assert.Equal(0, frame.Line);
        Assert.Equal(file, frame.File);
    }

    [Fact]
    public void LineFittingChromeAndDotNetShapes_ParsesAsChrome() {
        var frame = Assert.Single(NTBlazorStackFrameParser.Parse("    at Object.load (https://app.test/app.js:10:5)"));

        Assert.Equal("Object.load", frame.Method);
        Assert.Equal("https://app.test/app.js", frame.File);
        Assert.Equal(10, frame.Line);
    }

    [Fact]
    public void AsyncChromeFrame_KeepsAsyncInMethodName() {
        var frame = Assert.Single(NTBlazorStackFrameParser.Parse("    at async fn (https://app.test/app.js:1:2)"));

        Assert.Equal("async fn", frame.Method);
        Assert.Equal("https://app.test/app.js", frame.File);
        Assert.Equal(1, frame.Line);
    }

    [Fact]
    public void SafariNativeCodeLine_IsSkipped() {
        var frames = NTBlazorStackFrameParser.Parse("forEach@[native code]\nload@https://app.test/app.js:10:5");

        Assert.Equal("load", Assert.Single(frames).Method);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Script error.")]
    public void NoFrames_ReturnsEmpty(string? stack) {
        Assert.Empty(NTBlazorStackFrameParser.Parse(stack));
    }
}
