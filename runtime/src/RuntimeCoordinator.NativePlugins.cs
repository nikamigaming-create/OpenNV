using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutNativePluginCampaign? _nativePluginCampaign;
    private string? _nativePluginRetirementFailure;
    private void BindNativePluginCampaign()
    {
        if (_nativePluginCampaign is not null) throw new InvalidOperationException("Actual campaign already owns its native module generations.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Native campaign has no selected source.");
        var records = _nativePluginStack ?? throw new InvalidOperationException("Native campaign has no actual winning records.");
        var quests = _nativeQuestState ?? throw new InvalidOperationException("Native campaign has no actual quest state.");
        var scripts = _nativeQuestScripts?.Scripts ?? throw new InvalidOperationException("Native campaign has no actual script/clock/value stores.");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var companion = _options.GetValueOrDefault("native-plugin-companion",
            ProjectSettings.GlobalizePath("res://generated/native-plugins/" + configuration + "/opennv_plugin_domain.exe"));
        var privateRoot = Path.Combine(OS.GetUserDataDir(), "native-plugin-state");
        var owner = new FalloutNativePluginCampaign(records, quests, scripts, companion, privateRoot, source.NativePluginDeclarations);
        var world = scripts.References ?? throw new InvalidOperationException("Native campaign has no shared live reference owner.");
        if (world.NativePlugins is not null) { owner.Dispose(); throw new InvalidOperationException("World native command owner is already bound."); }
        world.NativePlugins = owner; _nativePluginCampaign = owner;
    }
    private void RetireNativePluginCampaign()
    {
        var owner = _nativePluginCampaign;
        if (owner is null) return;
        try { owner.Dispose(); }
        catch (Exception error) { _nativePluginRetirementFailure = error.ToString(); throw; }
        finally
        {
            if (owner.ChildDomainsExited)
            {
                _nativePluginCampaign = null;
                if (_nativeQuestScripts?.Scripts.References is { } world && ReferenceEquals(world.NativePlugins, owner))
                    world.NativePlugins = null;
            }
        }
    }
    private void RequireNativePluginSaveBoundary() => _nativePluginCampaign?.RequireIdleForSave();
}
