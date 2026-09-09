using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

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
            if (_options.TryGetValue("development-gallery", out var gallery))
            {
                RunNativeDevelopmentGallery(gallery);
                return true;
            }
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
        var scene = GD.Load<PackedScene>("res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn");
        var host = scene.Instantiate<Fo2CharacterStartHost>();
        host.Configure(installRoot, savePath, _options.GetValueOrDefault("appearance-data-root"),
            _options.GetValueOrDefault("world-fallout3-data-root"));
        AddChild(host);
        DismissLoadingScreen();
    }
}
