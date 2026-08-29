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
            host.Picker.TogglePortraitMode();
            var appearanceIdentities = new List<object>();
            for (var index = 0; index < host.CharacterStart.Characters.Count; index++)
            {
                host.Picker.Select(index);
                var character = host.Picker.Selected;
                var relief = host.Picker.PortraitRelief;
                if (!host.Picker.Live3DVisible || relief.CharacterId != character.Id ||
                    relief.SourcePanelSha256 != character.Panel.SourceSha256 ||
                    relief.LocalPanelPngSha256 != character.Panel.PngSha256 ||
                    relief.SurfaceCount != 1)
                    throw new InvalidOperationException(
                        "Fallout 2 distinct owned panel relief identity failed.");
                appearanceIdentities.Add(new
                {
                    character.Id,
                    character.Profile.Name,
                    character.Panel.LogicalPath,
                    character.Panel.SourceSha256,
                    character.Panel.PngSha256,
                    relief.SurfaceCount,
                });
            }
            if (host.CharacterStart.Characters
                    .Select(character => character.Panel.SourceSha256)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != host.CharacterStart.Characters.Count)
                throw new InvalidOperationException(
                    "Fallout 2 premade source panels are not distinct.");
            host.Picker.TogglePortraitMode();
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
            var stateBeforeInventory = confrontation.State;
            var saveBeforeInventory = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 inventory proof has no saved post-loot state.");
            var positionBeforeInventory = player.Position;
            var tileBeforeInventory = player.CurrentTile;
            var rotationBeforeInventory = player.Presentation.Direction;
            if (!InputMap.HasAction(confrontation.InventoryAction) ||
                !InputMap.ActionGetEvents(confrontation.InventoryAction)
                    .OfType<InputEventKey>()
                    .Any(row => row.PhysicalKeycode == confrontation.InventoryPhysicalKey))
                throw new InvalidOperationException(
                    "Fallout 2 inventory action is not configured from the runtime profile.");
            await PressAction(host, confrontation.InventoryAction);
            if (!confrontation.InventoryVisible)
                throw new InvalidOperationException(
                    "Fallout 2 configured inventory action did not open the screen.");
            if (confrontation.InventorySourceLogicalPath !=
                    "art\\intrface\\invbox.frm" ||
                confrontation.InventorySourceSha256 !=
                    "ae347b83f24d00fbf5806f80a9084855d6ae275f31388cfabee90b700903a657")
                throw new InvalidOperationException(
                    "Fallout 2 inventory screen lost its owned INVBOX FRM identity.");
            if (!confrontation.InventoryCharacterText.Contains(
                    host.SelectedCharacter?.Profile.Name ?? "",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Fallout 2 inventory screen did not show the selected character.");
            if (!confrontation.InventorySpearSelected ||
                !confrontation.InventoryItemText.Contains(
                    $"{host.Temple.Confrontation.DefeatLoot.Quantity} × SPEAR",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Fallout 2 inventory screen did not select the exact Spear stack: " +
                    confrontation.InventoryItemText.Replace('\n', '|'));
            if (confrontation.State != stateBeforeInventory ||
                host.CurrentSave?.Sha256 != saveBeforeInventory.Sha256)
                throw new InvalidOperationException(
                    "Fallout 2 opening/selecting inventory changed gameplay or save state.");

            await PressAction(host, confrontation.InventoryInspectAction);
            if (!confrontation.InventoryInspectionVisible ||
                !confrontation.InventoryInspectionText.Contains(
                    $"PID {host.Temple.Confrontation.DefeatLoot.Pid}",
                    StringComparison.Ordinal) ||
                !confrontation.InventoryInspectionText.Contains("DMG 3–10", StringComparison.Ordinal) ||
                confrontation.State != stateBeforeInventory ||
                host.CurrentSave?.Sha256 != saveBeforeInventory.Sha256)
                throw new InvalidOperationException(
                    "Fallout 2 Spear inspection changed state or lost exact weapon data.");

            await PressAction(host, confrontation.InventoryEquipAction);
            var equippedState = stateBeforeInventory with { SpearEquipped = true };
            var equippedSave = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 equipped Spear state was not persisted.");
            if (confrontation.State != equippedState ||
                equippedSave.TempleConfrontation != equippedState ||
                equippedSave.Sha256 == saveBeforeInventory.Sha256 ||
                !confrontation.InventoryItemText.Contains("[EQUIPPED]", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Fallout 2 Spear equip did not persist its sole intended state change.");

            await PressAction(host, confrontation.InventoryEquipAction);
            if (confrontation.State != stateBeforeInventory ||
                !confrontation.InventoryItemText.Contains("[UNEQUIPPED]", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Fallout 2 Spear unequip did not restore the exact prior equipment state.");
            await PressAction(host, confrontation.InventoryEquipAction);
            equippedSave = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 final equipped Spear state was not persisted.");
            if (confrontation.State != equippedState ||
                equippedSave.TempleConfrontation != equippedState ||
                player.CurrentTile != tileBeforeInventory ||
                player.Position != positionBeforeInventory ||
                player.Presentation.Direction != rotationBeforeInventory ||
                !SameNonEquipmentState(confrontation.State, stateBeforeInventory))
                throw new InvalidOperationException(
                    "Fallout 2 equipment interaction changed AP, combat, loot, or world state.");
            var selectedSpear = confrontation.InventorySpearSelected;
            var escape = new InputEventKey
            {
                Keycode = Key.Escape,
                PhysicalKeycode = Key.Escape,
                Pressed = true,
            };
            host._UnhandledKeyInput(escape);
            if (confrontation.InventoryVisible || confrontation.State != equippedState ||
                host.CurrentSave?.Sha256 != equippedSave.Sha256 ||
                host.CurrentSave?.TempleConfrontation != equippedState)
                throw new InvalidOperationException(
                    "Fallout 2 inventory open/close changed gameplay or save state.");
            var saved = host.PersistCurrentState();
            var passed = saved.MapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                saved.TempleConfrontation == confrontation.State &&
                confrontation.State.SpearLooted && confrontation.State.SpearEquipped &&
                !confrontation.TargetVisible;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-temple-confrontation-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-temple-confrontation-write-proof/v1",
                    status = passed
                        ? "pass-bounded-defeat-loot-inventory-equip-save"
                        : "fail-bounded-defeat-loot-inventory-equip-save",
                    source = host.Temple.Confrontation,
                    appearance = new
                    {
                        contract = saved.Character.Appearance,
                        distinctOwnedPanelReliefs = appearanceIdentities,
                        originalPickerPreserved = true,
                        fullHeadGeometryRebuilt = false,
                        customFaceEditorImplemented = false,
                        customPortraitGenerated = false,
                    },
                    state = confrontation.State,
                    inventory = new
                    {
                        action = confrontation.InventoryAction,
                        physicalKey = confrontation.InventoryPhysicalKey.ToString(),
                        sourceLogicalPath = confrontation.InventorySourceLogicalPath,
                        sourceSha256 = confrontation.InventorySourceSha256,
                        character = confrontation.InventoryCharacterText,
                        items = confrontation.InventoryItemText,
                        inspection = confrontation.InventoryInspectionText,
                        selectedSpear,
                        inspectionExercised = true,
                        equipAndUnequipExercised = true,
                        finalSpearEquipped = confrontation.State.SpearEquipped,
                        closedByEscape = !confrontation.InventoryVisible,
                        nonEquipmentStateUnchanged = SameNonEquipmentState(
                            confrontation.State,
                            stateBeforeInventory),
                        worldStateUnchanged = player.CurrentTile == tileBeforeInventory &&
                            player.Position == positionBeforeInventory &&
                            player.Presentation.Direction == rotationBeforeInventory,
                        openInspectCloseSaveUnchanged = equippedSave.Sha256 ==
                            host.CurrentSave?.Sha256,
                    },
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

    internal static async Task RunRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, true);
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 confrontation cold restore has no validated save.");
            var confrontation = host.TempleConfrontation ??
                throw new InvalidOperationException(
                    "Fallout 2 confrontation cold restore has no active Temple runtime.");
            var restoredState = confrontation.State;
            var restoredSaveSha256 = saved.Sha256;
            await PressAction(host, confrontation.InventoryAction);
            var restoredInventory = confrontation.InventoryVisible &&
                confrontation.InventorySpearSelected &&
                confrontation.InventoryItemText.Contains("[EQUIPPED]", StringComparison.Ordinal);
            host._UnhandledKeyInput(new InputEventKey
            {
                Keycode = Key.Escape,
                PhysicalKeycode = Key.Escape,
                Pressed = true,
            });
            var passed = host.RestoredFromSave && host.TempleScene is not null &&
                host.LastTransition == host.Arroyo.LiveExit &&
                saved.MapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                saved.TempleConfrontation == confrontation.State &&
                confrontation.State.TargetHitPoints == 0 &&
                confrontation.State.SpearLooted && confrontation.State.SpearEquipped &&
                restoredInventory && !confrontation.InventoryVisible &&
                confrontation.State == restoredState &&
                host.CurrentSave?.Sha256 == restoredSaveSha256 &&
                !confrontation.TargetVisible;
            saved.Character.Appearance.Validate(saved.Character);
            WriteReport(
                System.IO.Path.Combine(output, "fo2-temple-confrontation-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-temple-confrontation-restore-proof/v1",
                    status = passed
                        ? "pass-cold-restore-defeated-looted-equipped-state"
                        : "fail-cold-restore-defeated-looted-equipped-state",
                    coldProcess = true,
                    state = confrontation.State,
                    appearance = saved.Character.Appearance,
                    targetVisible = confrontation.TargetVisible,
                    inventory = new
                    {
                        restoredEquippedSelection = restoredInventory,
                        closedByEscape = !confrontation.InventoryVisible,
                        stateUnchangedByOpenClose = confrontation.State == restoredState,
                        saveSha256Unchanged = host.CurrentSave?.Sha256 == restoredSaveSha256,
                    },
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
    }

    private static async Task PressAction(Fo2CharacterStartHost host, string action)
    {
        Input.ActionPress(action);
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        finally
        {
            Input.ActionRelease(action);
        }
    }

    private static bool SameNonEquipmentState(
        Fo2TempleConfrontationState left,
        Fo2TempleConfrontationState right) =>
        left.TargetHitPoints == right.TargetHitPoints &&
        left.PlayerActionPoints == right.PlayerActionPoints &&
        left.CombatActive == right.CombatActive &&
        left.SpearLooted == right.SpearLooted;

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
