using Microsoft.AspNetCore.Components;

namespace NT.Blazor.ErrorBoundary.Sample.Client.Components;

/// <summary>Throws while initializing, so any boundary above it renders its fallback.</summary>
public partial class ThrowOnRender {
    [Parameter]
    public string Message { get; set; } = "Sample render failure";

    protected override void OnInitialized() => throw new InvalidOperationException(Message);
}
