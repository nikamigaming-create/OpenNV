using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeImageSpace : Node
{
    private FalloutImageSpace _source = null!;
    private FalloutImageSpaceState _state = null!;
    private RetailHdrCompositorEffect _effect = null!;
    private string _unbound = string.Empty;
    internal FalloutImageSpaceFrame? Frame { get; private set; }

    internal void Configure(FalloutImageSpace source, FalloutImageSpaceState state, RetailHdrCompositorEffect effect)
    {
        Name = "NativeImageSpace";
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = 100;
        _source = source; _state = state; _effect = effect;
        Publish(0);
    }

    // Menus pause simulation, but must still present the latest committed IMAD
    // state. Otherwise a fade applied on the menu-opening frame is never drawn.
    public override void _Process(double delta) => Publish(GetTree().Paused ? 0 : delta);

    private void Publish(double delta)
    {
        Frame = _state.Compose(_source);
        _effect.SetSourceFrame(Frame, (float)delta);
        SetMeta("opennv_image_space_operational", _effect.Operational);
        var unbound = string.Join(',', Frame.UnboundChannels);
        if (unbound == _unbound) return;
        _unbound = unbound;
        SetMeta("opennv_unbound_image_space_channels", unbound);
        GD.Print($"OPENNV_NATIVE_IMAGE_SPACE_COVERAGE source={_source.Form} unbound={unbound} parity=unmeasured");
    }
}
