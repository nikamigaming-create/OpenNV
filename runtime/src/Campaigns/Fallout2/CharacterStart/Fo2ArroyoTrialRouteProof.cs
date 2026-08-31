using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2ArroyoTrialRouteProof
{
    private const int PairedGateNeutralFrames = 4;
    private const int PairedGateMovementFrames = 120;
    private const int PairedGateSettleFrames = 2;

    internal static async Task RunWrite(Fo2CharacterStartHost host, string proofRoot)
    {
        string? pressedAction = null;
        try
        {
            var output = PrepareOutput(proofRoot, false);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 trial write proof requires a fresh save path.");
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
            if (selected.Character is null)
                throw new InvalidOperationException(
                    "Fallout 2 owned premades contain no exact tagged-Speech trial route.");
            host.Picker.Select(selected.Index);
            var editor = host.Picker.OpenCustom();
            editor.SetCharacterName("Mara");
            editor.SetTaggedSkills(selected.Character.Profile.TaggedSkills);
            editor.SetTraits(selected.Character.Profile.Traits);
            editor.Confirm();
            var handoff = host.OpeningHandoff ?? throw new InvalidOperationException(
                "Fallout 2 paired gate did not start its owned opening-tail handoff.");
            handoff.RequestSkip();
            if (host.OpeningHandoffTask is not null)
                await host.OpeningHandoffTask;
            var runtime = host.TrialRuntime ?? throw new InvalidOperationException(
                "Fallout 2 trial runtime was not created.");
            runtime.TraverseApproach();
            var dialogue = new List<object>();
            foreach (var messageId in host.TrialRoute.Cameron.TaggedSpeechBranch.SelectedMessageIds)
                dialogue.Add(new
                {
                    messageId,
                    text = runtime.SelectTaggedSpeechOption(
                        messageId,
                        host.SelectedCharacter!),
                });
            runtime.TraverseReturn();
            host.EnterTempleAfterTrial();
            var temple = host.TempleScene ?? throw new InvalidOperationException(
                "Fallout 2 trial route did not enter ARTEMPLE.");
            var klint = Descendants<Sprite3D>(temple.Root).Single(row =>
                row.HasMeta("map_serial") && row.GetMeta("map_serial").AsInt32() ==
                    host.TrialRoute.KlintGate.ActorSerial);
            var gate = Descendants<Sprite3D>(temple.Root).Single(row =>
                row.HasMeta("map_serial") && row.GetMeta("map_serial").AsInt32() ==
                    host.TrialRoute.KlintGate.GateSerial);
            runtime.TraverseVillageRoute();
            var applied = host.EnterVillageAfterTrial();
            var player = host.Runtime?.Player ?? throw new InvalidOperationException(
                "Fallout 2 trial route has no player runtime.");
            var villageHumanoid = player.VillageHumanoid ??
                throw new InvalidOperationException(
                    "Fallout 2 ARVILLAG arrival has no selected full-body humanoid.");
            if (runtime.State.Stage != Fo2ArroyoTrialProgressState.VillageArrivalStage ||
                player.CurrentMapIndex != host.TrialRoute.VillageArrival.MapIndex ||
                player.CurrentTile != host.TrialRoute.VillageArrival.ArrivalTile ||
                !villageHumanoid.UsesOwnedDonor || villageHumanoid.MeshInstances <= 0 ||
                villageHumanoid.AuthoredSurfaces <= 0 ||
                villageHumanoid.LitMaterials <= 0 ||
                !villageHumanoid.EquipmentSocketResolved)
                throw new InvalidOperationException(
                    "Fallout 2 ARVILLAG arrival state was not applied.");
            string? arrivalFrame = null;
            string? arrivalFrameSha256 = null;
            if (host.VillageArrivalCaptureRoot is not null)
            {
                (arrivalFrame, arrivalFrameSha256) = await CaptureVillageArrival(
                    host,
                    host.VillageArrivalCaptureRoot);
            }
            for (var frame = 0; frame < PairedGateNeutralFrames; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await host.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
            var beforeControl = CapturePairedGateFrame(
                host,
                output,
                "fo2-arvillag-before-control-input.png");
            var movement = SelectExactVillageFirstAction(host.Runtime!, player,
                host.TrialRoute.VillageArrival);
            var actionStartTile = player.CurrentTile;
            Input.ActionPress(movement.Action);
            pressedAction = movement.Action;
            var movementFrames = 0;
            for (; movementFrames < PairedGateMovementFrames &&
                   player.CurrentTile == actionStartTile;
                 movementFrames++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(movement.Action);
            pressedAction = null;
            for (var frame = 0; frame < PairedGateSettleFrames; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await host.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
            var afterAction = CapturePairedGateFrame(
                host,
                output,
                "fo2-arvillag-after-first-action.png");
            var saved = host.PersistCurrentState();
            var passed = runtime.State.Stage ==
                    Fo2ArroyoTrialProgressState.VillageFirstActionStage &&
                runtime.State.GlobalVariable10 == 2 && runtime.State.KlintAlive &&
                !klint.IsQueuedForDeletion() && klint.Visible &&
                gate.GetMeta("map_tile").AsInt32() ==
                    host.TrialRoute.KlintGate.DestinationTile &&
                saved.TrialProgress == runtime.State &&
                saved.TempleExitTransition == applied && saved.MapIndex == 4 &&
                saved.CurrentTile == host.TrialRoute.VillageArrival.FirstActionToTile &&
                player.GetMeta("destination_presentation_loaded").AsBool() &&
                player.GetMeta("first_legal_destination_input_driven").AsBool() &&
                actionStartTile == host.TrialRoute.VillageArrival.FirstActionFromTile &&
                player.CurrentTile == host.TrialRoute.VillageArrival.FirstActionToTile &&
                movementFrames > 0 && movementFrames < PairedGateMovementFrames &&
                villageHumanoid.UsesOwnedDonor && !player.Presentation.Visible &&
                player.ControlsEnabled && host.VillageScene is not null &&
                host.VillageScene.Root.Visible;
            var selectedIdentity = host.SelectedCharacter ??
                throw new InvalidOperationException(
                    "Fallout 2 paired gate lost its selected custom identity.");
            var outfitFormId = villageHumanoid.GetMeta(
                "donor_outfit_form_id").AsString();
            var expectedEquipmentState = saved.TempleConfrontation?.SpearEquipped == true
                ? "spear-equipped"
                : "unarmed";
            var identityAndOutfitPassed =
                selectedIdentity.Mode == Fo2CharacterSelection.CreateMode &&
                villageHumanoid.CharacterId == selectedIdentity.Id &&
                villageHumanoid.OwnedIdentitySha256 == selectedIdentity.GcdSha256 &&
                villageHumanoid.GetMeta("character_sex").AsString() ==
                    selectedIdentity.Profile.Sex &&
                outfitFormId.Length == 8 && outfitFormId.All(Uri.IsHexDigit) &&
                villageHumanoid.GetMeta("equipment_state").AsString() ==
                    expectedEquipmentState &&
                villageHumanoid.GetMeta("molded_floor_height_tile").AsInt32() ==
                    host.TrialRoute.VillageArrival.FirstActionToTile;
            passed = passed && identityAndOutfitPassed;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-arvillag-paired-handoff-report.json"),
                new
                {
                    schema = "opennv-fo2-arvillag-paired-handoff-proof/v1",
                    status = passed
                        ? "pass-owned-picker-tail-arvillag-live-input-save"
                        : "fail-owned-picker-tail-arvillag-live-input-save",
                    openingTail = new
                    {
                        handoff.Completed,
                        handoff.SkipRequested,
                        handoff.SkipTerminalStateApplied,
                        handoff.TerminalBlackPresented,
                        handoff.ControlReleased,
                    },
                    selection = new
                    {
                        selectedIdentity.Mode,
                        selectedIdentity.Id,
                        selectedIdentity.Profile.Name,
                        selectedIdentity.Profile.Sex,
                        selectedIdentity.GcdSha256,
                        selectedIdentity.Appearance.AppearanceRecipeSha256,
                        playerSourceFid = villageHumanoid.GetMeta("source_fid").AsString(),
                        playerSourceFrmSha256 = villageHumanoid.GetMeta(
                            "source_frm_sha256").AsString(),
                    },
                    fullBody = new
                    {
                        villageHumanoid.PresentationMode,
                        villageHumanoid.MeshInstances,
                        villageHumanoid.AuthoredSurfaces,
                        villageHumanoid.LitMaterials,
                        villageHumanoid.EquipmentSocketResolved,
                        villageHumanoid.EquipmentSocketName,
                        outfitFormId,
                        equipmentState = villageHumanoid.GetMeta(
                            "equipment_state").AsString(),
                        expectedEquipmentState,
                        groundedTile = villageHumanoid.GetMeta(
                            "molded_floor_height_tile").AsInt32(),
                        identityAndOutfitPassed,
                    },
                    controlAndAction = new
                    {
                        controlsReleased = player.ControlsEnabled,
                        action = movement.Action,
                        physicalKey = movement.PhysicalKey.ToString(),
                        fromTile = actionStartTile,
                        toTile = player.CurrentTile,
                        expectedToTile = host.TrialRoute.VillageArrival.FirstActionToTile,
                        movementFrames,
                        godotActionDrive = true,
                        foregroundInputInjected = false,
                        trialState = runtime.State.Stage,
                    },
                    frames = new { beforeControl, afterAction },
                    save = new
                    {
                        saved.Path,
                        saved.Sha256,
                        saved.MapIndex,
                        saved.CurrentTile,
                        characterMode = saved.Character.Mode,
                        characterId = saved.Character.Id,
                        characterIdentitySha256 = saved.Character.GcdSha256,
                    },
                    expectedColdRestore = new
                    {
                        saved.CurrentTile,
                        saved.Character.Mode,
                        saved.Character.Id,
                        outfitFormId,
                    },
                    passed,
                });
            WriteReport(
                System.IO.Path.Combine(output, "fo2-arroyo-trial-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-arroyo-trial-write-proof/v1",
                    status = passed
                        ? "pass-owned-cameron-gate-arvillag-arrival-first-action-save"
                        : "fail-owned-cameron-gate-arvillag-arrival-first-action-save",
                    route = new
                    {
                        host.TrialRoute.Path,
                        host.TrialRoute.Sha256,
                        approachSteps = host.TrialRoute.ApproachCameron.StepCount,
                        approachSha256 = host.TrialRoute.ApproachCameron.Sha256,
                        returnSteps = host.TrialRoute.ReturnToTemple.StepCount,
                        returnSha256 = host.TrialRoute.ReturnToTemple.Sha256,
                        villageSteps = host.TrialRoute.Village.Path.StepCount,
                        villageSha256 = host.TrialRoute.Village.Path.Sha256,
                    },
                    selected = new
                    {
                        host.SelectedCharacter!.Id,
                        host.SelectedCharacter.Profile.Name,
                        host.SelectedCharacter.Profile.TaggedSkills,
                        intelligence = Fo2TempleConfrontationRuntime.EffectiveIntelligence(
                            host.SelectedCharacter),
                    },
                    cameron = new
                    {
                        host.TrialRoute.Cameron.Serial,
                        host.TrialRoute.Cameron.ProgramLogicalPath,
                        host.TrialRoute.Cameron.ProgramSha256,
                        host.TrialRoute.Cameron.MessageLogicalPath,
                        host.TrialRoute.Cameron.MessageSha256,
                        dialogue,
                        runtime.State.CameronLocalVariable12,
                        runtime.State.CameronLocalVariable13,
                        runtime.State.CameronMapVariable20,
                        runtime.State.GlobalVariable10,
                        runtime.State.CameronTile,
                        runtime.State.CameronVisible,
                        runtime.State.CameronDoorOpened,
                        runtime.State.CameronDoorUnlocked,
                    },
                    acklint = new
                    {
                        host.TrialRoute.KlintGate.ActorSerial,
                        host.TrialRoute.KlintGate.ActorProgramLogicalPath,
                        host.TrialRoute.KlintGate.ActorProgramSha256,
                        klintAlive = runtime.State.KlintAlive,
                        gateTile = runtime.State.KlintGateTile,
                        mapEnterApplied = gate.GetMeta("acklint_map_enter_applied").AsBool(),
                    },
                    transition = applied,
                    destinationArrival = new
                    {
                        host.TrialRoute.VillageArrival.Mode,
                        host.TrialRoute.VillageArrival.MapLogicalPath,
                        host.TrialRoute.VillageArrival.MapSha256,
                        host.TrialRoute.VillageArrival.ArrivalTile,
                        host.TrialRoute.VillageArrival.ArrivalRotation,
                        host.TrialRoute.VillageArrival.WalkMaskSha256,
                        host.TrialRoute.VillageArrival.WalkableHexes,
                        host.TrialRoute.VillageArrival.LegalNeighborTiles,
                        host.TrialRoute.VillageArrival.FirstActionFromTile,
                        host.TrialRoute.VillageArrival.FirstActionToTile,
                        host.TrialRoute.VillageArrival.FirstActionRotation,
                        currentTile = player.CurrentTile,
                        presentationLoaded = player.GetMeta(
                            "destination_presentation_loaded").AsBool(),
                        cacheManifest = host.Village.ManifestPath,
                        cacheManifestSha256 = host.Village.ManifestSha256,
                        sourceManifestSha256 = host.Village.SourceManifestSha256,
                        reliefPlacements = host.VillageScene?.ReliefPlacements,
                        transparentPlacements = host.VillageScene?.TransparentPlacements,
                        sourceRoofPatches = host.VillageScene?.SourceRoofPatches,
                        roofCutaway = host.VillageScene?.RoofCutaway,
                        sourceMapLightRecords = host.VillageScene?.SourceMapLightRecords,
                        sourceMapLights = host.VillageScene?.SourceMapLights,
                        floorMaterialDepthMeshes =
                            host.VillageScene?.FloorMaterialDepthMeshes,
                        moldedFloorTriangles = host.VillageScene?.MoldedFloorTriangles,
                        moldedFloorBoundaryHeightMeters =
                            host.VillageScene?.MoldedFloorBoundaryHeightMeters,
                        presentationProfile = host.VillageScene?.PresentationProfilePath,
                        presentationProfileSha256 =
                            host.VillageScene?.PresentationProfileSha256,
                        playerPresentation = new
                        {
                            villageHumanoid.PresentationMode,
                            villageHumanoid.CharacterId,
                            villageHumanoid.MeshInstances,
                            villageHumanoid.AuthoredSurfaces,
                            villageHumanoid.LitMaterials,
                            villageHumanoid.EquipmentSocketResolved,
                            villageHumanoid.EquipmentSocketName,
                            donorManifestSha256 = villageHumanoid.GetMeta(
                                "donor_manifest_sha256").AsString(),
                            donorModelSha256 = villageHumanoid.GetMeta(
                                "donor_model_sha256").AsString(),
                            donorSidecarSha256 = villageHumanoid.GetMeta(
                                "donor_sidecar_sha256").AsString(),
                            donorOutfitFormId = villageHumanoid.GetMeta(
                                "donor_outfit_form_id").AsString(),
                            fo2IdentitySha256 = villageHumanoid.OwnedIdentitySha256,
                        },
                        arrivalFrame,
                        arrivalFrameSha256,
                    },
                    save = new
                    {
                        saved.Path,
                        saved.Sha256,
                        schema = Fo2CharacterStartSaveState.Schema,
                        saved.MapIndex,
                        saved.CurrentTile,
                        saved.WalkMaskSha256,
                        saved.TrialProgress,
                    },
                    limitations = new[]
                    {
                        "wall-edge and multihex collision parity is not implemented",
                        "Cameron combat/surrender and untagged Speech rolls remain fail-closed",
                        "ARVILLAG source roofs remain a cutaway because owned MAP/FRM data has no accepted 3D height contract",
                        "ARVILLAG molded relief is a first-party 3D interpretation and is not retail visual parity",
                    },
                    passed,
                });
            if (!passed)
                throw new InvalidOperationException(
                    "Fallout 2 owned Cameron-to-village write proof did not pass.");
            GD.Print(
                $"OPENNV_FO2_ARROYO_TRIAL_WRITE_PASS report={output} save={saved.Path} " +
                $"saveSha256={saved.Sha256}");
            host.GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_TRIAL_WRITE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        finally
        {
            if (pressedAction is not null)
                Input.ActionRelease(pressedAction);
        }
    }

    internal static Task RunRestore(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            var output = PrepareOutput(proofRoot, false);
            var runtime = host.TrialRuntime ?? throw new InvalidOperationException(
                "Fallout 2 restored trial runtime is absent.");
            var save = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 restored trial save is absent.");
            var gate = Descendants<Sprite3D>(host.TempleScene?.Root ??
                throw new InvalidOperationException("Fallout 2 restored Temple scene is absent."))
                .Single(row => row.HasMeta("map_serial") &&
                    row.GetMeta("map_serial").AsInt32() == host.TrialRoute.KlintGate.GateSerial);
            var klint = Descendants<Sprite3D>(host.TempleScene!.Root)
                .Single(row => row.HasMeta("map_serial") &&
                    row.GetMeta("map_serial").AsInt32() == host.TrialRoute.KlintGate.ActorSerial);
            var restoredPlayer = host.Runtime?.Player ??
                throw new InvalidOperationException(
                    "Fallout 2 paired restore has no player runtime.");
            var restoredSelection = host.SelectedCharacter ??
                throw new InvalidOperationException(
                    "Fallout 2 paired restore has no selected identity.");
            var restoredHumanoid = restoredPlayer.VillageHumanoid ??
                throw new InvalidOperationException(
                    "Fallout 2 paired restore has no village full-body actor.");
            var restoredOutfitFormId = restoredHumanoid.GetMeta(
                "donor_outfit_form_id").AsString();
            var restoredExpectedEquipmentState =
                save.TempleConfrontation?.SpearEquipped == true
                    ? "spear-equipped"
                    : "unarmed";
            var restoredIdentityAndOutfit =
                restoredSelection == save.Character &&
                restoredHumanoid.CharacterId == save.Character.Id &&
                restoredHumanoid.OwnedIdentitySha256 == save.Character.GcdSha256 &&
                restoredHumanoid.GetMeta("character_sex").AsString() ==
                    save.Character.Profile.Sex &&
                restoredOutfitFormId.Length == 8 &&
                restoredOutfitFormId.All(Uri.IsHexDigit) &&
                restoredHumanoid.GetMeta("equipment_state").AsString() ==
                    restoredExpectedEquipmentState &&
                restoredHumanoid.GetMeta("molded_floor_height_tile").AsInt32() ==
                    save.CurrentTile;
            var passed = host.RestoredFromSave &&
                runtime.State == save.TrialProgress &&
                runtime.State.Stage == Fo2ArroyoTrialProgressState.VillageFirstActionStage &&
                runtime.State.GlobalVariable10 == 2 && runtime.State.KlintAlive &&
                klint.Visible && gate.GetMeta("map_tile").AsInt32() ==
                    host.TrialRoute.KlintGate.DestinationTile &&
                host.Runtime?.Player.CurrentMapIndex ==
                    host.TrialRoute.VillageArrival.MapIndex &&
                host.Runtime.Player.CurrentTile ==
                    host.TrialRoute.VillageArrival.FirstActionToTile &&
                host.Runtime.Player.CurrentWalkMaskSha256 ==
                    host.TrialRoute.VillageArrival.WalkMaskSha256 &&
                host.TempleExitRuntime?.Applied == save.TempleExitTransition &&
                host.Runtime.Player.ControlsEnabled &&
                host.Runtime.Player.GetMeta("destination_presentation_loaded").AsBool() &&
                host.Runtime.Player.VillageHumanoid?.UsesOwnedDonor == true &&
                host.Runtime.Player.VillageHumanoid.MeshInstances > 0 &&
                host.Runtime.Player.VillageHumanoid.LitMaterials > 0 &&
                restoredIdentityAndOutfit &&
                !host.Runtime.Player.Presentation.Visible &&
                host.VillageScene is not null && host.VillageScene.Root.Visible;
            WriteReport(
                System.IO.Path.Combine(output, "fo2-arroyo-trial-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-arroyo-trial-restore-proof/v1",
                    status = passed
                        ? "pass-owned-cameron-gate-arvillag-first-action-cold-restore"
                        : "fail-owned-cameron-gate-arvillag-first-action-cold-restore",
                    routeSha256 = host.TrialRoute.Sha256,
                    save = new { save.Path, save.Sha256, save.TrialProgress },
                    restored = new
                    {
                        runtime.State,
                        gateTile = gate.GetMeta("map_tile").AsInt32(),
                        klintVisible = klint.Visible,
                        currentTile = host.Runtime?.Player.CurrentTile,
                        mapIndex = host.Runtime?.Player.CurrentMapIndex,
                        destinationPresentationLoaded = host.Runtime?.Player
                            .GetMeta("destination_presentation_loaded").AsBool(),
                        walkMaskSha256 = host.Runtime?.Player.CurrentWalkMaskSha256,
                        applied = host.TempleExitRuntime?.Applied,
                        controlsEnabled = host.Runtime?.Player.ControlsEnabled,
                        identity = new
                        {
                            restoredSelection.Mode,
                            restoredSelection.Id,
                            restoredSelection.Profile.Name,
                            restoredSelection.Profile.Sex,
                            restoredSelection.GcdSha256,
                            actorCharacterId = restoredHumanoid.CharacterId,
                            actorIdentitySha256 = restoredHumanoid.OwnedIdentitySha256,
                            outfitFormId = restoredOutfitFormId,
                            equipmentState = restoredHumanoid.GetMeta(
                                "equipment_state").AsString(),
                            expectedEquipmentState = restoredExpectedEquipmentState,
                            groundedTile = restoredHumanoid.GetMeta(
                                "molded_floor_height_tile").AsInt32(),
                            restoredIdentityAndOutfit,
                        },
                    },
                    passed,
                });
            if (!passed)
                throw new InvalidOperationException(
                    "Fallout 2 owned Cameron-to-village cold restore did not pass.");
            GD.Print(
                $"OPENNV_FO2_ARROYO_TRIAL_RESTORE_PASS report={output} " +
                $"saveSha256={save.Sha256}");
            host.GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_TRIAL_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        return Task.CompletedTask;
    }

    private static Fo2ArroyoInputBinding SelectExactVillageFirstAction(
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Fo2ArroyoCavesPlayerBody player,
        Fo2TrialVillageArrival arrival)
    {
        var candidates = new[]
        {
            (runtime.Profile.MoveBackward, Vector3.Back),
            (runtime.Profile.MoveForward, Vector3.Forward),
            (runtime.Profile.MoveRight, Vector3.Right),
            (runtime.Profile.MoveLeft, Vector3.Left),
        };
        foreach (var (binding, desired) in candidates)
        {
            var direction = Fo2ArroyoCavesPlayerBody.DirectionForMovement(
                player.CurrentTile,
                desired);
            if (direction == arrival.FirstActionRotation &&
                Fo1HexMath.TileInDirection(player.CurrentTile, direction) ==
                    arrival.FirstActionToTile)
                return binding;
        }
        throw new InvalidOperationException(
            "Fallout 2 ARVILLAG has no configured input for its exact first action.");
    }

    private static PairedGateFrameEvidence CapturePairedGateFrame(
        Fo2CharacterStartHost host,
        string output,
        string filename)
    {
        var path = System.IO.Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.GetWidth() <= 0 || image.GetHeight() <= 0 ||
            image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException(
                $"Fallout 2 ARVILLAG paired-gate frame could not be written: {path}");
        var bytes = File.ReadAllBytes(path);
        return new PairedGateFrameEvidence(
            path,
            bytes.LongLength,
            image.GetWidth(),
            image.GetHeight(),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static async Task<(string Path, string Sha256)> CaptureVillageArrival(
        Fo2CharacterStartHost host,
        string configuredRoot)
    {
        var scene = host.VillageScene;
        var runtime = host.Runtime;
        if (DisplayServer.GetName() == "headless" || scene is null || runtime is null ||
            runtime.Player.CurrentTile != host.TrialRoute.VillageArrival.ArrivalTile ||
            !scene.Root.Visible ||
            !runtime.Player.GetMeta("destination_presentation_loaded").AsBool() ||
            runtime.Player.VillageHumanoid?.UsesOwnedDonor != true ||
            runtime.Player.Presentation.Visible)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG arrival capture requires the live owned destination scene.");
        var root = System.IO.Path.GetFullPath(configuredRoot);
        if (File.Exists(root) || Directory.Exists(root))
            throw new InvalidOperationException(
                $"Refusing to overwrite Fallout 2 ARVILLAG arrival capture: {root}");
        Directory.CreateDirectory(root);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.GetWidth() <= 0 || image.GetHeight() <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG arrival viewport is empty.");
        var path = System.IO.Path.Combine(root, "fo2-arvillag-owned-arrival.png");
        var error = image.SavePng(path);
        if (error != Error.Ok || !File.Exists(path))
            throw new InvalidOperationException(
                $"Fallout 2 ARVILLAG arrival capture failed: {error}.");
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
        WriteReport(
            System.IO.Path.Combine(root, "fo2-arvillag-owned-arrival.json"),
            new
            {
                schema = "opennv-fo2-arvillag-owned-arrival-capture/v1",
                status = "captured-for-human-review-not-retail-parity",
                image = new
                {
                    path,
                    sha256,
                    width = image.GetWidth(),
                    height = image.GetHeight(),
                },
                source = new
                {
                    host.Village.ManifestPath,
                    host.Village.ManifestSha256,
                    host.Village.SourceManifestPath,
                    host.Village.SourceManifestSha256,
                    host.Village.MapSha256,
                    host.Village.WalkMaskSha256,
                    host.Village.ArrivalTile,
                    host.Village.ArrivalRotation,
                },
                presentation = new
                {
                    scene.ReliefPlacements,
                    scene.TransparentPlacements,
                    scene.ConstructedFloorPatches,
                    scene.SourceRoofPatches,
                    scene.RoofCutaway,
                    scene.ReliefTriangles,
                    scene.SourceMapLightRecords,
                    scene.SourceMapLights,
                    scene.FloorMaterialDepthMeshes,
                    scene.MoldedFloorTriangles,
                    scene.MoldedFloorBoundaryHeightMeters,
                    scene.MoldedFloorHeightScale,
                    scene.MoldedFloorNormalScale,
                    scene.MoldedFloorSourceDetailMix,
                    scene.MoldedFloorAlbedoScale,
                    scene.ObjectReliefDepthScale,
                    scene.ObjectReliefNormalScale,
                    scene.ObjectTwoSidedLightingMode,
                    scene.ObjectBacklightStrength,
                    arrivalFraming = new
                    {
                        scene.ArrivalFraming.Mode,
                        scene.ArrivalFraming.SourceObjectSerials,
                        scene.ArrivalFraming.SourceObjectTiles,
                        routeAndObjectBoundsPositionMeters = new
                        {
                            x = scene.ArrivalFraming.RouteAndObjectBoundsMeters.Position.X,
                            y = scene.ArrivalFraming.RouteAndObjectBoundsMeters.Position.Y,
                            z = scene.ArrivalFraming.RouteAndObjectBoundsMeters.Position.Z,
                        },
                        routeAndObjectBoundsSizeMeters = new
                        {
                            x = scene.ArrivalFraming.RouteAndObjectBoundsMeters.Size.X,
                            y = scene.ArrivalFraming.RouteAndObjectBoundsMeters.Size.Y,
                            z = scene.ArrivalFraming.RouteAndObjectBoundsMeters.Size.Z,
                        },
                        focusWorldMeters = new
                        {
                            x = scene.ArrivalFraming.FocusWorldMeters.X,
                            y = scene.ArrivalFraming.FocusWorldMeters.Y,
                            z = scene.ArrivalFraming.FocusWorldMeters.Z,
                        },
                        scene.ArrivalFraming.PaddingFraction,
                        cameraSizeMeters = runtime.Player.CameraSizeMeters,
                        runtime.Player.VillageArrivalFramingMode,
                    },
                    scene.PresentationProfilePath,
                    scene.PresentationProfileSha256,
                    playerMode = runtime.Player.VillageHumanoid.PresentationMode,
                    playerMeshes = runtime.Player.VillageHumanoid.MeshInstances,
                    playerSurfaces = runtime.Player.VillageHumanoid.AuthoredSurfaces,
                    playerLitMaterials = runtime.Player.VillageHumanoid.LitMaterials,
                    playerDonorManifestSha256 = runtime.Player.VillageHumanoid.GetMeta(
                        "donor_manifest_sha256").AsString(),
                    playerDonorModelSha256 = runtime.Player.VillageHumanoid.GetMeta(
                        "donor_model_sha256").AsString(),
                    destinationPresentationLoaded = runtime.Player
                        .GetMeta("destination_presentation_loaded").AsBool(),
                },
                firstLegalAction = new
                {
                    host.TrialRoute.VillageArrival.FirstActionFromTile,
                    host.TrialRoute.VillageArrival.FirstActionToTile,
                    host.TrialRoute.VillageArrival.FirstActionRotation,
                    applied = false,
                },
            });
        return (path, sha256);
    }

    private static string PrepareOutput(string configured, bool allowExisting)
    {
        var path = System.IO.Path.GetFullPath(configured);
        if (File.Exists(path) || Directory.Exists(path) && !allowExisting)
            throw new InvalidOperationException(
                $"Refusing to overwrite Fallout 2 trial proof output: {path}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteReport(string path, object value)
    {
        if (File.Exists(path))
            throw new InvalidOperationException(
                $"Refusing to overwrite Fallout 2 trial proof report: {path}");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        File.WriteAllText(
            path + ".sha256",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() +
                System.Environment.NewLine);
    }

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T typed)
                yield return typed;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed record PairedGateFrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);
}
