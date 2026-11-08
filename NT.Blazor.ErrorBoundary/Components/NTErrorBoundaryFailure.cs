namespace NT.Blazor.ErrorBoundary.Components;

/// <summary>
/// Describes the failure passed to <see cref="NTErrorBoundary.FallbackContent"/>.
/// </summary>
/// <param name="Exception">The exception captured by the boundary.</param>
/// <param name="ReferenceId">The short reference shown to the user and sent with the error report.</param>
public sealed record NTErrorBoundaryFailure(Exception Exception, string ReferenceId);
