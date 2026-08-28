using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1HexDemo
{
    internal static async Task Run(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        try
        {
            var fullReportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
            var stage = BuildStageBanner(host);
            await WaitForFrames(host, 45);

            stage.Text = "01  EXACT V13ENT ENTRY  •  3D VAULT 13 DIORAMA";
            loaded.Session.SetCameraStatus(
                "Exact V13ENT entry and Vault door • native Godot capture");
            loaded.Camera.SetOrbitDegrees(-45.0f, -49.0f);
            loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
            await WaitForFrames(host, 60);

            stage.Text = "02  OWNED 3D VAULT DWELLER  •  ORBIT + ZOOM";
            loaded.Session.SetCameraStatus(
                "Owned animated Vault 13 suit • source sprite retained for parity mode");
            loaded.Camera.SetOrbitDegrees(135.0f, -26.0f);
            loaded.Camera.FocusTileAtHeight(loaded.Session.PlayerTile, 3.0f, 0.86f);
            await WaitForFrames(host, 55);

            stage.Text = "03  MAPPED VAULT DOOR  •  SOURCE HEX 16290";
            loaded.Session.SetCameraStatus(
                "Mapped Vault door leaf and owned 3D cave-to-vault frame");
            loaded.Camera.SetOrbitDegrees(0.0f, -30.0f);
            loaded.Camera.FocusTileAtHeight(loaded.DoorTile, 7.5f, 2.0f);
            await WaitForFrames(host, 55);

            var combatTarget = loaded.Session.Mobs
                .Where(mob => mob.Alive)
                .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                .ThenBy(mob => mob.Serial)
                .First();
            var movementTarget = ChooseMovementTarget(
                loaded.Session,
                combatTarget.Tile,
                maximumSteps: 3);
            var initialTile = loaded.Session.PlayerTile;
            var initialAp = loaded.Session.ActionPoints;
            stage.Text = "04  TURN-BASED HEX MOVE  •  1 AP PER HEX";
            loaded.Session.ToggleGrid();
            loaded.Session.SetCameraStatus(
                "Walking three exact Fallout hexes toward the nearest source rat");
            loaded.Camera.SetOrbitDegrees(-38.0f, -40.0f);
            loaded.Camera.FocusTileAtHeight(initialTile, 5.0f, 0.68f);
            loaded.Session.SelectTile(movementTarget);
            await WaitUntilTile(
                host,
                loaded.Session,
                loaded.Camera,
                movementTarget,
                180);
            var apAfterMove = loaded.Session.ActionPoints;
            await WaitForFrames(host, 24);
            loaded.Session.ToggleGrid();

            stage.Text = "05  SOURCE RAT TARGET  •  OWNED ANIMATED 3D MODEL";
            loaded.Session.ActivateTile(combatTarget.Tile, false);
            loaded.Camera.SetOrbitDegrees(-45.0f, -38.0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, combatTarget.Tile);
            await WaitForFrames(host, 55);
            loaded.Camera.FocusTileAtHeight(combatTarget.Tile, 4.5f, 0.44f);
            await WaitForFrames(host, 45);

            var targetHpBefore = combatTarget.HitPoints;
            stage.Text = "06  10MM ATTACK  •  AP + HP + DEATH STATE";
            loaded.Session.SetCameraStatus(
                "Deterministic tactical attack using the live AP/HP combat session");
            loaded.Session.AttackSelected();
            await WaitForFrames(host, 55);

            stage.Text = "07  RAT TURN  •  SOURCE TEAMS + SEQUENCE ORDER";
            loaded.Session.EndTurn();
            var ratTurnTarget = loaded.Session.Mobs
                .Where(mob => mob.Alive)
                .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                .ThenBy(mob => mob.Serial)
                .First();
            loaded.Session.ActivateTile(ratTurnTarget.Tile, false);
            loaded.Camera.SetOrbitDegrees(-45.0f, -46.0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, ratTurnTarget.Tile);
            await WaitForFrames(host, 80);

            stage.Text = "PLAYABLE VERTICAL SLICE  •  EXACT HEXES  •  NATIVE 3D";
            loaded.Session.SetCameraStatus(
                "Mouse orbit • right-drag pan • wheel zoom • click-to-move • target + attack");
            loaded.Camera.SetOrbitDegrees(-45.0f, -49.0f);
            loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
            await WaitForFrames(host, 75);

            if (
                loaded.Session.PlayerTile != movementTarget ||
                initialAp - apAfterMove != Fo1HexMath.Distance(initialTile, movementTarget) ||
                loaded.Session.ActionPoints != initialAp ||
                loaded.Session.Turn != 2 ||
                loaded.Session.Attacks != 1 ||
                combatTarget.HitPoints >= targetHpBefore ||
                loaded.Session.PlayerMoveAnimationPlaybacks < 1 ||
                loaded.PlayerActor is null || loaded.PlayerActor.Value.Animations < 2)
                throw new InvalidOperationException("Fallout deterministic gameplay demo gate failed.");
            var report = new
            {
                schema = "opennv-fo1-gameplay-demo/v1",
                status = "pass",
                scene = loaded.ScenePath,
                sceneSha256 = loaded.SceneSha256,
                fixedFpsExpected = 30,
                entryTile = loaded.EntryTile,
                doorTile = loaded.DoorTile,
                movement = new
                {
                    fromTile = initialTile,
                    toTile = movementTarget,
                    distanceHexes = Fo1HexMath.Distance(initialTile, movementTarget),
                    actionPointsSpent = initialAp - apAfterMove,
                    playerMoveAnimation = "Forward",
                    moveAnimationPlaybacks = loaded.Session.PlayerMoveAnimationPlaybacks,
                },
                combat = new
                {
                    targetSerial = combatTarget.Serial,
                    targetPid = combatTarget.Pid,
                    hitPointsBefore = targetHpBefore,
                    hitPointsAfter = combatTarget.HitPoints,
                    attacks = loaded.Session.Attacks,
                    kills = loaded.Session.Kills,
                    turnAfterRatAi = loaded.Session.Turn,
                },
                player3d = new
                {
                    formId = loaded.PlayerActor.Value.FormId,
                    meshes = loaded.PlayerActor.Value.Meshes,
                    skeletons = loaded.PlayerActor.Value.Skeletons,
                    animations = loaded.PlayerActor.Value.Animations,
                    surfaces = loaded.PlayerActor.Value.AuthoredSurfaces,
                    textures = loaded.PlayerActor.Value.AuthoredTextures,
                    heightMeters = loaded.PlayerActor.Value.Bounds.Size.Y,
                },
                finalSession = loaded.Session.Report(),
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                fullReportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO1_GAMEPLAY_DEMO_PASS moved={initialTile}->{movementTarget} " +
                $"attacks={loaded.Session.Attacks} kills={loaded.Session.Kills} turn={loaded.Session.Turn}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_GAMEPLAY_DEMO_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static Label BuildStageBanner(Node host)
    {
        var layer = new CanvasLayer { Name = "Fo1GameplayDemoBanner", Layer = 60 };
        host.AddChild(layer);
        var background = new ColorRect
        {
            Position = new Vector2(20.0f, 18.0f),
            Size = new Vector2(650.0f, 44.0f),
            Color = new Color(0.015f, 0.02f, 0.012f, 0.90f),
        };
        layer.AddChild(background);
        var label = new Label
        {
            Position = new Vector2(32.0f, 26.0f),
            Size = new Vector2(630.0f, 32.0f),
            Text = "LOADING HASH-VERIFIED PLAYER-OWNED CONTENT",
            Modulate = new Color(1.0f, 0.80f, 0.18f),
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        layer.AddChild(label);
        return label;
    }

    private static int ChooseMovementTarget(
        Fo1TacticalSession session,
        int towardTile,
        int maximumSteps)
    {
        var current = session.PlayerTile;
        var visited = new HashSet<int> { current };
        for (var step = 0; step < maximumSteps; step++)
        {
            var next = Fo1HexMath.Neighbors(current)
                .Where(tile => session.CanWalk(tile) && !visited.Contains(tile))
                .OrderBy(tile => Fo1HexMath.Distance(tile, towardTile))
                .ThenBy(tile => tile)
                .FirstOrDefault(-1);
            if (next < 0)
                break;
            current = next;
            visited.Add(current);
        }
        if (current == session.PlayerTile)
            throw new InvalidOperationException("Fallout demo could not find a walkable movement target.");
        return current;
    }

    private static async Task WaitUntilTile(
        Node host,
        Fo1TacticalSession session,
        Fo1TacticalCamera camera,
        int tile,
        int maximumFrames)
    {
        for (var frame = 0; frame < maximumFrames && session.PlayerTile != tile; frame++)
        {
            camera.FocusWorldPoint(
                session.PlayerToken.GlobalPosition + Vector3.Up * 0.68f,
                4.2f,
                180.0f);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (session.PlayerTile != tile)
            throw new InvalidOperationException($"Fallout demo movement timed out at {session.PlayerTile}.");
    }

    private static async Task WaitForFrames(Node host, int count)
    {
        for (var frame = 0; frame < count; frame++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
