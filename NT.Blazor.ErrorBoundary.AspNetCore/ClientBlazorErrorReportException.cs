using NT.Blazor.ErrorBoundary.Models;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal sealed class ClientBlazorErrorReportException(NTBlazorErrorReport report) : Exception($"{report.ReportKind}: {report.ExceptionType}: {report.ExceptionMessage}") {
    public override string Message => report.ExceptionMessage ?? base.Message;

    public override string? StackTrace => report.ExceptionStackTrace;

    public override string ToString() => report.ExceptionDetails
        ?? (report.ExceptionStackTrace is null ? $"{report.ExceptionType}: {Message}" : $"{report.ExceptionType}: {Message}{Environment.NewLine}{report.ExceptionStackTrace}");
}
