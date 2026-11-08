using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
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
    private string? _originUri;
    private string _referenceId = string.Empty;

    /// <summary>
    /// Gets or sets the diagnostic name of the component or page protected by this boundary.
    /// </summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets when fallback UI renders exception details.
    /// </summary>
    [Parameter]
    public ExceptionDetailsMode RenderExceptionDetails { get; set; } = ExceptionDetailsMode.DevelopmentOnly;

    /// <summary>
    /// Gets or sets whether the boundary clears its error when the location changes.
    /// </summary>
    [Parameter]
    public bool RecoverOnNavigation { get; set; } = true;

    /// <summary>
    /// Gets or sets the fallback title.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = "Something went wrong.";

    /// <summary>
    /// Gets or sets the fallback message.
    /// </summary>
    [Parameter]
    public string Message { get; set; } = "Refresh the page or try again.";

    /// <summary>
    /// Gets or sets the label shown before the error reference.
    /// </summary>
    [Parameter]
    public string ReferenceLabel { get; set; } = "Reference";

    /// <summary>
    /// Gets or sets the text of the button that recovers from the error.
    /// </summary>
    [Parameter]
    public string RetryText { get; set; } = "Try again";

    /// <summary>
    /// Gets or sets the text of the link that reloads the page when the renderer is not interactive.
    /// </summary>
    [Parameter]
    public string ReloadText { get; set; } = "Reload page";

    /// <summary>
    /// Gets or sets the accessibility heading level of the fallback title, clamped to 1 through 6.
    /// </summary>
    [Parameter]
    public int HeadingLevel { get; set; } = 2;

    /// <summary>
    /// Gets or sets fallback content rendered with the captured exception and reference ID. Takes precedence over <c>ErrorContent</c>.
    /// </summary>
    [Parameter]
    public RenderFragment<NTErrorBoundaryFailure>? FallbackContent { get; set; }

    private int Level => Math.Clamp(HeadingLevel, 1, 6);

    private bool ShouldRenderExceptionDetails => RenderExceptionDetails switch {
        ExceptionDetailsMode.Always => true,
        ExceptionDetailsMode.Never => false,
        _ => _options.Value.IsDevelopmentEnvironment
    };

    /// <inheritdoc />
    protected override void OnInitialized() {
        _originUri = _navigationManager.Uri;
        _navigationManager.LocationChanged += OnLocationChanged;
    }

    /// <inheritdoc />
    public void Dispose() {
        _navigationManager.LocationChanged -= OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) {
        if (RecoverOnNavigation && CurrentException is not null) {
            _ = InvokeAsync(Recover);
        }
    }

    /// <inheritdoc />
    protected override async Task OnErrorAsync(Exception exception) {
        _referenceId = NTBlazorErrorReference.Create();
        _exceptionDetails = ShouldRenderExceptionDetails
            ? NTBlazorErrorReport.Truncate(exception.ToString(), _options.Value.MaxExceptionDetailsLength, TruncatedExceptionDetailsMessage)!
            : string.Empty;

        try {
            await _errorReporter.ReportAsync(exception, new NTBlazorErrorBoundaryContext {
                BoundaryName = string.IsNullOrWhiteSpace(Name) ? GetType().FullName : Name,
                IsInteractive = RendererInfo.IsInteractive,
                OriginUri = _originUri,
                ReferenceId = _referenceId,
                RenderMode = RendererInfo.Name,
                Uri = _navigationManager.Uri
            });
        }
        catch (Exception reportException) {
            _logger.LogWarning(reportException, "Failed to report Blazor error boundary exception.");
        }
    }
}
