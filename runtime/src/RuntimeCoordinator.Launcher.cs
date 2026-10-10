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

    private void StartInProcessLaunch(
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
            // These belong to the living application session. A source-menu
            // reload must keep the same profile and diagnostic input recipient.
            foreach (var name in new[] { "profile-root", "settings-path", "live-harness", "parity-channel", "parity-capture", "perf-report", "native-plugins" })
                if (sessionOptions.TryGetValue(name, out var value)) _options.Add(name, value);
            request.ModSelection?.WriteOptions(_options);
            if (request.EngineCampaign is "fallout-new-vegas" or "fallout-3")
                _options["opening-menu"] = "true";
            if (request.Presentation == "openxr")
            {
                _options["vr"] = "true";
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
        var mod = FalloutModStackSelection.ReadOptions(_options)?.Resolve(baseRoot);
        var nativeSelection = _options.GetValueOrDefault("native-plugins", "installed");
        if (nativeSelection is not ("installed" or "none"))
            throw new ArgumentException("Native plugin selection must be installed or none.");
        RuntimeLiveContentSource.Configure(baseRoot, campaign, mod?.ContentRoots.Skip(1).ToArray(), mod?.ActivePlugins, mod?.Settings,
            nativePluginsEnabled: nativeSelection == "installed");
        var dependency = mod?.Dependencies.SingleOrDefault(row =>
            row.LogicalPath.Equals("nvse_1_4.dll", StringComparison.OrdinalIgnoreCase));
        if (nativeSelection == "installed" && dependency?.SourcePath is { } host)
        {
            var source = RuntimeLiveContentSource.Current ??
                throw new InvalidOperationException("Selected native dependency has no configured content owner.");
            source.BindNativePluginHostDependency(new(host,
                "actual-launcher-selected-stack-dependency:" + source.StackId));
        }
    }
}
