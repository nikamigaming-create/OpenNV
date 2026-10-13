using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed partial class RuntimeHeadlessScreenBlood(FalloutScreenBlood owner,
    RuntimeLiveContentSource source, FalloutScriptMenus menus) : Node
{
    private FalloutHeadlessScreenBlood? _adapter;
    internal object? State => _adapter?.State;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless" || RenderingServer.GetRenderingDevice() is not null)
            throw new InvalidOperationException("Headless screen blood requires the actual host with no rendering device.");
        ProcessMode = ProcessModeEnum.Always; ProcessPriority = int.MinValue + 2;
        var enabled = FalloutInstallationSettings.Read(source).Boolean("ScreenSplatter", "bBloodSplatterEnabled");
        _adapter = new(owner, enabled, FalloutScreenBloodMediaReader.ForSource(source));
        GD.Print($"OPENNV_HEADLESS_SCREEN_BLOOD_BOUND enabled={enabled} nativeResources=false draws=0 parity=unverified");
    }
    public override void _Process(double delta)
    {
        try { _adapter!.Advance(delta, !GetTree().Paused && menus.Query() == 0); }
        catch (Exception error) { SetProcess(false); GD.PushError($"OPENNV_HEADLESS_SCREEN_BLOOD_FAILURE {error}"); }
    }
    public override void _ExitTree() { _adapter?.Dispose(); _adapter = null; }
}
