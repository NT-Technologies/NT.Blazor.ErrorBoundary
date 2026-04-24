using Microsoft.Extensions.Logging;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.AspNetCore;

internal sealed class NTBlazorServerErrorReporter(ILogger<NTBlazorServerErrorReporter> logger) : INTBlazorErrorReporter {
    private readonly ILogger<NTBlazorServerErrorReporter> _logger = logger;

    public Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        using (_logger.BeginScope(CreateScope(context))) {
            _logger.LogError(exception, "Unhandled Blazor component exception caught by {BoundaryName}", context.BoundaryName);
        }

        return Task.CompletedTask;
    }

    private static Dictionary<string, object?> CreateScope(NTBlazorErrorBoundaryContext context) => new(StringComparer.Ordinal) {
        ["BlazorBoundaryName"] = context.BoundaryName,
        ["BlazorIsInteractive"] = context.IsInteractive,
        ["BlazorRenderMode"] = context.RenderMode,
        ["BlazorUri"] = context.Uri
    };
}

