using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeImageSpace : Node
{
    private FalloutImageSpace _source = null!;
    private FalloutImageSpaceState _state = null!;
    private RetailHdrCompositorEffect _effect = null!;
    private FalloutGameTime? _gameTime;
    private string _unbound = string.Empty;
    private FalloutImageSpaceFrame? _menuBackground;
    private long _menuSerial;
    internal FalloutImageSpaceFrame? Frame { get; private set; }
    internal RetailHdrCompositorEffect Effect => _effect;

    internal void Configure(FalloutImageSpace source, FalloutImageSpaceState state, RetailHdrCompositorEffect effect,
        FalloutGameTime? gameTime = null)
    {
        Name = "NativeImageSpace";
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = 100;
        _source = source; _state = state; _effect = effect; _gameTime = gameTime;
        SetMeta("opennv_image_space_program_source", effect.SourceProgramIdentity);
        SetMeta("opennv_image_space_kernel_sha256", effect.SourceKernelSha256);
        Publish(0);
    }

    // Menus pause simulation, but must still present the latest committed IMAD
    // state. Otherwise a fade applied on the menu-opening frame is never drawn.
    public override void _Process(double delta) => Publish(GetTree().Paused ? 0 : delta);

    internal IDisposable BeginMenuBackground(FalloutPluginStack records, FalloutMenuBackgroundKind kind)
    {
        if (_menuBackground is not null) throw new InvalidOperationException("Another static menu background already owns the world capture.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Menu background has no owned content source.");
        var executable = Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
            source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe");
        var declarations = FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(executable);
        var modifier = FalloutImageSpaceModifierReader.Read(records.GetEffective(records.RuntimeFormKey(declarations.Form(kind))));
        var frame = _state.Compose(_source, _gameTime?.Hour, _effect.DoubleVisionPhase, modifier);
        var serial = checked(++_menuSerial);
        _effect.SetMenuBackground(serial, frame);
        _menuBackground = frame;
        SetMeta("opennv_menu_background_source", modifier.Form.ToString());
        SetMeta("opennv_menu_background_source_sha256", modifier.SourceSha256);
        SetMeta("opennv_menu_background_selector_sha256", declarations.SourceSha256);
        SetMeta("opennv_menu_background_active", true);
        Publish(0);
        GD.Print($"OPENNV_NATIVE_MENU_BACKGROUND source={modifier.Form} blur={frame.BlurRadius:R} owner=temporary-imad-world-capture parity=unmeasured");
        return new BackgroundLease(this, serial);
    }

    private sealed class BackgroundLease(RuntimeNativeImageSpace owner, long serial) : IDisposable
    {
        private RuntimeNativeImageSpace? _owner = owner;
        public void Dispose()
        {
            var target = _owner; _owner = null;
            if (target is null || !GodotObject.IsInstanceValid(target) || serial != target._menuSerial) return;
            target._effect.ClearMenuBackground(serial);
            target._menuBackground = null;
            target.SetMeta("opennv_menu_background_active", false);
            target.Publish(0);
        }
    }

    private void Publish(double delta)
    {
        var gameplay = _state.Compose(_source, _gameTime?.Hour, _effect.DoubleVisionPhase);
        Frame = _menuBackground ?? gameplay;
        _effect.SetSourceFrame(gameplay, (float)delta);
        SetMeta("opennv_image_space_operational", _effect.Operational);
        SetMeta("opennv_image_space_blur_radius", Frame.BlurRadius);
        SetMeta("opennv_image_space_double_vision_offset", new Vector2(Frame.DoubleVisionOffset.X, Frame.DoubleVisionOffset.Y));
        if (_gameTime is not null) SetMeta("opennv_image_space_game_hour", _gameTime.Hour);
        var unbound = string.Join(',', Frame.UnboundChannels);
        if (unbound == _unbound) return;
        _unbound = unbound;
        SetMeta("opennv_unbound_image_space_channels", unbound);
        GD.Print($"OPENNV_NATIVE_IMAGE_SPACE_COVERAGE source={_source.Form} unbound={unbound} parity=unmeasured");
    }
}
