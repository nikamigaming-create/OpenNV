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
        if (launch.Is(RuntimeLaunchRoute.NativeOwnedData))
        {
            if (_nativeInstallation?.Game == NativeGame.Fallout1)
                LoadFallout1NativeInstall(_nativeInstallation.InstallRoot, RequireOption(_options, "save-path"));
            else if (_nativeInstallation?.Game == NativeGame.Fallout2)
                LoadFallout2NativeInstall(_nativeInstallation.InstallRoot);
            else
                LoadNativeOwnedStack();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Model))
        {
            SetLoadingStatus(
                _options.ContainsKey("classic-diorama")
                    ? "LOADING CLASSIC DIORAMA MODEL"
                    : "VERIFYING HASHED 3D MODEL");
            LoadModel(RequireOption(_options, "model"), RequireOption(_options, "sidecar"), _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.CellScene))
        {
            SetLoadingStatus(
                _options.ContainsKey("classic-diorama")
                    ? "LOADING CLASSIC DIORAMA CELL"
                    : "LOADING VERIFIED 3D CELL");
            LoadCellScene(RequireOption(_options, "cell-scene"), _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.StaticCellCompile))
        {
            LoadStaticCellCompile(
                RequireOption(_options, "static-cell-compile"),
                _options);
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.ActorModel))
        {
            SetLoadingStatus("VERIFYING HASHED ACTOR MODEL");
            LoadActorModel(
                RequireOption(_options, "actor-model"),
                RequireOption(_options, "actor-sidecar"),
                _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Fallout1NativeOwned))
        {
            LoadFallout1NativeOwned(
                RequireOption(_options, "fo1-owned-profile"),
                RequireOption(_options, "fo1-start-presentation"),
                RequireOption(_options, "save-path"));
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Fallout1HexScene))
        {
            SetLoadingStatus("LOADING V13ENT 200×200 HEX MAP");
            LoadFo1HexScene(RequireOption(_options, "fo1-hex-scene"), _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Fallout1CampaignTransport))
        {
            SetLoadingStatus("HASHING AND VALIDATING 96 MAP CONTRACTS");
            LoadFo1CampaignTransport(
                RequireOption(_options, "fo1-campaign-transport"),
                _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Fallout1CampaignPresentation))
        {
            SetLoadingStatus("VERIFYING ALL MAPS AND SOURCE ARTIFACTS");
            LoadFo1CampaignPresentation(
                RequireOption(_options, "fo1-campaign-presentation"),
                _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Fallout2TemplePresentation))
        {
            SetLoadingStatus("VERIFYING MAP 126 SOURCE AND PNG HASHES");
            LoadFo2TemplePresentation(
                RequireOption(_options, "fo2-temple-cache"),
                RequireOption(_options, "report"),
                _options.TryGetValue("fo2-temple-transitions", out var transitions)
                    ? transitions
                    : null);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.ActorReviewScene))
        {
            LoadActorReviewScene(
                RequireOption(_options, "actor-review-scene"),
                _options);
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.Fallout3Opening))
        {
            SetLoadingStatus("VERIFYING OWNED FALLOUT 3 CG00 CONTRACT");
            LoadFo3Opening(RequireOption(_options, "fo3-profile"), _options);
            DismissLoadingScreen();
            return true;
        }

        if (launch.Is(RuntimeLaunchRoute.TtwFallout3Opening))
        {
            SetLoadingStatus("APPLYING ISOLATED TTW CG00 TO CG01 STATE");
            LoadTtwFo3Opening(
                RequireOption(_options, "ttw-fo3-opening-profile"),
                _options);
            DismissLoadingScreen();
            return true;
        }

        return false;
    }

    private void LoadFallout2NativeInstall(string installRoot)
    {
        _options["fo2-install-root"] = installRoot;
        var scene = GD.Load<PackedScene>("res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn");
        AddChild(scene.Instantiate<Node3D>());
        DismissLoadingScreen();
    }
}
