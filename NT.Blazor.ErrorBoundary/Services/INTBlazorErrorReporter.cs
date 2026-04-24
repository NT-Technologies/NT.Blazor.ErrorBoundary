using NT.Blazor.ErrorBoundary.Models;

namespace NT.Blazor.ErrorBoundary.Services;

/// <summary>
/// Reports unhandled Blazor component exceptions captured by a boundary.
/// </summary>
public interface INTBlazorErrorReporter {
    /// <summary>
    /// Reports a captured exception and the boundary context where it occurred.
    /// </summary>
    Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default);
}
