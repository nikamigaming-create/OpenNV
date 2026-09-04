using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    /// <summary>
    /// Dispatches one validated content source. Returns false only when startup
    /// has no explicit source and should continue to the default startup path.
    /// </summary>
    private bool TryDispatchLaunch(RuntimeLaunchRequest launch)
    {
        if (launch.Is(RuntimeLaunchRoute.LiveRetailFiles))
        {
            if (_nativeInstallation?.Game == NativeGame.Fallout1)
                LoadFallout1NativeInstall(_nativeInstallation.InstallRoot, RequireOption(_options, "save-path"));
            else if (_nativeInstallation?.Game == NativeGame.Fallout2)
                LoadFallout2NativeInstall(
                    _nativeInstallation.InstallRoot,
                    RequireOption(_options, "save-path"));
            else
                LoadNativeLiveStack();
            return true;
        }

        return false;
    }

    private void LoadFallout2NativeInstall(string installRoot, string savePath)
    {
        _options["fo2-install-root"] = installRoot;
        _options["save-path"] = savePath;
        var scene = GD.Load<PackedScene>("res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn");
        AddChild(scene.Instantiate<Node3D>());
        DismissLoadingScreen();
    }
}
