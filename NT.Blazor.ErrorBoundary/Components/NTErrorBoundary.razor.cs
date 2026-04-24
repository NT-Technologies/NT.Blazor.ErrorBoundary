using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

namespace NT.Blazor.ErrorBoundary.Components;

/// <summary>
/// Error boundary that reports captured Blazor component exceptions before rendering fallback UI.
/// </summary>
public partial class NTErrorBoundary(INTBlazorErrorReporter _errorReporter, ILogger<NTErrorBoundary> _logger, NavigationManager _navigationManager, IOptions<NTErrorBoundaryOptions> _options) {
    private const string TruncatedExceptionDetailsMessage = "\n... exception details truncated ...";

    private string _exceptionDetails = string.Empty;

    /// <summary>
    /// Gets or sets when fallback UI renders exception details.
    /// </summary>
    [Parameter]
    public ExceptionDetailsMode RenderExceptionDetails { get; set; } = ExceptionDetailsMode.DevelopmentOnly;

    private string CurrentUri => _navigationManager.Uri;

    private string ExceptionDetails => _exceptionDetails;

    private bool ShouldRenderExceptionDetails => RenderExceptionDetails switch {
        ExceptionDetailsMode.Always => true,
        ExceptionDetailsMode.Never => false,
        _ => _options.Value.IsDevelopmentEnvironment
    };

    /// <inheritdoc />
    protected override async Task OnErrorAsync(Exception exception) {
        _exceptionDetails = ShouldRenderExceptionDetails
            ? FormatExceptionDetails(exception)
            : string.Empty;

        try {
            await _errorReporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext {
                BoundaryName = GetType().FullName,
                IsInteractive = RendererInfo.IsInteractive,
                RenderMode = RendererInfo.Name,
                Uri = _navigationManager.Uri
            });
        }
        catch (Exception reportException) {
            _logger.LogWarning(reportException, "Failed to report Blazor error boundary exception.");
        }
    }

    private string FormatExceptionDetails(Exception exception) {
        var details = exception.ToString();
        var maxLength = _options.Value.MaxExceptionDetailsLength;
        if (maxLength <= 0 || details.Length <= maxLength) {
            return details;
        }

        if (maxLength <= TruncatedExceptionDetailsMessage.Length) {
            return details[..maxLength];
        }

        return string.Concat(details.AsSpan(0, maxLength - TruncatedExceptionDetailsMessage.Length), TruncatedExceptionDetailsMessage);
    }
}
