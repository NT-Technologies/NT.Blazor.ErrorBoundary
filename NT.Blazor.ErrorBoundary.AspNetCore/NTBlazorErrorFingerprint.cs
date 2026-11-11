using NT.Blazor.ErrorBoundary.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

// A stable key for grouping occurrences of one error across builds, hosts, and cache-busting URLs.
internal static partial class NTBlazorErrorFingerprint {
    private const int MaxFrames = 5;
    // Real frames are far shorter; the cap bounds the normalization regex on hostile report text, which would otherwise take seconds per line.
    private const int MaxFrameLength = 512;

    internal static string Compute(NTBlazorErrorReport report) {
        // .NET and Chrome frames start with "at "; Firefox and Safari frames are "function@location:line:column". Other lines carry messages,
        // which can contain '@' too (an email address), so a bare '@' is not enough.
        var frames = (report.ExceptionStackTrace ?? string.Empty).Split('\n')
            .Select(line => line.Trim())
            .Select(line => line.Length > MaxFrameLength ? line[..MaxFrameLength] : line)
            .Where(line => line.StartsWith("at ", StringComparison.Ordinal) || LocationFrame().IsMatch(line))
            .Take(MaxFrames)
            .Select(line => Volatile().Replace(line, string.Empty))
            .ToList();
        // Without frames, the message is the only distinguishing signal left.
        string?[] parts = frames.Count > 0 ? [report.ReportKind, report.ExceptionType, .. frames] : [report.ReportKind, report.ExceptionType, report.ExceptionMessage];

        // FNV-1a 64 over UTF-16 code units, low byte first.
        var hash = 14695981039346656037UL;
        foreach (var character in string.Join('\n', parts)) {
            hash = (hash ^ (byte)character) * 1099511628211UL;
            hash = (hash ^ (uint)(character >> 8)) * 1099511628211UL;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^[^@]*@\S+:\d+(:\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LocationFrame();

    // File paths and line numbers, URL origins and query strings, line:column positions, and native offsets (Mono writes "<0xADDR + 0xOFFSET>"); content hashes in bundled
    // file names (blazor.web.xmryko9e6j.js); and the numbers the C# compiler gives async state machines (<Load>d__5), lambdas (b__0_1),
    // closures (<>c__DisplayClass0_0), and local functions (g__Inner|3_0), which change whenever unrelated code in the type does.
    [GeneratedRegex(@" in .*$|https?://[^/\s)]+|\?[^:)\s]*|(:\d+)+(?=\)?$)|\s*\[0x[0-9a-f]+\]|\s*<0x[0-9a-f]+(?:\s*\+\s*0x[0-9a-f]+)?>|\s*\+\s*0x[0-9a-f]+|\.(?=[a-z]*[0-9])[a-z0-9]{8,20}(?=\.(?:m?js|css|wasm)\b)|(?<=>[dbg]__|__DisplayClass)[0-9]+(?:_[0-9]+)*|\|[0-9]+(?:_[0-9]+)*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Volatile();
}
