using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2TempleConfrontationProof
{
    private const int GroundingFrames = 120;
    private const int MaximumMovementFrames = 420;
    private const int MaximumAttackAttempts = 100;

    internal static Task RunWrite(Fo2CharacterStartHost host, string proofRoot) =>
        RunWrite(host, proofRoot, integratedOnly: false);

    internal static Task RunIntegratedWrite(Fo2CharacterStartHost host, string proofRoot) =>
        RunWrite(host, proofRoot, integratedOnly: true);

    private static async Task RunWrite(
        Fo2CharacterStartHost host,
        string proofRoot,
        bool integratedOnly)
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
                var preview = host.Picker.HumanoidPreview;
                if (!host.Picker.Live3DVisible || preview.CharacterId != character.Id ||
                    preview.SourcePanelSha256 != character.Panel.SourceSha256 ||
                    preview.LocalPanelPngSha256 != character.Panel.PngSha256 ||
                    preview.SurfaceCount < 10 ||
                    !preview.UsesOwnedDonor)
                    throw new InvalidOperationException(
                        "Fallout 2 source-bound humanoid preview identity failed.");
                appearanceIdentities.Add(new
                {
                    character.Id,
                    character.Profile.Name,
                    character.Panel.LogicalPath,
                    character.Panel.SourceSha256,
                    character.Panel.PngSha256,
                    preview.SurfaceCount,
                    preview.PresentationMode,
                    preview.UsesOwnedDonor,
                });
            }
            if (host.CharacterStart.Characters
                    .Select(character => character.Panel.SourceSha256)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != host.CharacterStart.Characters.Count)
                throw new InvalidOperationException(
                    "Fallout 2 premade source panels are not distinct.");
            host.Picker.TogglePortraitMode();
            var selected = host.CharacterStart.Characters
                .Select((character, index) => (Character: character, Index: index))
                .Where(row => row.Character.Profile.TaggedSkills.Contains(
                    host.TrialRoute.Cameron.TaggedSpeechBranch.RequiredTaggedSkill,
                    StringComparer.Ordinal))
                .Where(row => Fo2TempleConfrontationRuntime.EffectiveIntelligence(
                    new Fo2CharacterSelection(
                        Fo2CharacterSelection.PremadeMode,
                        row.Character,
                        row.Character.Profile)) >=
                    host.TrialRoute.Cameron.TaggedSpeechBranch.MinimumIntelligence)
                .OrderBy(row => row.Index)
                .FirstOrDefault();
            if (selected.Character is null || selected.Character.Profile.Sex != "Female")
                throw new InvalidOperationException(
                    "Fallout 2 integrated combat proof lost the paired-gate owned premade.");
            host.Picker.Select(selected.Index);
            var editor = host.Picker.OpenCustom();
            editor.SetCharacterName("Mara");
            editor.SetTaggedSkills(selected.Character.Profile.TaggedSkills);
            editor.SetTraits(selected.Character.Profile.Traits);
            editor.Confirm();
            var selectedIdentity = host.SelectedCharacter ??
                throw new InvalidOperationException(
                    "Fallout 2 integrated combat proof lost its modified owned identity.");
            var handoff = host.OpeningHandoff ?? throw new InvalidOperationException(
                "Fallout 2 integrated combat proof did not start the owned opening tail.");
            handoff.RequestSkip();
            if (host.OpeningHandoffTask is not null)
                await host.OpeningHandoffTask;
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 confrontation proof did not enter Arroyo Caves.");
            var player = runtime.Player;
            if (selectedIdentity.Mode != Fo2CharacterSelection.CreateMode ||
                selectedIdentity.Id != "custom" ||
                selectedIdentity.Profile.Sex != "Female" ||
                runtime.SelectedPlayerPresentation.LogicalPath !=
                    Fo2CharacterStartCatalog.FemaleLogicalPath)
                throw new InvalidOperationException(
                    "Fallout 2 paired picker identity did not reach exact HFPRIM source.");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var femaleIdleRelief = CaptureReliefEvidence(
                "Female",
                player.Presentation,
                "AA",
                Fo2CharacterStartCatalog.FemaleLogicalPath,
                false);
            var malePresentation = new Fo2ArroyoPlayerPresentation(
                host.MalePlayerPresentation,
                host.Scene?.SourcePixelsPerMeter ?? throw new InvalidOperationException(
                    "Fallout 2 male relief proof has no source pixel scale."),
                runtime.Profile.SpawnCenterHeightMeters,
                player.Presentation.Direction)
            {
                Name = "HEADLESS_MALE_SOURCE_RELIEF_PROOF",
            };
            host.AddChild(malePresentation);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var maleIdleRelief = CaptureReliefEvidence(
                "Male",
                malePresentation,
                "AA",
                Fo2ArroyoPlayerPresentationCatalog.ExpectedLogicalPath,
                false);
            malePresentation.SetSpearEquipped(host.Temple.Confrontation.DefeatLoot, true);
            var maleEquippedRelief = CaptureReliefEvidence(
                "Male",
                malePresentation,
                "GA",
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedIdleLogicalPath,
                true);
            malePresentation.StartWalking(malePresentation.Direction);
            var maleEquippedWalkRelief = CaptureReliefEvidence(
                "Male",
                malePresentation,
                "GB",
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedWalkLogicalPath,
                true);
            malePresentation.StopWalking();
            host.RemoveChild(malePresentation);
            malePresentation.Free();
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
            var templeScene = host.TempleScene ?? throw new InvalidOperationException(
                "Fallout 2 confrontation proof has no Temple scene.");
            var target = host.Temple.Confrontation.Critter;
            var targetTile = target.Tile;
            var route = Fo1HexMath.Neighbors(targetTile)
                .Where(player.CanOccupy)
                .Select(tile => templeScene.Topology.Movement.BuildShortestPath(
                    player.CurrentTile,
                    tile))
                .OrderBy(path => path.Count)
                .ThenBy(path => path[^1])
                .FirstOrDefault();
            if (route is null || route.Count == 0 || route[0] != player.CurrentTile)
                throw new InvalidOperationException(
                    "Fallout 2 confrontation target has no source path from the live arrival.");
            if (!confrontation.TargetPlacementExact || !confrontation.TargetVisible ||
                confrontation.TargetSourceSha256.Length != 64 ||
                confrontation.TargetWorldPosition != Fo1HexMath.Center(target.Tile) ||
                target.RuntimeTeam != target.Stats.Team ||
                target.RuntimeAiPacket != target.Stats.AiPacket)
                throw new InvalidOperationException(
                    "Fallout 2 sole placed Temple encounter identity drifted.");
            var targetEvidence = new
            {
                target.Serial,
                target.Tile,
                target.Elevation,
                target.Rotation,
                target.Fid,
                target.Pid,
                target.Sid,
                target.ScriptIndex,
                target.DisplayName,
                target.RuntimeTeam,
                target.RuntimeAiPacket,
                target.CurrentHitPoints,
                target.CurrentActionPoints,
                target.PrototypeLogicalPath,
                target.PrototypeSha256,
                confrontation.TargetNodePath,
                confrontation.TargetSourceLogicalPath,
                confrontation.TargetSourceSha256,
                worldPosition = Vector(confrontation.TargetWorldPosition),
                rotationDegrees = Vector(confrontation.TargetRotationDegrees),
                placementExact = confrontation.TargetPlacementExact,
                visibleBeforeCombat = confrontation.TargetVisible,
                soleTopLevelCritterInOwnedMap = true,
            };

            // ACKlint only admits dialogue while the player is adjacent. Use the
            // already-proven source walk mask/AP movement to reach that authored
            // interaction range, then leave positioning combat before exercising
            // the pre-hostility dialogue graph.
            if (!confrontation.ToggleCombat())
                throw new InvalidOperationException(
                    "Fallout 2 confrontation could not enter bounded positioning mode.");
            var movementEndTurns = 0;
            foreach (var destination in route.Skip(1))
            {
                if (confrontation.State.PlayerActionPoints <
                    confrontation.MovementActionPointCost)
                {
                    if (!confrontation.EndTurn())
                        throw new InvalidOperationException(
                            "Fallout 2 source-route combat movement could not restore AP.");
                    movementEndTurns++;
                }
                if (!confrontation.TryMove(destination))
                    throw new InvalidOperationException(
                        $"Fallout 2 source-route combat movement rejected tile {destination}.");
            }
            if (Fo1HexMath.Distance(player.CurrentTile, targetTile) != 1 ||
                !player.Position.IsEqualApprox(
                    Fo1HexMath.Center(player.CurrentTile) +
                    Vector3.Up * runtime.Profile.SpawnCenterHeightMeters))
                throw new InvalidOperationException(
                    "Fallout 2 AP movement did not reach the exact source encounter adjacency.");
            var routeSha256 = Fo2TempleMovementConsumer.PathSha256(route);
            if (!confrontation.ToggleCombat() || confrontation.State.CombatActive)
                throw new InvalidOperationException(
                    "Fallout 2 confrontation could not leave positioning mode before ACKlint dialogue.");
            if (!confrontation.Talk() || !confrontation.DialogueVisible ||
                confrontation.DialogueNodeId != "Node001" ||
                !confrontation.DialogueReplyText.Contains(
                    host.SelectedCharacter!.Profile.Name,
                    StringComparison.Ordinal) ||
                !confrontation.DialogueOptions.Select(row => row.MessageId)
                    .SequenceEqual([106, 107, 108]))
                throw new InvalidOperationException(
                    "Fallout 2 ACKlint initial high-INT dialogue state did not match the owned graph.");
            var dialogueSteps = new List<object>
            {
                DialogueStep(confrontation),
            };
            if (!confrontation.SelectDialogueOption(106) ||
                confrontation.DialogueNodeId != "Node003" ||
                !confrontation.DialogueOptions.Select(row => row.MessageId)
                    .SequenceEqual([114, 115, 116]))
                throw new InvalidOperationException(
                    "Fallout 2 ACKlint negotiation branch did not enter Node003.");
            dialogueSteps.Add(DialogueStep(confrontation));
            if (!confrontation.SelectDialogueOption(116) ||
                confrontation.DialogueNodeId != "Node005" ||
                !confrontation.DialogueOptions.Select(row => row.MessageId)
                    .SequenceEqual([120]))
                throw new InvalidOperationException(
                    "Fallout 2 ACKlint threat branch did not enter Node005.");
            dialogueSteps.Add(DialogueStep(confrontation));
            if (!confrontation.SelectDialogueOption(120) ||
                confrontation.DialogueVisible ||
                !confrontation.DialogueVisitedNodes.SequenceEqual(
                    ["Node001", "Node003", "Node005"]))
                throw new InvalidOperationException(
                    "Fallout 2 ACKlint dialogue did not converge through the owned terminal branch.");
            if (confrontation.Loot() || !confrontation.State.CombatActive ||
                !confrontation.State.ScriptState.Flag("attack-player-requested"))
                throw new InvalidOperationException(
                    "Fallout 2 ACKlint pickup procedure did not enter hostile combat.");
            var attempts = 0;
            var attackEndTurns = 0;
            while (confrontation.State.TargetHitPoints > 0 &&
                attempts++ < MaximumAttackAttempts)
            {
                if (confrontation.Attack())
                    continue;
                if (!confrontation.EndTurn())
                    throw new InvalidOperationException(
                        "Fallout 2 confrontation could neither attack nor restore player AP.");
                attackEndTurns++;
            }
            var defeatedVisibleBeforeLoot = confrontation.TargetVisible;
            if (confrontation.State.TargetHitPoints != 0 ||
                !defeatedVisibleBeforeLoot || !confrontation.Loot() ||
                confrontation.TargetVisible)
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
                    host.CharacterStart.Inventory.LogicalPath ||
                confrontation.InventorySourceSha256 !=
                    host.CharacterStart.Inventory.SourceSha256)
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
                !confrontation.InventoryItemText.Contains("[EQUIPPED]", StringComparison.Ordinal) ||
                confrontation.PlayerSourceAnimationCode != "GA" ||
                !confrontation.PlayerUsesOwnedFrmRelief ||
                confrontation.PlayerSourceLogicalPath !=
                    Fo2CharacterStartCatalog.FemaleEquippedIdleLogicalPath ||
                !confrontation.PlayerEquippedCompositeVisible ||
                confrontation.PlayerEquippedWeaponGeometryVisible ||
                confrontation.PlayerMoldedFaceTriangles <= 0 ||
                confrontation.PlayerMoldedSideTriangles <= 0 ||
                confrontation.PlayerReliefIslands <= 0)
                throw new InvalidOperationException(
                    "Fallout 2 Spear equip did not bind the exact molded HFPRIM GA composite.");
            var femaleEquippedRelief = CaptureReliefEvidence(
                "Female",
                player.Presentation,
                "GA",
                Fo2CharacterStartCatalog.FemaleEquippedIdleLogicalPath,
                true);
            player.Presentation.StartWalking(player.Presentation.Direction);
            var femaleEquippedWalkRelief = CaptureReliefEvidence(
                "Female",
                player.Presentation,
                "GB",
                Fo2CharacterStartCatalog.FemaleEquippedWalkLogicalPath,
                true);
            player.Presentation.StopWalking();

            await PressAction(host, confrontation.InventoryEquipAction);
            if (confrontation.State != stateBeforeInventory ||
                confrontation.PlayerSourceAnimationCode != "AA" ||
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
            if (integratedOnly)
            {
                var integratedSave = host.PersistCurrentState();
                var integratedPassed = integratedSave.MapIndex ==
                        Fo2TemplePresentationCatalog.MapIndex &&
                    integratedSave.Character == selectedIdentity &&
                    selectedIdentity.GcdSha256 == selected.Character.GcdSha256 &&
                    integratedSave.TempleConfrontation == confrontation.State &&
                    integratedSave.TempleExitTransition is null &&
                    confrontation.State.TargetHitPoints == 0 &&
                    confrontation.State.SpearLooted &&
                    confrontation.State.SpearEquipped &&
                    !confrontation.TargetVisible &&
                    confrontation.InventoryCharacterText.Contains(
                        selectedIdentity.Profile.Name,
                        StringComparison.OrdinalIgnoreCase) &&
                    handoff.Completed && handoff.SkipRequested &&
                    handoff.SkipTerminalStateApplied &&
                    handoff.TerminalBlackPresented && handoff.ControlReleased;
                WriteReport(
                    System.IO.Path.Combine(
                        output,
                        "fo2-picker-temple-combat-integrated-ledger.json"),
                    new
                    {
                        schema = "opennv-fo2-picker-temple-combat-integrated-ledger/v1",
                        status = integratedPassed
                            ? "pass-owned-picker-tail-temple-ap-defeat-loot-inventory-save"
                            : "fail-owned-picker-tail-temple-ap-defeat-loot-inventory-save",
                        selection = new
                        {
                            selectedIdentity.Mode,
                            selectedIdentity.Id,
                            selectedIdentity.Profile.Name,
                            selectedIdentity.Profile.Sex,
                            selectedIdentity.GcdSha256,
                            selectedIdentity.Appearance.AppearanceRecipeSha256,
                        },
                        openingTail = new
                        {
                            handoff.Completed,
                            handoff.SkipRequested,
                            handoff.SkipTerminalStateApplied,
                            handoff.TerminalBlackPresented,
                            handoff.ControlReleased,
                        },
                        combat = new
                        {
                            target.Serial,
                            target.Pid,
                            target.Sid,
                            targetTile,
                            routeSha256,
                            routeSteps = route.Count - 1,
                            movementActionPointCost = confrontation.MovementActionPointCost,
                            movementEndTurns,
                            attackAttempts = attempts,
                            attackEndTurns,
                            confrontation.PlayerMeleeDamage,
                            finalState = confrontation.State,
                        },
                        lootAndInventory = new
                        {
                            host.Temple.Confrontation.DefeatLoot.Serial,
                            host.Temple.Confrontation.DefeatLoot.Pid,
                            host.Temple.Confrontation.DefeatLoot.DisplayName,
                            host.Temple.Confrontation.DefeatLoot.Quantity,
                            sourceLogicalPath = confrontation.InventorySourceLogicalPath,
                            sourceSha256 = confrontation.InventorySourceSha256,
                            selectedCharacterShown = true,
                            spearSelected = selectedSpear,
                            spearEquipped = confrontation.State.SpearEquipped,
                            sourceAnimationCode = confrontation.PlayerSourceAnimationCode,
                            sourceCompositeIncludesSpear =
                                confrontation.PlayerEquippedCompositeVisible,
                        },
                        continuity = new
                        {
                            sameSelectionInSave = integratedSave.Character == selectedIdentity,
                            sameGcdIdentity = selectedIdentity.GcdSha256 ==
                                selected.Character.GcdSha256,
                            sameCharacterInInventory = true,
                            sameEquipmentInSave =
                                integratedSave.TempleConfrontation?.SpearEquipped == true,
                        },
                        save = new
                        {
                            integratedSave.Path,
                            integratedSave.Sha256,
                            schema = Fo2CharacterStartSaveState.Schema,
                            integratedSave.MapIndex,
                            integratedSave.CurrentTile,
                        },
                        boundary = new
                        {
                            stoppedAt = "owned-temple-post-loot-equipped-save",
                            directGuardianDeathToVillageJoinAttempted = false,
                            peacefulCameronToVillageRouteRemains = "retained-r26",
                        },
                        passed = integratedPassed,
                    });
                GD.Print(integratedPassed
                    ? $"OPENNV_FO2_INTEGRATED_COMBAT_WRITE_PASS save={integratedSave.Path}"
                    : $"OPENNV_FO2_INTEGRATED_COMBAT_WRITE_FAIL output={output}");
                host.GetTree().Quit(integratedPassed ? 0 : 1);
                return;
            }
            var postGuardianExit = host.TempleTransition.Exits
                .Where(row => row.TargetMapIndex == 4 && player.CanOccupy(row.Tile))
                .Select(row => new
                {
                    Exit = row,
                    Path = templeScene.Topology.Movement.BuildShortestPath(
                        player.CurrentTile,
                        row.Tile),
                })
                .Where(row => row.Path.Count > 0)
                .OrderBy(row => row.Path.Count)
                .ThenBy(row => row.Exit.Serial)
                .FirstOrDefault() ?? throw new InvalidOperationException(
                    "Fallout 2 post-guardian state has no source-walkable Arroyo Village exit.");
            foreach (var destination in postGuardianExit.Path.Skip(1))
            {
                if (!confrontation.TryPostGuardianStep(destination))
                    throw new InvalidOperationException(
                        $"Fallout 2 post-guardian source step rejected tile {destination}.");
            }
            if (player.CurrentTile != postGuardianExit.Exit.Tile ||
                !confrontation.TryApplyTempleExit())
                throw new InvalidOperationException(
                    "Fallout 2 post-guardian exit-grid interaction was not applied.");
            var appliedTempleExit = host.TempleExitRuntime?.Applied ??
                throw new InvalidOperationException(
                    "Fallout 2 post-guardian transition has no applied source state.");
            var saved = host.PersistCurrentState();
            var passed = saved.MapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                saved.Character == selectedIdentity &&
                selectedIdentity.GcdSha256 == selected.Character.GcdSha256 &&
                saved.TempleConfrontation == confrontation.State &&
                saved.TempleExitTransition == appliedTempleExit &&
                confrontation.State.SpearLooted && confrontation.State.SpearEquipped &&
                !confrontation.TargetVisible && handoff.Completed &&
                handoff.SkipRequested && handoff.SkipTerminalStateApplied &&
                handoff.TerminalBlackPresented && handoff.ControlReleased;
            WriteReport(
                System.IO.Path.Combine(
                    output,
                    "fo2-picker-temple-combat-integrated-ledger.json"),
                new
                {
                    schema = "opennv-fo2-picker-temple-combat-integrated-ledger/v1",
                    status = passed
                        ? "pass-owned-picker-tail-temple-ap-defeat-loot-inventory-save"
                        : "fail-owned-picker-tail-temple-ap-defeat-loot-inventory-save",
                    selection = new
                    {
                        selectedIdentity.Mode,
                        selectedIdentity.Id,
                        selectedIdentity.Profile.Name,
                        selectedIdentity.Profile.Sex,
                        selectedIdentity.GcdSha256,
                        selectedIdentity.Appearance.AppearanceRecipeSha256,
                    },
                    openingTail = new
                    {
                        handoff.Completed,
                        handoff.SkipRequested,
                        handoff.SkipTerminalStateApplied,
                        handoff.TerminalBlackPresented,
                        handoff.ControlReleased,
                    },
                    combat = new
                    {
                        target.Serial,
                        target.Pid,
                        target.Sid,
                        targetTile,
                        routeSha256,
                        routeSteps = route.Count - 1,
                        movementActionPointCost = confrontation.MovementActionPointCost,
                        movementEndTurns,
                        attackAttempts = attempts,
                        attackEndTurns,
                        confrontation.PlayerMeleeDamage,
                        finalState = confrontation.State,
                    },
                    lootAndInventory = new
                    {
                        host.Temple.Confrontation.DefeatLoot.Serial,
                        host.Temple.Confrontation.DefeatLoot.Pid,
                        host.Temple.Confrontation.DefeatLoot.DisplayName,
                        host.Temple.Confrontation.DefeatLoot.Quantity,
                        sourceLogicalPath = confrontation.InventorySourceLogicalPath,
                        sourceSha256 = confrontation.InventorySourceSha256,
                        selectedCharacterShown = confrontation.InventoryCharacterText.Contains(
                            selectedIdentity.Profile.Name,
                            StringComparison.OrdinalIgnoreCase),
                        spearSelected = selectedSpear,
                        spearEquipped = confrontation.State.SpearEquipped,
                        sourceAnimationCode = confrontation.PlayerSourceAnimationCode,
                        sourceCompositeIncludesSpear =
                            confrontation.PlayerEquippedCompositeVisible,
                    },
                    continuity = new
                    {
                        sameSelectionInSave = saved.Character == selectedIdentity,
                        sameGcdIdentity = selectedIdentity.GcdSha256 ==
                            selected.Character.GcdSha256,
                        sameCharacterInInventory = confrontation.InventoryCharacterText.Contains(
                            selectedIdentity.Profile.Name,
                            StringComparison.OrdinalIgnoreCase),
                        sameEquipmentInSave = saved.TempleConfrontation?.SpearEquipped == true,
                    },
                    save = new
                    {
                        saved.Path,
                        saved.Sha256,
                        schema = Fo2CharacterStartSaveState.Schema,
                        saved.MapIndex,
                        saved.CurrentTile,
                    },
                    passed,
                });
            WriteReport(
                System.IO.Path.Combine(output, "fo2-temple-confrontation-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-temple-confrontation-write-proof/v1",
                    status = passed
                        ? "pass-bounded-defeat-loot-inventory-equip-save"
                        : "fail-bounded-defeat-loot-inventory-equip-save",
                    source = host.Temple.Confrontation,
                    encounter = new
                    {
                        targetEvidence,
                        guardianDialogue = new
                        {
                            host.Temple.Confrontation.GuardianScript.Schema,
                            host.Temple.Confrontation.GuardianScript.ProgramLogicalPath,
                            host.Temple.Confrontation.GuardianScript.ProgramSha256,
                            host.Temple.Confrontation.GuardianScript.MessageLogicalPath,
                            host.Temple.Confrontation.GuardianScript.MessageSha256,
                            host.Temple.Confrontation.GuardianScript.ContractSha256,
                            playerIntelligence = Fo2TempleConfrontationRuntime
                                .EffectiveIntelligence(host.SelectedCharacter!),
                            steps = dialogueSteps,
                            visitedNodes = confrontation.DialogueVisitedNodes,
                            dialogueBeforeFirstDamage = true,
                            dialogueStatePersistsInSave = false,
                            generalIntInterpreterExecuted = false,
                        },
                        earliestReachableAuthoredEncounter = true,
                        route,
                        routeSha256,
                        routeStartTile = route[0],
                        routeEndTile = route[^1],
                        routeSteps = route.Count - 1,
                        movementResolution = confrontation.MovementResolution,
                        movementActionPointCost = confrontation.MovementActionPointCost,
                        movementEndTurns,
                        attackAttempts = attempts,
                        attackEndTurns,
                        playerMeleeDamage = confrontation.PlayerMeleeDamage,
                        defeatedVisibleBeforeLoot,
                        hiddenAfterLoot = !confrontation.TargetVisible,
                        targetAiExecuted = false,
                        generalIntScriptsExecuted = false,
                    },
                    postGuardianTransition = new
                    {
                        path = postGuardianExit.Path,
                        pathSha256 = Fo2TempleMovementConsumer.PathSha256(
                            postGuardianExit.Path),
                        pathSteps = postGuardianExit.Path.Count - 1,
                        sourceInteraction = "step-onto-owned-nonblocking-exit-grid",
                        applied = appliedTempleExit,
                        destinationPresentationLoaded = false,
                        headerMapProgramExecuted = false,
                        doorInteractionRequired = false,
                    },
                    appearance = new
                    {
                        contract = saved.Character.Appearance,
                        distinctOwnedPanelReliefs = appearanceIdentities,
                        originalPickerPreserved = true,
                        selectedIdentityMode = selectedIdentity.Mode,
                        selectedIdentityId = selectedIdentity.Id,
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
                        sourceAnimationCode = confrontation.PlayerSourceAnimationCode,
                        equipmentSocketResolved = confrontation.PlayerEquipmentSocketResolved,
                        equipmentSocketName = confrontation.PlayerEquipmentSocketName,
                        weaponGeometryVisible = confrontation.PlayerEquippedWeaponGeometryVisible,
                        sourceCompositeIncludesSpear =
                            confrontation.PlayerEquippedCompositeVisible,
                        separableWeaponGeometry = false,
                        weaponGeometryDisposition = runtime.SelectedPlayerPresentation
                            .EquippedWeapon.GeometryDisposition,
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
                        selectedSex = host.SelectedCharacter?.Profile.Sex,
                        sourceLogicalPath = runtime.SelectedPlayerPresentation.LogicalPath,
                        maleAndFemaleMoldedReliefsExercised = true,
                        maleIdleRelief,
                        maleEquippedRelief,
                        maleEquippedWalkRelief,
                        femaleIdleRelief,
                        femaleEquippedRelief,
                        femaleEquippedWalkRelief,
                        gameplayUsesOwnedDonor = player.Presentation.UsesOwnedDonor,
                        gameplayUsesOwnedFrmRelief = player.Presentation.UsesOwnedFrmRelief,
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
                    proofSetupRepositionedToSourceWalkableAdjacentHex = false,
                    sourcePathApMovementExecuted = true,
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

    internal static Task RunRestore(Fo2CharacterStartHost host, string proofRoot) =>
        RunRestore(host, proofRoot, integratedOnly: false);

    internal static Task RunIntegratedRestore(Fo2CharacterStartHost host, string proofRoot) =>
        RunRestore(host, proofRoot, integratedOnly: true);

    private static async Task RunRestore(
        Fo2CharacterStartHost host,
        string proofRoot,
        bool integratedOnly)
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
            var restoredSelection = host.SelectedCharacter ??
                throw new InvalidOperationException(
                    "Fallout 2 integrated combat cold restore lost its selected identity.");
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
                restoredSelection == saved.Character &&
                restoredSelection.Mode == Fo2CharacterSelection.CreateMode &&
                restoredSelection.Id == "custom" &&
                restoredSelection.Profile.Sex == "Female" &&
                host.Runtime?.SelectedPlayerPresentation.LogicalPath ==
                    Fo2CharacterStartCatalog.FemaleLogicalPath &&
                host.LastTransition == host.Arroyo.LiveExit &&
                saved.MapIndex == Fo2TemplePresentationCatalog.MapIndex &&
                saved.TempleConfrontation == confrontation.State &&
                confrontation.State.TargetHitPoints == 0 &&
                confrontation.State.SpearLooted && confrontation.State.SpearEquipped &&
                confrontation.PlayerSourceAnimationCode == "GA" &&
                confrontation.PlayerUsesOwnedFrmRelief &&
                confrontation.PlayerSourceLogicalPath ==
                    Fo2CharacterStartCatalog.FemaleEquippedIdleLogicalPath &&
                confrontation.PlayerEquippedCompositeVisible &&
                confrontation.PlayerMoldedFaceTriangles > 0 &&
                confrontation.PlayerMoldedSideTriangles > 0 &&
                confrontation.PlayerReliefIslands > 0 &&
                !confrontation.PlayerEquippedWeaponGeometryVisible &&
                confrontation.TargetPlacementExact &&
                confrontation.TargetSourceSha256.Length == 64 &&
                (integratedOnly
                    ? saved.TempleExitTransition is null &&
                        host.TempleExitRuntime?.Applied is null
                    : saved.TempleExitTransition is not null &&
                        host.TempleExitRuntime?.Applied == saved.TempleExitTransition) &&
                host.Temple.Confrontation.GuardianScript.ProgramSha256.Length == 64 &&
                host.Temple.Confrontation.GuardianScript.MessageSha256.Length == 64 &&
                !confrontation.DialogueVisible &&
                Fo1HexMath.Distance(
                    host.Runtime!.Player.CurrentTile,
                    host.Temple.Confrontation.Critter.Tile) == 1 &&
                restoredInventory && !confrontation.InventoryVisible &&
                confrontation.State == restoredState &&
                host.CurrentSave?.Sha256 == restoredSaveSha256 &&
                !confrontation.TargetVisible;
            saved.Character.Appearance.Validate(saved.Character);
            WriteReport(
                System.IO.Path.Combine(
                    output,
                    "fo2-picker-temple-combat-integrated-restore.json"),
                new
                {
                    schema = "opennv-fo2-picker-temple-combat-integrated-restore/v1",
                    status = passed
                        ? "pass-cold-restore-same-picker-identity-defeat-loot-equipment"
                        : "fail-cold-restore-same-picker-identity-defeat-loot-equipment",
                    selection = new
                    {
                        restoredSelection.Mode,
                        restoredSelection.Id,
                        restoredSelection.Profile.Name,
                        restoredSelection.Profile.Sex,
                        restoredSelection.GcdSha256,
                        sameAsSaved = restoredSelection == saved.Character,
                    },
                    combat = new
                    {
                        confrontation.State.TargetHitPoints,
                        confrontation.State.PlayerActionPoints,
                        confrontation.State.CombatActive,
                        confrontation.State.SpearLooted,
                        confrontation.State.SpearEquipped,
                        targetVisible = confrontation.TargetVisible,
                    },
                    inventory = new
                    {
                        restoredInventory,
                        confrontation.InventorySourceLogicalPath,
                        confrontation.InventorySourceSha256,
                        selectedCharacterShown = confrontation.InventoryCharacterText.Contains(
                            restoredSelection.Profile.Name,
                            StringComparison.OrdinalIgnoreCase),
                        confrontation.PlayerSourceAnimationCode,
                        confrontation.PlayerSourceLogicalPath,
                    },
                    save = new
                    {
                        saved.Path,
                        saved.Sha256,
                        saved.MapIndex,
                        saved.CurrentTile,
                        stateUnchanged = confrontation.State == restoredState,
                        saveSha256Unchanged = host.CurrentSave?.Sha256 == restoredSaveSha256,
                    },
                    passed,
                });
            if (integratedOnly)
            {
                GD.Print(passed
                    ? $"OPENNV_FO2_INTEGRATED_COMBAT_RESTORE_PASS save={saved.Path}"
                    : $"OPENNV_FO2_INTEGRATED_COMBAT_RESTORE_FAIL output={output}");
                host.GetTree().Quit(passed ? 0 : 1);
                return;
            }
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
                    selected = new
                    {
                        sex = host.SelectedCharacter?.Profile.Sex,
                        sourceLogicalPath = host.Runtime?.SelectedPlayerPresentation.LogicalPath,
                        sourceIdentityRestored = true,
                    },
                    appearance = saved.Character.Appearance,
                    targetVisible = confrontation.TargetVisible,
                    encounter = new
                    {
                        serial = host.Temple.Confrontation.Critter.Serial,
                        tile = host.Temple.Confrontation.Critter.Tile,
                        confrontation.TargetNodePath,
                        confrontation.TargetSourceLogicalPath,
                        confrontation.TargetSourceSha256,
                        placementExact = confrontation.TargetPlacementExact,
                        adjacentPlayerTile = host.Runtime!.Player.CurrentTile,
                        adjacentAfterColdRestore = Fo1HexMath.Distance(
                            host.Runtime.Player.CurrentTile,
                            host.Temple.Confrontation.Critter.Tile) == 1,
                        killedAndLootedStateRestored =
                            confrontation.State.TargetHitPoints == 0 &&
                            confrontation.State.SpearLooted,
                        guardianDialogue = new
                        {
                            host.Temple.Confrontation.GuardianScript.Schema,
                            host.Temple.Confrontation.GuardianScript.ProgramLogicalPath,
                            host.Temple.Confrontation.GuardianScript.ProgramSha256,
                            host.Temple.Confrontation.GuardianScript.MessageLogicalPath,
                            host.Temple.Confrontation.GuardianScript.MessageSha256,
                            host.Temple.Confrontation.GuardianScript.ContractSha256,
                            dialogueVisibleAfterColdRestore = confrontation.DialogueVisible,
                            dialogueSessionPersisted = false,
                            sourceContractReloaded = true,
                        },
                        postGuardianTransition = new
                        {
                            saved.TempleExitTransition,
                            restoredApplied = host.TempleExitRuntime?.Applied,
                            exactSourceStateRestored = host.TempleExitRuntime?.Applied ==
                                saved.TempleExitTransition,
                            destinationPresentationLoaded = false,
                        },
                    },
                    inventory = new
                    {
                        restoredEquippedSelection = restoredInventory,
                        sourceAnimationCode = confrontation.PlayerSourceAnimationCode,
                        equipmentSocketResolved = confrontation.PlayerEquipmentSocketResolved,
                        equipmentSocketName = confrontation.PlayerEquipmentSocketName,
                        weaponGeometryVisible = confrontation.PlayerEquippedWeaponGeometryVisible,
                        sourceCompositeIncludesSpear =
                            confrontation.PlayerEquippedCompositeVisible,
                        separableWeaponGeometry = false,
                        moldedRelief = CaptureReliefEvidence(
                            "Female",
                            host.Runtime!.Player.Presentation,
                            "GA",
                            Fo2CharacterStartCatalog.FemaleEquippedIdleLogicalPath,
                            true),
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

    private static object DialogueStep(Fo2TempleConfrontationRuntime confrontation) => new
    {
        node = confrontation.DialogueNodeId,
        reply = confrontation.DialogueReplyText,
        options = confrontation.DialogueOptions.Select(row => new
        {
            row.MessageId,
            row.Text,
            row.Target,
            row.MinimumIntelligence,
            row.MaximumIntelligence,
            row.Reaction,
        }).ToArray(),
    };

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
        left.TargetActionPoints == right.TargetActionPoints &&
        left.TargetTurnCount == right.TargetTurnCount &&
        left.LastTargetTurnAction == right.LastTargetTurnAction &&
        left.LastTargetAttack == right.LastTargetAttack &&
        SameTargetPath(left.LastTargetPath, right.LastTargetPath) &&
        left.CombatActive == right.CombatActive &&
        left.SpearLooted == right.SpearLooted;

    private static bool SameTargetPath(
        ClassicTargetPathState? left,
        ClassicTargetPathState? right) =>
        left is null && right is null ||
        left is not null && right is not null &&
        left.CurrentTile == right.CurrentTile &&
        left.TargetTile == right.TargetTile &&
        left.ActionPoints == right.ActionPoints &&
        left.Rotation == right.Rotation &&
        left.CompletedSteps == right.CompletedSteps &&
        left.Path.SequenceEqual(right.Path) &&
        left.Contract == right.Contract &&
        left.Boundary == right.Boundary;

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private static MoldedReliefEvidence CaptureReliefEvidence(
        string sex,
        Fo2ArroyoPlayerPresentation presentation,
        string expectedAnimationCode,
        string expectedLogicalPath,
        bool expectedSpear)
    {
        var frame = presentation.CurrentFrame;
        if (!presentation.VisibleInWorld || !presentation.UsesOwnedFrmRelief ||
            presentation.UsesOwnedDonor ||
            presentation.GeometryMode != Fo2ArroyoPlayerPresentation.OwnedFrmReliefMode ||
            presentation.AnimationCode != expectedAnimationCode ||
            frame.LogicalPath != expectedLogicalPath ||
            presentation.SpearEquipped != expectedSpear ||
            presentation.EquippedCompositeVisible != expectedSpear ||
            presentation.EquippedWeaponGeometryVisible ||
            presentation.MeshInstances != 2 ||
            presentation.MoldedFaceTriangles <= 0 ||
            presentation.MoldedSideTriangles <= 0 ||
            presentation.ReliefIslands <= 0)
            throw new InvalidOperationException(
                $"Fallout 2 {sex} {expectedAnimationCode} molded source relief failed.");
        return new MoldedReliefEvidence(
            sex,
            presentation.AnimationCode,
            frame.Id,
            frame.LogicalPath,
            frame.SourceSha256,
            frame.PngSha256,
            frame.Relief.NormalPngSha256,
            frame.Relief.SolidMaskPngSha256,
            frame.Relief.DepthPngSha256,
            presentation.ReliefIslands,
            presentation.MeshInstances,
            presentation.MoldedFaceTriangles,
            presentation.MoldedSideTriangles,
            presentation.VisibleInWorld,
            presentation.UsesOwnedFrmRelief,
            expectedSpear,
            false);
    }

    private sealed record MoldedReliefEvidence(
        string Sex,
        string AnimationCode,
        string ArtifactId,
        string LogicalPath,
        string SourceSha256,
        string PngSha256,
        string NormalPngSha256,
        string SolidMaskPngSha256,
        string DepthPngSha256,
        int ReliefIslands,
        int MeshInstances,
        int MoldedFaceTriangles,
        int MoldedSideTriangles,
        bool VisibleInWorld,
        bool UsesOwnedFrmRelief,
        bool SourceCompositeIncludesSpear,
        bool SeparableWeaponGeometry);

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
