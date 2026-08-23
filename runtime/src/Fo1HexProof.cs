using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1HexProof
{
    internal static async Task Run(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var camera = loaded.Camera;
            var initialYaw = camera.TargetYawRadians;
            var initialPitch = camera.TargetPitchRadians;
            var initialSize = camera.TargetSizeMeters;
            var initialPosition = camera.Position;

            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Middle,
                Pressed = true,
            });
            camera._UnhandledInput(new InputEventMouseMotion
            {
                Relative = new Vector2(36.0f, -18.0f),
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Middle,
                Pressed = false,
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = true,
            });
            camera._UnhandledInput(new InputEventMouseMotion
            {
                Relative = new Vector2(52.0f, 24.0f),
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = false,
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.WheelUp,
                Pressed = true,
                Position = new Vector2(640.0f, 360.0f),
            });
            if (Mathf.IsEqualApprox(camera.TargetYawRadians, initialYaw) ||
                Mathf.IsEqualApprox(camera.TargetPitchRadians, initialPitch) ||
                camera.TargetSizeMeters >= initialSize ||
                camera.Position.IsEqualApprox(initialPosition) ||
                camera.OrbitDragging || camera.PanDragging)
                throw new InvalidOperationException("Fallout tactical mouse camera proof failed.");

            var target = Fo1HexMath.Neighbors(loaded.Session.PlayerTile)
                .FirstOrDefault(loaded.Session.CanWalk, -1);
            if (target < 0)
                throw new InvalidOperationException("V13ENT entry has no provisionally walkable adjacent hex.");
            var initialAp = loaded.Session.ActionPoints;
            loaded.Session.SelectTile(target);
            for (var frame = 0; frame < 180 && loaded.Session.PlayerTile != target; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (loaded.Session.PlayerTile != target || loaded.Session.ActionPoints != initialAp - 1)
                throw new InvalidOperationException("Fallout one-AP movement proof failed.");
            loaded.Session.EndTurn();
            if (loaded.Session.Turn != 2 || loaded.Session.ActionPoints != initialAp)
                throw new InvalidOperationException("Fallout end-turn AP restoration proof failed.");

            var report = new
            {
                schema = "opennv-fo1-tactical-proof/v1",
                status = "pass",
                sceneSha256 = loaded.SceneSha256,
                grid = new
                {
                    width = Fo1HexMath.Width,
                    height = Fo1HexMath.Height,
                    flatToFlatMeters = Fo1HexMath.FlatToFlatMeters,
                    layout = "odd-row-offset-pointy",
                },
                entryTile = loaded.EntryTile,
                movedToTile = target,
                moveDistanceMeters = Fo1HexMath.Distance(loaded.EntryTile, target),
                movementCostAp = 1,
                turnAfterEnd = loaded.Session.Turn,
                actionPointsAfterEnd = loaded.Session.ActionPoints,
                camera = new
                {
                    middleMouseOrbit = true,
                    rightMousePan = true,
                    wheelZoomTowardCursor = true,
                    initialYawDegrees = Mathf.RadToDeg(initialYaw),
                    resultingYawDegrees = Mathf.RadToDeg(camera.TargetYawRadians),
                    initialPitchDegrees = Mathf.RadToDeg(initialPitch),
                    resultingPitchDegrees = Mathf.RadToDeg(camera.TargetPitchRadians),
                    initialSizeMeters = initialSize,
                    resultingSizeMeters = camera.TargetSizeMeters,
                    panDeltaMeters = (camera.Position - initialPosition).Length(),
                },
                session = loaded.Session.Report(),
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                fullPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print($"OPENNV_FO1_TACTICAL_PROOF_PASS moved={loaded.EntryTile}->{target} ap=1");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_TACTICAL_PROOF_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }
}
