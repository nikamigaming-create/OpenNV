using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Fallout1.Native;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void LoadFallout1NativeOwned(
        string profilePath,
        string presentation,
        string isolatedSavePath)
    {
        SetLoadingStatus("READING V13ENT MAP / PRO / FRM DIRECTLY");
        var source = Fallout1OwnedContentSource.Load(profilePath);
        if (presentation != "hex-tactical")
            throw new NotSupportedException(
                $"Fallout 1 native {presentation} remains unavailable; only the bounded direct V13ENT " +
                "floor/evidenced-static-object presentation is admitted.");
        var coverage = Fallout1NativeV13Presentation.Build(this, source, isolatedSavePath);
        DismissLoadingScreen();
        GD.Print(
            $"OPENNV_FO1_NATIVE_PRESENTATION_READY profile={coverage.ProfileId} " +
            $"floorPatches={coverage.FloorPatches} floorMeshes={coverage.FloorMeshes} " +
            $"floorFrms={coverage.FloorFrmResources} objectFrms={coverage.ObjectFrmResources} " +
            $"admittedObjects={coverage.AdmittedObjects} deferredObjects={coverage.DeferredObjects} " +
            $"semanticObjects={coverage.SemanticObjects} " +
            $"unclassifiedObjects={coverage.UnclassifiedObjects} " +
            $"nestedInventoryObjects={coverage.NestedInventoryObjects} " +
            $"liveMapScripts={coverage.LiveMapScripts} " +
            $"unboundLiveMapScripts={coverage.UnboundLiveMapScripts} " +
            $"scrollCollision={coverage.Interactions.ScrollBlockerCount} " +
            $"collisionShapes={coverage.Interactions.CollisionShapeCount} " +
            $"securityDoor=closed-active " +
            $"resolvedExitGrids={coverage.Interactions.ResolvedExitGridCount} " +
            $"saveBound={coverage.Interactions.IsolatedSavePath is not null} " +
            $"semanticBuckets={coverage.SemanticBuckets} firstObjectFrm={coverage.FirstObjectFrm} " +
            "preparedInputs=0 writes=0 gameplay=fail-closed");
        if (DisplayServer.GetName() == "headless")
            GetTree().Quit();
    }

    private void LoadFallout1NativeInstall(string installRoot, string isolatedSavePath)
    {
        SetLoadingStatus("READING V13ENT MAP / PRO / FRM DIRECTLY");
        var source = Fallout1OwnedContentSource.LoadInstall(installRoot);
        var coverage = Fallout1NativeV13Presentation.Build(this, source, isolatedSavePath);
        DismissLoadingScreen();
        GD.Print($"OPENNV_FO1_NATIVE_INSTALL_READY profile={coverage.ProfileId} preparedInputs=0 writes=0");
        if (DisplayServer.GetName() == "headless")
            GetTree().Quit();
    }
}
