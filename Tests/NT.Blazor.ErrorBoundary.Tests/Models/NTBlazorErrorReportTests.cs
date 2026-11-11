using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Reflection;

namespace NT.Blazor.ErrorBoundary.Tests.Models;

public sealed class NTBlazorErrorReportTests {
    // Microsoft.Testing.Platform runs this test project as its own executable, so it is the entry assembly here, as the app is in production.
    [Fact]
    public void FromException_SetsEntryAssemblyInformationalVersion() {
        var expected = typeof(NTBlazorErrorReportTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var report = NTBlazorErrorReport.FromException(new InvalidOperationException("x"), new NTBlazorErrorBoundaryContext());

        Assert.Equal(expected, report.ApplicationVersion);
    }

    [Fact]
    public void WithClientContext_KeepsReportVersionOverPageVersion() {
        var report = new NTBlazorErrorReport { ApplicationVersion = "2.0.0+client" }.WithClientContext(new NTBlazorClientContext { ApplicationVersion = "1.4.0+server" });

        Assert.Equal("2.0.0+client", report.ApplicationVersion);
    }

    [Fact]
    public void WithClientContext_ReportWithoutVersion_TakesPageVersion() {
        var report = new NTBlazorErrorReport().WithClientContext(new NTBlazorClientContext { ApplicationVersion = "1.4.0+server" });

        Assert.Equal("1.4.0+server", report.ApplicationVersion);
    }

    [Fact]
    public void ToString_OmitsMessagesStackTracesAndUris() {
        var report = new NTBlazorErrorReport {
            ExceptionDetails = "details-secret",
            ExceptionMessage = "message-secret",
            ExceptionStackTrace = "stack-secret",
            ExceptionType = "System.InvalidOperationException",
            OriginUri = "https://example.test/origin-secret",
            ReferenceId = "7F3K2QAB",
            TargetUri = "https://example.test/target-secret",
            Uri = "https://example.test/uri-secret"
        };

        Assert.Equal("NTBlazorErrorReport { ReportKind = Exception, ExceptionType = System.InvalidOperationException, ReferenceId = 7F3K2QAB }", report.ToString());
    }
}
