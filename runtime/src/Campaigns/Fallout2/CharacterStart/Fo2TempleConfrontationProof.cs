using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2TempleConfrontationProof
{
    private const int GroundingFrames = 120;
    private const int MaximumMovementFrames = 420;
    private const int MaximumAttackAttempts = 100;

    internal static async Task RunWrite(Fo2CharacterStartHost host, string proofRoot)
    {
        var pressed = false;
        try
        {
            var output = PrepareOutput(proofRoot, false);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 confrontation write proof requires an empty save boundary.");
            host.Picker.Select(0);
            host.Picker.ChooseCurrent();
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 confrontation proof did not enter Arroyo Caves.");
            var player = runtime.Player;
            for (var frame = 0; frame < GroundingFrames && !player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);

            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                runtime.Profile.MoveBackward.PhysicalKey,
                true));
            pressed = true;
            for (var frame = 0;
                 frame < MaximumMovementFrames && host.TempleConfrontation is null;
                 frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                runtime.Profile.MoveBackward.PhysicalKey,
                false));
            pressed = false;

            var confrontation = host.TempleConfrontation ?? throw new InvalidOperationException(
                "Fallout 2 confrontation proof did not traverse the source exit route.");
            var targetTile = host.TempleScene is not null
                ? host.Temple.Confrontation.Critter.Tile
                : throw new InvalidOperationException(
                    "Fallout 2 confrontation proof has no Temple scene.");
            var adjacentTile = Fo1HexMath.Neighbors(targetTile)
                .Where(player.CanOccupy)
                .Order()
                .FirstOrDefault(-1);
            if (adjacentTile < 0)
                throw new InvalidOperationException(
                    "Fallout 2 confrontation target has no source-walkable adjacent hex.");
            player.Restore(
                adjacentTile,
                Fo1HexMath.Center(adjacentTile) +
                    Vector3.Up * runtime.Profile.SpawnCenterHeightMeters,
                player.Presentation.Direction);

            if (!confrontation.ToggleCombat())
                throw new InvalidOperationException(
                    "Fallout 2 confrontation could not enter bounded combat.");
            var attempts = 0;
            while (confrontation.State.TargetHitPoints > 0 &&
                attempts++ < MaximumAttackAttempts)
            {
                if (!confrontation.Attack() && !confrontation.EndTurn())
                    throw new InvalidOperationException(
                        "Fallout 2 confrontation could neither attack nor restore player AP.");
            }
            if (confrontation.State.TargetHitPoints != 0 || !confrontation.Loot())
                throw new InvalidOperationException(
                    "Fallout 2 confrontation did not reach exact defeat-to-loot state.");
            var saved = host.PersistCurrentState();
            var passed = saved.MapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                saved.TempleConfrontation == confrontation.State &&
                confrontation.State.SpearLooted && !confrontation.TargetVisible;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-temple-confrontation-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-temple-confrontation-write-proof/v1",
                    status = passed
                        ? "pass-bounded-defeat-loot-save"
                        : "fail-bounded-defeat-loot-save",
                    source = host.Temple.Confrontation,
                    state = confrontation.State,
                    player = new
                    {
                        mapIndex = player.CurrentMapIndex,
                        tile = player.CurrentTile,
                        adjacentToSourceTarget = Fo1HexMath.Distance(
                            player.CurrentTile,
                            targetTile) == 1,
                    },
                    save = new
                    {
                        saved.Path,
                        saved.Sha256,
                        schema = Fo2CharacterStartSaveState.Schema,
                    },
                    ordinarySourceExitTraversal = true,
                    proofSetupRepositionedToSourceWalkableAdjacentHex = true,
                    targetAiExecuted = false,
                    generalIntScriptsExecuted = false,
                    retailCombatParity = false,
                });
            GD.Print(passed
                ? $"OPENNV_FO2_CONFRONTATION_WRITE_PASS save={saved.Path}"
                : $"OPENNV_FO2_CONFRONTATION_WRITE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CONFRONTATION_WRITE_FAIL {exception}");
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

    internal static Task RunRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, true);
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 confrontation cold restore has no validated save.");
            var confrontation = host.TempleConfrontation ??
                throw new InvalidOperationException(
                    "Fallout 2 confrontation cold restore has no active Temple runtime.");
            var passed = host.RestoredFromSave && host.TempleScene is not null &&
                host.LastTransition == host.Arroyo.LiveExit &&
                saved.MapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                saved.TempleConfrontation == confrontation.State &&
                confrontation.State.TargetHitPoints == 0 &&
                confrontation.State.SpearLooted && !confrontation.TargetVisible;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-temple-confrontation-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-temple-confrontation-restore-proof/v1",
                    status = passed
                        ? "pass-cold-restore-defeated-looted-state"
                        : "fail-cold-restore-defeated-looted-state",
                    coldProcess = true,
                    state = confrontation.State,
                    targetVisible = confrontation.TargetVisible,
                    save = new
                    {
                        saved.Path,
                        saved.Sha256,
                        schema = Fo2CharacterStartSaveState.Schema,
                    },
                    targetAiExecuted = false,
                    generalIntScriptsExecuted = false,
                    retailCombatParity = false,
                });
            GD.Print(passed
                ? $"OPENNV_FO2_CONFRONTATION_RESTORE_PASS save={saved.Path}"
                : $"OPENNV_FO2_CONFRONTATION_RESTORE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CONFRONTATION_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        return Task.CompletedTask;
    }

    private static string PrepareOutput(string proofRoot, bool requireExisting)
    {
        var output = System.IO.Path.GetFullPath(proofRoot);
        if (File.Exists(output) || requireExisting != Directory.Exists(output))
            throw new InvalidOperationException(requireExisting
                ? $"Fallout 2 confrontation restore output is unavailable: {output}"
                : $"Refusing to overwrite Fallout 2 confrontation proof: {output}");
        if (!requireExisting)
            Directory.CreateDirectory(output);
        return output;
    }

    private static void WriteReport(string path, object report) => File.WriteAllText(
        path,
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);
}
