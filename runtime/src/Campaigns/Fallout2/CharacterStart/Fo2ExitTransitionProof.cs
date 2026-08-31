using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2ExitTransitionProof
{
    private const int GroundingFrames = 120;
    private const int MaximumMovementFrames = 420;
    private const int SettleFrames = 8;

    internal static async Task RunWrite(Fo2CharacterStartHost host, string proofRoot)
    {
        var pressed = false;
        try
        {
            var output = PrepareOutput(proofRoot, false);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 exit write proof requires an empty save boundary.");
            host.Picker.Select(0);
            host.Picker.ChooseCurrent();
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 exit proof did not enter Arroyo Caves.");
            var player = runtime.Player;
            var exit = host.Arroyo.LiveExit;
            var observedPath = new List<int> { player.CurrentTile };
            for (var frame = 0; frame < GroundingFrames && !player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                runtime.Profile.MoveBackward.PhysicalKey,
                true));
            pressed = true;
            var movementFrames = 0;
            for (; movementFrames < MaximumMovementFrames && host.TempleScene is null;
                 movementFrames++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (player.CurrentMapIndex == exit.SourceMapIndex &&
                    observedPath[^1] != player.CurrentTile)
                    observedPath.Add(player.CurrentTile);
            }
            if (host.LastTransition == exit && observedPath[^1] != exit.SourceTile)
                observedPath.Add(exit.SourceTile);
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                runtime.Profile.MoveBackward.PhysicalKey,
                false));
            pressed = false;
            for (var frame = 0; frame < SettleFrames; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var saved = host.PersistCurrentState();
            var framePath = System.IO.Path.Combine(output, "temple-arrival.png");
            var image = host.GetViewport().GetTexture().GetImage();
            image.SavePng(framePath);
            var temple = host.TempleScene;
            var descendants = temple is null
                ? Array.Empty<Node>()
                : Descendants(temple.Root).ToArray();
            var wallProxyMeshes = descendants
                .OfType<MeshInstance3D>()
                .Count(node => node.Name == "MOLDED_SOURCE_WALL_SHELL");
            var sourceWallSprites = descendants
                .OfType<Sprite3D>()
                .Where(node => node.HasMeta("source_object_type") &&
                    node.GetMeta("source_object_type").AsInt32() == 3)
                .ToArray();
            var sourceWallsVisible = temple is not null && wallProxyMeshes == 0 &&
                sourceWallSprites.Length == temple.Topology.WallSourceObjects &&
                sourceWallSprites.All(sprite => sprite.Visible && sprite.Texture is not null);
            var camera = player.GetChildren().OfType<Camera3D>().Single();
            var passed = host.LastTransition == exit && host.TempleScene is not null &&
                observedPath.SequenceEqual(exit.SourcePath) &&
                player.CurrentMapIndex == exit.TargetMapIndex &&
                player.CurrentElevation == exit.TargetElevation &&
                player.CurrentMapSha256 == exit.TargetMapSha256 &&
                player.CurrentTile == exit.TargetTile &&
                player.ArrivalTile == exit.TargetTile &&
                player.Presentation.Direction == exit.TargetRotation &&
                !player.Presentation.IsWalking && player.Presentation.AnimationCode == "AA" &&
                player.CanOccupy(exit.TargetTile) && player.IsOnFloor() &&
                host.TempleScene.MapSha256 == exit.TargetMapSha256 &&
                host.TempleScene.Topology.Movement.CanReachFromEntry(exit.TargetTile) &&
                saved.MapIndex == exit.TargetMapIndex &&
                saved.Elevation == exit.TargetElevation &&
                saved.ArrivalTile == exit.TargetTile &&
                saved.CurrentTile == exit.TargetTile &&
                saved.LastTransition == exit && saved.Sha256.Length == 64 &&
                sourceWallsVisible && camera.Size == player.CameraSizeMeters &&
                File.Exists(framePath) && FileSha256(framePath).Length == 64;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-exit-transition-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-exit-transition-write-proof/v1",
                    status = passed
                        ? "pass-source-exit-ordinary-movement-map126-save"
                        : "fail-fo2-source-exit-write",
                    source = new
                    {
                        mapIndex = exit.SourceMapIndex,
                        mapSha256 = exit.SourceMapSha256,
                        exit.ExitSerial,
                        exit.ExitFid,
                        exit.ExitPid,
                        tile = exit.SourceTile,
                        elevation = exit.SourceElevation,
                        path = exit.SourcePath,
                        pathSha256 = exit.SourcePathSha256,
                        observedPath,
                    },
                    destination = new
                    {
                        mapIndex = exit.TargetMapIndex,
                        logicalPath = exit.TargetLogicalPath,
                        mapSha256 = exit.TargetMapSha256,
                        tile = exit.TargetTile,
                        elevation = exit.TargetElevation,
                        rotation = exit.TargetRotation,
                        playerGrounded = player.IsOnFloor(),
                        playerVisible = player.Presentation.Visible,
                        animation = player.Presentation.AnimationCode,
                    },
                    save = new { saved.Path, saved.Sha256, schema = Fo2CharacterStartSaveState.Schema },
                    frame = new
                    {
                        path = framePath,
                        sha256 = FileSha256(framePath),
                        width = image.GetWidth(),
                        height = image.GetHeight(),
                    },
                    visual = new
                    {
                        wallProxyMeshes,
                        sourceWallSprites = sourceWallSprites.Length,
                        sourceWallsVisible,
                        cameraSizeMeters = camera.Size,
                        cameraDerivedSizeMeters = player.CameraSizeMeters,
                        cameraCompositionMode = runtime.Profile.CameraCompositionMode,
                        cameraSourcePixelScale = player.CameraSourcePixelScale,
                        cameraSourceFrameCropPixels = player.CameraSourceFrameCropPixels,
                        cameraWorldViewportHeightPixels =
                            player.CameraWorldViewportHeightPixels,
                        sourceFrmSpritesRetained = true,
                        hiddenSourceGeometry = false,
                    },
                    movementFrames,
                    ordinaryGroundedMovement = true,
                    debugTeleportUsed = false,
                    scriptsExecuted = false,
                    retailParity = false,
                });
            GD.Print(passed
                ? $"OPENNV_FO2_EXIT_WRITE_PASS serial={exit.ExitSerial} target={exit.TargetMapIndex}:{exit.TargetTile} save={saved.Path}"
                : $"OPENNV_FO2_EXIT_WRITE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_EXIT_WRITE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        finally
        {
            if (pressed && host.Runtime is not null)
                Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                    host.Runtime.Profile.MoveBackward.PhysicalKey,
                    false));
        }
    }

    internal static async Task RunRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, true);
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 exit cold restore has no runtime.");
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 exit cold restore has no validated save.");
            var exit = host.Arroyo.LiveExit;
            var exactInitialPosition = runtime.Player.Position.IsEqualApprox(saved.Position);
            var exactInitialTile = runtime.Player.CurrentTile == saved.CurrentTile;
            var exactInitialFacing = runtime.Player.Presentation.Direction == saved.Rotation;
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var passed = host.RestoredFromSave && host.LastTransition == exit &&
                host.TempleScene is not null && saved.LastTransition == exit &&
                exactInitialPosition && exactInitialTile && exactInitialFacing &&
                runtime.Player.CurrentMapIndex == exit.TargetMapIndex &&
                runtime.Player.CurrentElevation == exit.TargetElevation &&
                runtime.Player.CurrentMapSha256 == exit.TargetMapSha256 &&
                runtime.Player.CurrentWalkMaskSha256 == saved.WalkMaskSha256 &&
                runtime.Player.CurrentTile == exit.TargetTile &&
                runtime.Player.IsOnFloor() && runtime.Player.Presentation.Visible &&
                !runtime.Player.Presentation.IsWalking &&
                runtime.Player.Presentation.AnimationCode == "AA" &&
                saved.Sha256.Length == 64;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-exit-transition-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-exit-transition-restore-proof/v1",
                    status = passed
                        ? "pass-map126-source-transition-cold-restore"
                        : "fail-fo2-source-exit-cold-restore",
                    transition = new
                    {
                        exit.ExitSerial,
                        source = new { mapIndex = exit.SourceMapIndex, tile = exit.SourceTile },
                        target = new
                        {
                            mapIndex = exit.TargetMapIndex,
                            mapSha256 = exit.TargetMapSha256,
                            tile = exit.TargetTile,
                            elevation = exit.TargetElevation,
                            rotation = exit.TargetRotation,
                        },
                    },
                    restore = new
                    {
                        coldProcess = true,
                        exactInitialPosition,
                        exactInitialTile,
                        exactInitialFacing,
                        grounded = runtime.Player.IsOnFloor(),
                        ownedPresentationVisible = runtime.Player.Presentation.Visible,
                        idleAa = runtime.Player.Presentation.AnimationCode == "AA" &&
                            !runtime.Player.Presentation.IsWalking,
                    },
                    save = new { saved.Path, saved.Sha256, schema = Fo2CharacterStartSaveState.Schema },
                    debugTeleportUsed = false,
                    scriptsExecuted = false,
                    retailParity = false,
                });
            GD.Print(passed
                ? $"OPENNV_FO2_EXIT_RESTORE_PASS target={exit.TargetMapIndex}:{exit.TargetTile} save={saved.Path}"
                : $"OPENNV_FO2_EXIT_RESTORE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_EXIT_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static string PrepareOutput(string proofRoot, bool requireExisting)
    {
        var output = System.IO.Path.GetFullPath(proofRoot);
        if (File.Exists(output) || requireExisting != Directory.Exists(output))
            throw new InvalidOperationException(requireExisting
                ? $"Fallout 2 exit restore proof output is unavailable: {output}"
                : $"Refusing to overwrite Fallout 2 exit proof: {output}");
        if (!requireExisting)
            Directory.CreateDirectory(output);
        return output;
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void WriteReport(string path, object report) => File.WriteAllText(
        path,
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);
}
