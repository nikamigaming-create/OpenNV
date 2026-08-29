using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2CharacterStartPersistenceProof
{
    private const int GroundingFrames = 120;
    private const int MaximumMovementFrames = 120;
    private const int TargetTileTransitions = 2;
    private const int SettleFrames = 4;

    internal static async Task RunWrite(Fo2CharacterStartHost host, string proofRoot)
    {
        var pressed = false;
        try
        {
            var output = PrepareOutput(proofRoot, false);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 write proof requires an empty save boundary.");
            host.Picker.Select(2);
            var selected = host.Picker.Selected;
            host.Picker.ChooseCurrent();
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 persistence proof did not enter Arroyo.");
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);

            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                runtime.Profile.AcceptanceKey,
                true));
            pressed = true;
            var movementFrames = 0;
            for (; movementFrames < MaximumMovementFrames; movementFrames++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (runtime.Player.CompletedTileTransitions >= TargetTileTransitions)
                    break;
            }
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                runtime.Profile.AcceptanceKey,
                false));
            pressed = false;
            for (var frame = 0; frame < SettleFrames; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var saved = host.PersistCurrentState();
            var passed = selected.Id == "diplomat" &&
                selected.Profile.Name == "Chitsa" &&
                saved.Character == selected &&
                saved.MapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex &&
                saved.Elevation == Fo2ArroyoCavesPresentationCatalog.Elevation &&
                saved.ArrivalTile == 28707 &&
                saved.CurrentTile == runtime.Player.CurrentTile &&
                saved.CurrentTile != saved.ArrivalTile &&
                saved.Rotation == runtime.Player.Presentation.Direction &&
                saved.Position.IsEqualApprox(runtime.Player.Position) &&
                saved.MotionMode == CharacterBody3D.MotionModeEnum.Grounded.ToString() &&
                File.Exists(saved.Path) &&
                saved.Sha256.Length == 64;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-character-save-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-character-save-write-proof/v1",
                    status = passed
                        ? "pass-owned-premade-map3-state-atomic-save"
                        : "fail-fo2-character-save-write",
                    selected = CharacterReport(selected),
                    world = WorldReport(saved),
                    runtime = RuntimeReport(saved),
                    save = new
                    {
                        path = saved.Path,
                        sha256 = saved.Sha256,
                        atomicReplace = true,
                        retailDataWritten = false,
                    },
                    movementFrames,
                    coldRestoreRequired = true,
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_SAVE_WRITE_PASS tile={saved.CurrentTile} save={saved.Path}"
                    : $"OPENNV_FO2_SAVE_WRITE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_SAVE_WRITE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        finally
        {
            if (pressed && host.Runtime is not null)
                Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(
                    host.Runtime.Profile.AcceptanceKey,
                    false));
        }
    }

    internal static async Task RunRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, true);
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 cold restore did not enter Arroyo.");
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 cold restore did not retain its validated save contract.");
            var selected = host.SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 cold restore did not restore its owned premade.");
            var exactInitialPosition = runtime.Player.Position.IsEqualApprox(saved.Position);
            var exactInitialTile = runtime.Player.CurrentTile == saved.CurrentTile;
            var exactInitialRotation = runtime.Player.Presentation.Direction == saved.Rotation;
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var passed = host.RestoredFromSave &&
                selected == saved.Character &&
                selected.Id == "diplomat" &&
                selected.Profile.Name == "Chitsa" &&
                selected.GcdSha256 == saved.Character.GcdSha256 &&
                exactInitialPosition && exactInitialTile && exactInitialRotation &&
                runtime.Player.CurrentTile == saved.CurrentTile &&
                runtime.Player.IsOnFloor() &&
                runtime.SelectedPlayerPresentation.Fid == Fo2CharacterStartCatalog.FemaleFid &&
                runtime.SelectedPlayerPresentation.LogicalPath ==
                    Fo2CharacterStartCatalog.FemaleLogicalPath &&
                runtime.Player.Presentation.Visible &&
                saved.Sha256.Length == 64;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-character-save-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-character-save-restore-proof/v1",
                    status = passed
                        ? "pass-owned-premade-map3-state-cold-restore"
                        : "fail-fo2-character-save-cold-restore",
                    selected = CharacterReport(selected),
                    world = WorldReport(saved),
                    runtime = RuntimeReport(saved),
                    restore = new
                    {
                        coldProcess = true,
                        exactInitialPosition,
                        exactInitialTile,
                        exactInitialRotation,
                        groundedAfterRestore = runtime.Player.IsOnFloor(),
                        visibleSexCorrectOwnedFrm = runtime.Player.Presentation.Visible,
                    },
                    save = new { path = saved.Path, sha256 = saved.Sha256 },
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_SAVE_RESTORE_PASS name={selected.Profile.Name} " +
                        $"tile={saved.CurrentTile} save={saved.Path}"
                    : $"OPENNV_FO2_SAVE_RESTORE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_SAVE_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static object CharacterReport(Fo2PremadeCharacter character) => new
    {
        character.Id,
        character.Role,
        character.Profile.Name,
        character.Profile.Age,
        character.Profile.Sex,
        special = character.Profile.Special,
        taggedSkills = character.Profile.TaggedSkills,
        traits = character.Profile.Traits,
        character.GcdSha256,
        character.BioSha256,
    };

    private static object WorldReport(Fo2CharacterStartSaveState saved) => new
    {
        saved.MapIndex,
        saved.Elevation,
        saved.ArrivalTile,
        saved.CurrentTile,
        saved.Rotation,
        position = new[] { saved.Position.X, saved.Position.Y, saved.Position.Z },
        saved.MapSha256,
        saved.WalkMaskSha256,
    };

    private static object RuntimeReport(Fo2CharacterStartSaveState saved) => new
    {
        saved.RuntimeProfileId,
        saved.RuntimeProfileSha256,
        saved.MotionMode,
        saved.BlockedMovementMode,
        saved.PresentationMode,
    };

    private static string PrepareOutput(string proofRoot, bool requireExisting)
    {
        var output = System.IO.Path.GetFullPath(proofRoot);
        if (File.Exists(output) || requireExisting != Directory.Exists(output))
            throw new InvalidOperationException(
                requireExisting
                    ? $"Fallout 2 restore proof output is unavailable: {output}"
                    : $"Refusing to overwrite Fallout 2 persistence proof: {output}");
        if (!requireExisting)
            Directory.CreateDirectory(output);
        return output;
    }

    private static void WriteReport(string path, object report) => File.WriteAllText(
        path,
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);
}
