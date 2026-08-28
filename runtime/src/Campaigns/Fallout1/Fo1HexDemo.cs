using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1HexDemoNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float AcceptanceFloatNEgativE26Point0f = -26.0f;
    internal const float AcceptanceFloatNEgativE30Point0f = -30.0f;
    internal const float AcceptanceFloatNEgativE38Point0f = -38.0f;
    internal const float AcceptanceFloatNEgativE40Point0f = -40.0f;
    internal const float AcceptanceFloatNEgativE45Point0f = -45.0f;
    internal const float AcceptanceFloatNEgativE46Point0f = -46.0f;
    internal const float AcceptanceFloatNEgativE49Point0f = -49.0f;
    internal const float AcceptanceFloat0Point012f = 0.012f;
    internal const float AcceptanceFloat0Point015f = 0.015f;
    internal const float AcceptanceFloat0Point02f = 0.02f;
    internal const float AcceptanceFloat0Point18f = 0.18f;
    internal const float AcceptanceFloat0Point44f = 0.44f;
    internal const float AcceptanceFloat0Point68f = 0.68f;
    internal const float AcceptanceFloat0Point80f = 0.80f;
    internal const float AcceptanceFloat0Point86f = 0.86f;
    internal const float AcceptanceFloat0Point90f = 0.90f;
    internal const float AcceptanceFloat135Point0f = 135.0f;
    internal const int AcceptanceInt18 = 18;
    internal const float AcceptanceFloat18Point0f = 18.0f;
    internal const int AcceptanceInt180 = 180;
    internal const float AcceptanceFloat180Point0f = 180.0f;
    internal const float AcceptanceFloat20Point0f = 20.0f;
    internal const int AcceptanceInt24 = 24;
    internal const float AcceptanceFloat26Point0f = 26.0f;
    internal const int AcceptanceInt30 = 30;
    internal const float AcceptanceFloat32Point0f = 32.0f;
    internal const float AcceptanceFloat4Point2f = 4.2f;
    internal const float AcceptanceFloat4Point5f = 4.5f;
    internal const float AcceptanceFloat44Point0f = 44.0f;
    internal const int AcceptanceInt45 = 45;
    internal const float AcceptanceFloat5Point0f = 5.0f;
    internal const int AcceptanceInt55 = 55;
    internal const int AcceptanceInt60 = 60;
    internal const float AcceptanceFloat630Point0f = 630.0f;
    internal const float AcceptanceFloat650Point0f = 650.0f;
    internal const float AcceptanceFloat7Point5f = 7.5f;
    internal const int AcceptanceInt75 = 75;
    internal const int AcceptanceInt80 = 80;
}

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
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt45);

            stage.Text = "01  EXACT V13ENT ENTRY  •  3D VAULT 13 DIORAMA";
            loaded.Session.SetCameraStatus(
                "Exact V13ENT entry and Vault door • native Godot capture");
            loaded.Camera.SetOrbitDegrees(Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE45Point0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE49Point0f);
            loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt60);

            stage.Text = "02  OWNED 3D VAULT DWELLER  •  ORBIT + ZOOM";
            loaded.Session.SetCameraStatus(
                "Owned animated Vault 13 suit • source sprite retained for parity mode");
            loaded.Camera.SetOrbitDegrees(Fo1HexDemoNumericContracts.AcceptanceFloat135Point0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE26Point0f);
            loaded.Camera.FocusTileAtHeight(loaded.Session.PlayerTile, 3.0f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point86f);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt55);

            stage.Text = "03  MAPPED VAULT DOOR  •  SOURCE HEX 16290";
            loaded.Session.SetCameraStatus(
                "Mapped Vault door leaf and owned 3D cave-to-vault frame");
            loaded.Camera.SetOrbitDegrees(0.0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE30Point0f);
            loaded.Camera.FocusTileAtHeight(loaded.DoorTile, Fo1HexDemoNumericContracts.AcceptanceFloat7Point5f, 2.0f);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt55);

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
            loaded.Camera.SetOrbitDegrees(Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE38Point0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE40Point0f);
            loaded.Camera.FocusTileAtHeight(initialTile, Fo1HexDemoNumericContracts.AcceptanceFloat5Point0f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point68f);
            loaded.Session.SelectTile(movementTarget);
            await WaitUntilTile(
                host,
                loaded.Session,
                loaded.Camera,
                movementTarget,
                Fo1HexDemoNumericContracts.AcceptanceInt180);
            var apAfterMove = loaded.Session.ActionPoints;
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt24);
            loaded.Session.ToggleGrid();

            stage.Text = "05  SOURCE RAT TARGET  •  OWNED ANIMATED 3D MODEL";
            loaded.Session.ActivateTile(combatTarget.Tile, false);
            loaded.Camera.SetOrbitDegrees(Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE45Point0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE38Point0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, combatTarget.Tile);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt55);
            loaded.Camera.FocusTileAtHeight(combatTarget.Tile, Fo1HexDemoNumericContracts.AcceptanceFloat4Point5f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point44f);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt45);

            var targetHpBefore = combatTarget.HitPoints;
            stage.Text = "06  10MM ATTACK  •  AP + HP + DEATH STATE";
            loaded.Session.SetCameraStatus(
                "Deterministic tactical attack using the live AP/HP combat session");
            loaded.Session.AttackSelected();
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt55);

            stage.Text = "07  RAT TURN  •  SOURCE TEAMS + SEQUENCE ORDER";
            loaded.Session.EndTurn();
            var ratTurnTarget = loaded.Session.Mobs
                .Where(mob => mob.Alive)
                .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                .ThenBy(mob => mob.Serial)
                .First();
            loaded.Session.ActivateTile(ratTurnTarget.Tile, false);
            loaded.Camera.SetOrbitDegrees(Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE45Point0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE46Point0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, ratTurnTarget.Tile);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt80);

            stage.Text = "PLAYABLE VERTICAL SLICE  •  EXACT HEXES  •  NATIVE 3D";
            loaded.Session.SetCameraStatus(
                "Mouse orbit • right-drag pan • wheel zoom • click-to-move • target + attack");
            loaded.Camera.SetOrbitDegrees(Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE45Point0f, Fo1HexDemoNumericContracts.AcceptanceFloatNEgativE49Point0f);
            loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
            await WaitForFrames(host, Fo1HexDemoNumericContracts.AcceptanceInt75);

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
                fixedFpsExpected = Fo1HexDemoNumericContracts.AcceptanceInt30,
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
        var layer = new CanvasLayer { Name = "Fo1GameplayDemoBanner", Layer = Fo1HexDemoNumericContracts.AcceptanceInt60 };
        host.AddChild(layer);
        var background = new ColorRect
        {
            Position = new Vector2(Fo1HexDemoNumericContracts.AcceptanceFloat20Point0f, Fo1HexDemoNumericContracts.AcceptanceFloat18Point0f),
            Size = new Vector2(Fo1HexDemoNumericContracts.AcceptanceFloat650Point0f, Fo1HexDemoNumericContracts.AcceptanceFloat44Point0f),
            Color = new Color(Fo1HexDemoNumericContracts.AcceptanceFloat0Point015f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point02f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point012f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point90f),
        };
        layer.AddChild(background);
        var label = new Label
        {
            Position = new Vector2(Fo1HexDemoNumericContracts.AcceptanceFloat32Point0f, Fo1HexDemoNumericContracts.AcceptanceFloat26Point0f),
            Size = new Vector2(Fo1HexDemoNumericContracts.AcceptanceFloat630Point0f, Fo1HexDemoNumericContracts.AcceptanceFloat32Point0f),
            Text = "LOADING HASH-VERIFIED PLAYER-OWNED CONTENT",
            Modulate = new Color(1.0f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point80f, Fo1HexDemoNumericContracts.AcceptanceFloat0Point18f),
        };
        label.AddThemeFontSizeOverride("font_size", Fo1HexDemoNumericContracts.AcceptanceInt18);
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
                session.PlayerToken.GlobalPosition + Vector3.Up * Fo1HexDemoNumericContracts.AcceptanceFloat0Point68f,
                Fo1HexDemoNumericContracts.AcceptanceFloat4Point2f,
                Fo1HexDemoNumericContracts.AcceptanceFloat180Point0f);
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
