using Microsoft.AspNetCore.Components;

namespace NT.Blazor.ErrorBoundary.Sample.Client.Components;

/// <summary>Renders a button whose click handler throws. A fresh instance renders after a boundary recovers.</summary>
public partial class BombButton {
    [Parameter]
    public string Message { get; set; } = "Sample click failure";

    private void Explode() => throw new InvalidOperationException(Message);
}
