using OpenNV.Runtime.Campaigns.Classic.Native;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Fallout1.Native;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void LoadFallout1NativeInstall(string installRoot, string isolatedSavePath)
    {
        SetLoadingStatus("READING OWNED MAP / PRO / FRM WORLD");
        var source = Fallout1OwnedContentSource.LoadInstall(installRoot);
        var browser = new ClassicWorldBrowser(); AddChild(browser);
        browser.Configure(source, isolatedSavePath, _options.GetValueOrDefault("appearance-data-root"),
            fallout3WorldRoot: _options.GetValueOrDefault("world-fallout3-data-root"));
        DismissLoadingScreen();
        GD.Print($"OPENNV_FO1_NATIVE_INSTALL_READY profile={source.ProfileId} preparedInputs=0 movement=available campaign=unimplemented");
        if (DisplayServer.GetName() == "headless")
            GetTree().Quit();
    }
}
