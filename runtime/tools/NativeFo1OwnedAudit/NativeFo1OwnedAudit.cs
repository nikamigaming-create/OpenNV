using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Fallout1.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public sealed partial class NativeFo1OwnedAudit : Node
{
    private const int ExpectedScrollBlockers = 351;
    private const int ExpectedCollisionShapes = 352;
    private const int ExpectedResolvedExitGrids = 5;
    private const int ExpectedDestinationMap = 6;
    private const int ExpectedDestinationTile = 17695;

    public override async void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            var option = Array.IndexOf(arguments, "--fo1-owned-profile");
            if (option < 0 || option + 1 >= arguments.Length)
                throw new InvalidOperationException("NativeFo1OwnedAudit requires --fo1-owned-profile.");
            var profilePath = Path.GetFullPath(arguments[option + 1]);
            var profileBefore = Snapshot(Path.GetDirectoryName(profilePath)!);
            var source = Fallout1OwnedContentSource.Load(profilePath);
            var installBefore = Snapshot(source.InstallRoot);
            var isolatedSavePath = Path.Combine(
                Path.GetDirectoryName(profilePath)!, "saves", "native-exit-audit.json");
            var coverage = Fallout1NativeV13Presentation.Build(this, source, isolatedSavePath);
            var interactions = coverage.Interactions;
            if (interactions.ScrollBlockerCount != ExpectedScrollBlockers ||
                interactions.CollisionShapeCount != ExpectedCollisionShapes ||
                interactions.ResolvedExitGridCount != ExpectedResolvedExitGrids ||
                !interactions.IsTileBlocked(interactions.SecurityDoorTile))
                throw new InvalidDataException("V13ENT collision/interaction source counts differ.");
            var adjacentDoorTile = Fo1HexMath.Neighbors(interactions.SecurityDoorTile).First();
            if (!interactions.TryActivateSecurityDoor(adjacentDoorTile) ||
                interactions.IsTileBlocked(interactions.SecurityDoorTile) ||
                interactions.TryActivateSecurityDoor(adjacentDoorTile))
                throw new InvalidDataException("V13ENT Security Door activation state is invalid.");
            var exitSourceTile = interactions.ResolvedExitSourceTiles.First();
            if (!interactions.TryConsumeResolvedExitGrid(exitSourceTile, out var exit) || exit is null ||
                exit.DestinationMap != ExpectedDestinationMap ||
                exit.DestinationTile != ExpectedDestinationTile ||
                exit.DestinationElevation != 0 || exit.DestinationRotation != 0)
                throw new InvalidDataException("V13ENT resolved Exit Grid interaction differs.");
            var arrival = interactions.CommitResolvedExitGrid(exitSourceTile);
            if (arrival.SaveCompatibilityId != $"fallout1:{source.ProfileId}" ||
                arrival.IsolatedSavePath != Path.GetFullPath(isolatedSavePath) ||
                arrival.MapIndex != ExpectedDestinationMap ||
                arrival.MapName != "VAULT13.MAP" ||
                arrival.MapLogicalPath != "maps\\vault13.map" ||
                arrival.Tile != ExpectedDestinationTile || arrival.Elevation != 0 || arrival.Rotation != 0 ||
                arrival.MapSha256.Length != 64 || interactions.AuthoritativePlayerArrival != arrival)
                throw new InvalidDataException("Native VAULT13 authoritative arrival state differs.");
            try
            {
                interactions.ExecuteDestinationScript(0);
                throw new InvalidDataException("Destination script execution did not fail closed.");
            }
            catch (NotSupportedException)
            {
                // Exact expected boundary: destination MAP loads, its scripts do not execute.
            }

            var looseFrame = source.Read("art\\tiles\\grid000.frm");
            if (!looseFrame.Source.StartsWith("loose:data:", StringComparison.Ordinal) ||
                Fallout1NativeFrmReader.ReadFirstFrame(looseFrame.Bytes).Width == 0)
                throw new InvalidDataException("Fallout 1 loose DATA precedence was not exercised.");
            var critterPath = source.FirstArchiveMember("critter.dat");
            if (!source.Read(critterPath).Source.StartsWith("dat1:critter.dat:", StringComparison.Ordinal))
                throw new InvalidDataException("Fallout 1 critter DAT precedence was not exercised.");

            if (DisplayServer.GetName() == "headless")
            {
                VerifyNoWrites(profilePath, profileBefore, source, installBefore);
                GD.Print(
                    $"OPENNV_FO1_NATIVE_SCENE_PASS profile={coverage.ProfileId} " +
                    $"floorPatches={coverage.FloorPatches} floorMeshes={coverage.FloorMeshes} " +
                    $"floorFrms={coverage.FloorFrmResources} objectFrms={coverage.ObjectFrmResources} " +
                    $"admittedObjects={coverage.AdmittedObjects} deferredObjects={coverage.DeferredObjects} " +
                    $"semanticObjects={coverage.SemanticObjects} " +
                    $"unclassifiedObjects={coverage.UnclassifiedObjects} " +
                    $"nestedInventoryObjects={coverage.NestedInventoryObjects} " +
                    $"liveMapScripts={coverage.LiveMapScripts} " +
                    $"unboundLiveMapScripts={coverage.UnboundLiveMapScripts} " +
                    $"scrollCollision={interactions.ScrollBlockerCount} " +
                    $"collisionShapes={interactions.CollisionShapeCount} " +
                    $"doorActivated={interactions.SecurityDoorOpen} " +
                    $"resolvedExitConsumed={exit.SourceTile}->{exit.DestinationMap}:{exit.DestinationTile} " +
                    $"destinationLoaded={arrival.MapName}:{arrival.Tile}:{arrival.Elevation}:{arrival.Rotation} " +
                    $"destinationScripts={arrival.LiveMapScripts}:fail-closed saveIdentity={arrival.SaveCompatibilityId} " +
                    $"semanticBuckets={coverage.SemanticBuckets} preparedInputs=0 writes=0 " +
                    "render=not-attempted-dummy-driver");
                GetTree().Quit();
                return;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var image = GetViewport().GetTexture().GetImage();
            var pixels = image.GetData();
            var visiblePixels = 0;
            for (var offset = 0; offset < pixels.Length; offset += 4)
                if (pixels[offset] > 8 || pixels[offset + 1] > 8 || pixels[offset + 2] > 8)
                    visiblePixels++;
            if (visiblePixels == 0)
                throw new InvalidOperationException("The direct Fallout 1 presentation rendered no visible pixels.");
            VerifyNoWrites(profilePath, profileBefore, source, installBefore);

            GD.Print(
                $"OPENNV_FO1_NATIVE_RENDER_PASS profile={coverage.ProfileId} " +
                $"mapSha256={coverage.MapSha256} elevation={coverage.Elevation} " +
                $"arrivalTile={coverage.ArrivalTile} floorPatches={coverage.FloorPatches} " +
                $"floorMeshes={coverage.FloorMeshes} floorFrms={coverage.FloorFrmResources} " +
                $"objectFrms={coverage.ObjectFrmResources} admittedObjects={coverage.AdmittedObjects} " +
                $"deferredObjects={coverage.DeferredObjects} " +
                $"semanticObjects={coverage.SemanticObjects} " +
                $"unclassifiedObjects={coverage.UnclassifiedObjects} " +
                $"nestedInventoryObjects={coverage.NestedInventoryObjects} " +
                $"liveMapScripts={coverage.LiveMapScripts} " +
                $"unboundLiveMapScripts={coverage.UnboundLiveMapScripts} " +
                $"scrollCollision={interactions.ScrollBlockerCount} " +
                $"collisionShapes={interactions.CollisionShapeCount} " +
                $"doorActivated={interactions.SecurityDoorOpen} " +
                $"resolvedExitConsumed={exit.SourceTile}->{exit.DestinationMap}:{exit.DestinationTile} " +
                $"destinationLoaded={arrival.MapName}:{arrival.Tile}:{arrival.Elevation}:{arrival.Rotation} " +
                $"destinationScripts={arrival.LiveMapScripts}:fail-closed saveIdentity={arrival.SaveCompatibilityId} " +
                $"semanticBuckets={coverage.SemanticBuckets} " +
                $"firstObjectFrm={coverage.FirstObjectFrm} visiblePixels={visiblePixels} " +
                $"viewport={image.GetWidth()}x{image.GetHeight()} preparedInputs=0 writes=0 " +
                "blockers=script-execution,interaction,gameplay");
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_FO1_NATIVE_RENDER_FAIL {error.Message}");
            GetTree().Quit(2);
            return;
        }
        GetTree().Quit();
    }

    private static void VerifyNoWrites(
        string profilePath,
        string[] profileBefore,
        Fallout1OwnedContentSource source,
        string[] installBefore)
    {
        if (!profileBefore.SequenceEqual(Snapshot(Path.GetDirectoryName(profilePath)!)) ||
            !installBefore.SequenceEqual(Snapshot(source.InstallRoot)))
            throw new InvalidOperationException("Fallout 1 native Godot audit wrote into profile or install roots.");
    }

    private static string[] Snapshot(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return $"{Path.GetRelativePath(root, path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            })
            .OrderBy(value => value, StringComparer.Ordinal).ToArray()
        : [];
}
