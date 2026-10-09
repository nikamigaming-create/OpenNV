using Godot;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private NativePluginSteamCampaign? _nativeSteamCampaign;
    private void BindNativeSteamCampaign()
    {
        if (_nativeSteamCampaign is not null) throw new InvalidOperationException("Actual Main still owns its platform lifetime.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Main platform has no current owned source.");
        var world = _nativeReferences ?? throw new InvalidOperationException("Main platform has no actual reference world.");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var companion = _options.GetValueOrDefault("native-plugin-companion",
            ProjectSettings.GlobalizePath("res://generated/native-plugins/" + configuration + "/opennv_plugin_domain.exe"));
        var arguments = RuntimeSourceLaunchArguments.Read(source, _configuration.Sha256);
        var owner = new NativePluginSteamCampaign(source, world, companion, Path.Combine(OS.GetUserDataDir(), "native-plugin-state"), arguments);
        // Retain construction before any native/provider/binding call can fail.
        _nativeSteamCampaign = owner; owner.Bind();
    }
    private void RetireNativeSteamCampaign()
    {
        if (_nativeSteamCampaign is not { } owner) return;
        _nativeOpeningStageDriver?.RetireSourceMainScriptCaller();
        owner.Dispose();
        if (owner.Retired) _nativeSteamCampaign = null;
    }
}
