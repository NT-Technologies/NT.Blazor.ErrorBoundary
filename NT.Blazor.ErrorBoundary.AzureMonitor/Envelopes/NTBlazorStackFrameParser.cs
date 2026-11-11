using System.Globalization;
using System.Text.RegularExpressions;

namespace NT.Blazor.ErrorBoundary.AzureMonitor.Envelopes;

// Parses the stack text a browser report carries: .NET and Mono frames from WebAssembly, and Chrome, Firefox, and Safari frames from JavaScript.
// The text is attacker-controlled, so the regexes run on the non-backtracking engine, whose time is linear in the input.
internal static partial class NTBlazorStackFrameParser {
    internal const int MaxFrames = 100;
    // Real frames are far shorter; with the non-backtracking engine below, the cap keeps hostile report text cheap to parse.
    internal const int MaxFrameLength = 512;

    internal static List<NTBlazorStackFrame> Parse(string? stack) {
        var frames = new List<NTBlazorStackFrame>();
        foreach (var line in (stack ?? string.Empty).Split('\n')) {
            var trimmed = line.Trim();
            var text = trimmed.Length > MaxFrameLength ? trimmed[..MaxFrameLength] : trimmed;
            // Chrome frames are tried before .NET frames, because "at fn (url:1:2)" also fits the .NET shape.
            var match = ChromeFrame().Match(text) is { Success: true } chrome ? chrome
                : AnonymousChromeFrame().Match(text) is { Success: true } anonymous ? anonymous
                : FirefoxFrame().Match(text) is { Success: true } firefox ? firefox
                : DotNetFrame().Match(text);
            if (match.Success) {
                var method = match.Groups["method"].Value.Trim();
                var file = match.Groups["file"].Value;
                frames.Add(new NTBlazorStackFrame(method.Length > 0 ? method : "<anonymous>", file.Length > 0 && !file.StartsWith('<') ? file : null, int.TryParse(match.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0, text));
                if (frames.Count == MaxFrames) {
                    break;
                }
            }
        }

        return frames;
    }

    [GeneratedRegex(@"^at (?<method>.+?) \((?<file>.+?):(?<line>\d+):\d+\)$", RegexOptions.NonBacktracking)]
    private static partial Regex ChromeFrame();

    [GeneratedRegex(@"^at (?<file>\S+?):(?<line>\d+):\d+$", RegexOptions.NonBacktracking)]
    private static partial Regex AnonymousChromeFrame();

    [GeneratedRegex(@"^(?<method>[^@\s]*)@(?<file>.+?):(?<line>\d+):\d+$", RegexOptions.NonBacktracking)]
    private static partial Regex FirefoxFrame();

    // "at Namespace.Type.Method(args) in path:line 12", or Mono's "at Method () <0x..> + 0x.. in <filename unknown>:0".
    [GeneratedRegex(@"^at (?<method>.+?)\(.*\)(?:.*? in (?<file>.+?)(?::line (?<line>\d+)|:\d+)?)?$", RegexOptions.NonBacktracking)]
    private static partial Regex DotNetFrame();
}
