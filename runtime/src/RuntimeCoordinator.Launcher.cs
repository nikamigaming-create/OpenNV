using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void LoadGodotLauncher()
    {
        var launcher = new NativeGodotLauncher();
        launcher.Configure(GodotLauncherProfileStore.Load(_options.GetValueOrDefault("profile-root")),
            _options.GetValueOrDefault("launcher-game"), _options.GetValueOrDefault("presentation"));
        launcher.LaunchRequested += request => StartInProcessLaunch(launcher, request);
        AddChild(launcher);
        SetMeta("opennv_product_entry", "godot-native-launcher-v1");
        GD.Print("OPENNV_GODOT_LAUNCHER_READY source=live-installation-picker " +
            "routes=manifest-backed profiles=godot-native");
        DismissLoadingScreen();
    }

    private async void StartInProcessLaunch(
        NativeGodotLauncher launcher,
        NativeGodotLauncherLaunchRequest request)
    {
        try
        {
            ConfigureLauncherEntry(request);
            var sessionOptions = _options;
            _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data-root"] = request.DataRoot,
                ["campaign"] = request.EngineCampaign,
                ["presentation"] = request.Presentation,
                ["save-path"] = request.SavePath,
            };
            foreach (var name in new[] { "profile-root", "settings-path", "live-harness", "parity-channel", "parity-capture", "perf-report", "native-plugins", "content-selection" })
                if (sessionOptions.TryGetValue(name, out var value)) _options.Add(name, value);
            request.ModSelection?.WriteOptions(_options);
            if (request.EngineCampaign is "fallout-new-vegas" or "fallout-3")
                _options["opening-menu"] = "true";
            if (request.Presentation == "openxr")
            {
                _options["vr"] = "true";
                // Input can request a launch while the previous mono frame is
                // still queued. Change viewport/camera view counts together
                // after that draw, as the direct startup route already does.
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!IsInstanceValid(this) || !IsInstanceValid(launcher) || !IsInsideTree()) return;
                EnableOpenXr();
            }
            if (request.AppearanceDataRoot is not null)
                _options["appearance-data-root"] = request.AppearanceDataRoot;
            if (request.Fallout3WorldRoot is not null)
                _options["world-fallout3-data-root"] = request.Fallout3WorldRoot;

            _nativeInstallation = NativeGameInstallation.Detect(request.DataRoot);
            RuntimeLaunchValidator.ValidateInstallation(_options, _nativeInstallation);
            if (_nativeInstallation.Game is NativeGame.Fallout3 or NativeGame.FalloutNewVegas)
                ConfigureSelectedContent(request.DataRoot, request.EngineCampaign);
            else
                RuntimeLiveContentSource.Clear();

            var launch = RuntimeLaunchRequest.Create(_options);
            RuntimeLaunchValidator.ValidatePreflight(_options);
            RuntimeLaunchValidator.ValidateContent(_options, launch);
            launcher.Hide();
            if (!TryDispatchLaunch(launch))
                throw new InvalidOperationException("The selected route has no in-process runtime owner.");
            launcher.QueueFree();
            GD.Print($"OPENNV_GODOT_IN_PROCESS_ROUTE_READY campaign={request.CampaignId} " +
                $"presentation={request.Presentation} source={request.DataRoot}");
        }
        catch (Exception exception)
        {
            CancelNativeLauncherEntry();
            RuntimeLiveContentSource.Clear();
            launcher.Show();
            launcher.ReportLaunchFailure(exception.Message);
            GD.PushError($"OPENNV_GODOT_IN_PROCESS_ROUTE_FAIL {exception}");
        }
    }

    private void ConfigureSelectedContent(string baseRoot, string campaign)
    {
        var nativeSelection = _options.GetValueOrDefault("native-plugins", "none");
        if (nativeSelection != "none")
            throw new NotSupportedException("This recovery build runs with native extensions disabled. Use --native-plugins none.");
        var selection = FalloutModStackSelection.ReadOptions(_options);
        var contentSelection = _options.GetValueOrDefault("content-selection", selection is null ? "vanilla" : "installed");
        if (contentSelection is not ("vanilla" or "installed"))
            throw new ArgumentException("Content selection must be vanilla or installed.");
        if (contentSelection == "vanilla" && selection is not null)
            throw new InvalidOperationException("Disable selected mods for vanilla play, or explicitly select installed content.");
        var mod = selection?.Resolve(baseRoot);
        var vanilla = contentSelection == "vanilla";
        var plugins = vanilla ? RuntimeLiveContentSource.ResolveVanillaPlugins(baseRoot) : mod?.ActivePlugins;
        RuntimeLiveContentSource.Configure(baseRoot, campaign, mod?.ContentRoots.Skip(1).ToArray(), plugins, mod?.Settings,
            includeImplicitPlugins: !vanilla);
        var source = RuntimeLiveContentSource.Current!;
        GD.Print($"OPENNV_CONTENT_SELECTION mode={contentSelection} nativePlugins=none " +
            $"plugins={string.Join(',', source.PluginSources.Select(plugin => plugin.Name))} saveCompatibility={source.SaveCompatibilityId}");
    }
}
