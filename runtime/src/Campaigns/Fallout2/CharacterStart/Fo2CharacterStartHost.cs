using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Campaigns.Classic.Native;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

public sealed partial class Fo2CharacterStartHost : Node3D
{
    private string? _installRoot, _savePath, _appearanceRoot, _fallout3WorldRoot;
    private Fo2NativeOwnedSource? _source;

    internal void Configure(string installRoot, string savePath, string? appearanceRoot, string? fallout3WorldRoot = null)
    { _installRoot = installRoot; _savePath = savePath; _appearanceRoot = appearanceRoot; _fallout3WorldRoot = fallout3WorldRoot; }

    public override void _Ready()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_installRoot) || string.IsNullOrWhiteSpace(_savePath))
                throw new InvalidOperationException("Fallout 2 requires the validated campaign launch configuration.");
            _source = Fo2NativeOwnedSource.LoadInstall(_installRoot);
            var world = new ClassicWorldBrowser(); AddChild(world);
            world.Configure(_source, _savePath, _appearanceRoot, "fallout-2", _fallout3WorldRoot);
            SetMeta("authoritative_save_path", _savePath);
            GD.Print($"OPENNV_FO2_NATIVE_INSTALL_READY profile={_source.ProfileId} source=live-owned-install " +
                "world=shared-classic maps=owned-catalog movement=available campaign=unimplemented");
            if (DisplayServer.GetName() == "headless") GetTree().Quit();
        }
        catch (Exception exception)
        { GD.PushError($"OPENNV_FO2_NATIVE_INSTALL_FAIL {exception}"); GetTree().Quit(1); }
    }

    public override void _ExitTree() => _source?.Dispose();
}
