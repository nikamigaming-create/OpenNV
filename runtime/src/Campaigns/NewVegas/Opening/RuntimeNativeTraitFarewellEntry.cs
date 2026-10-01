using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeTraitEntry : CanvasLayer
{
    private NativeOwnedTraitMenu? _menu;
    internal object? State => _menu?.State;
    internal event Action<IReadOnlyList<FalloutNativeTraitIdentity>>? Accepted;
    internal event Action<Exception>? Failed;

    internal void Configure(FalloutPluginStack records, FalloutNativeTraitFarewellContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> current)
    {
        Name = "NativeTraitEntry"; Layer = 120;
        _menu = new(records, contract, current, selection => Accepted?.Invoke(selection), error => Failed?.Invoke(error));
        AddChild(_menu);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
