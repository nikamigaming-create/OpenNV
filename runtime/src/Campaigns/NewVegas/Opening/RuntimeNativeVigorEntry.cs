using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeVigorEntry : CanvasLayer
{
    internal event Action<FalloutNativeSpecialState>? Accepted;

    internal void Configure(FalloutNativeVigorContract contract, FalloutNativeSpecialState initial, FalloutPluginStack records)
    {
        ArgumentNullException.ThrowIfNull(contract); ArgumentNullException.ThrowIfNull(initial);
        if (initial.Values.Any(value => value < contract.MinimumAttribute || value > contract.MaximumAttribute) ||
            initial.Values.Sum() > contract.RequiredTotal)
            throw new InvalidDataException("Native initial SPECIAL allocation is invalid.");
        Name = "NativeVigorEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        var menu = new NativeOwnedLoveTesterMenu(contract, initial, records)
        {
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
        };
        menu.Accepted += state => Accepted?.Invoke(state);
        AddChild(menu);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
