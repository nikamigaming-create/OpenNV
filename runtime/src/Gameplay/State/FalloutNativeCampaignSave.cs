using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutNativeSavedItem(
    uint RuntimeFormId,
    string EditorId,
    string RecordType,
    int Count,
    IReadOnlyList<FalloutItemVariant>? Variants = null, bool UnequipLocked = false);
internal sealed record FalloutFinishedSpeechStageScope(FalloutFormKey Quest, short Stage);
internal sealed record FalloutPlayerPackageAudioSnapshot(FalloutAnimationSoundEventsSnapshot Events, ulong RandomState);

internal sealed record FalloutNativeCampaignState(
    string Schema,
    string SaveCompatibilityId,
    FalloutFormKey ActiveCell,
    string QuestEditorId,
    short Stage,
    string PlayerName,
    FalloutNativeRaceSexSelection Character,
    FalloutNativeSpecialState Special,
    IReadOnlyList<FalloutNativeSkillIdentity> TagSkills,
    IReadOnlyList<FalloutNativeTraitIdentity> Traits,
    IReadOnlyList<FalloutNativeSavedItem> Inventory,
    IReadOnlyList<uint> EquippedRuntimeFormIds,
    IReadOnlyList<bool> PlayerControls,
    IReadOnlyList<float> PlayerPosition,
    IReadOnlyList<float> PlayerRotation,
    IReadOnlyList<FalloutQuestSnapshot>? Quests = null,
    FalloutQuestScriptsSnapshot? Scripts = null,
    FalloutGlobalStateSnapshot? Globals = null,
    FalloutGameTimeSnapshot? GameTime = null,
    FalloutSkyLightingSnapshot? SkyLighting = null,
    IReadOnlyList<FalloutReferenceSnapshot>? References = null,
    ulong? InventoryRandomState = null, bool CharacterCreationComplete = true, GameplayVitals? Vitals = null,
    FalloutWeaponHandlingSnapshot? WeaponHandling = null, float? PlayerViewPitchRadians = null,
    FalloutIngestiblesSnapshot? Ingestibles = null, IReadOnlyList<FalloutActorOverrides>? ActorOverrides = null,
    IReadOnlyList<FalloutEncounterZoneSnapshot>? EncounterZones = null,
    IReadOnlyList<FalloutExplosionExposure>? ExplosionExposure = null,
    FalloutPlayerActorValuesSnapshot? PlayerActorValues = null,
    FalloutPlayerTagSkillsSnapshot? TagSkillSlots = null,
    IReadOnlyList<FalloutFactionRelationSnapshot>? FactionRelations = null,
    FalloutDetectionEventsSnapshot? DetectionEvents = null,
    FalloutNativeFinishedSpeechSnapshot? FinishedSpeech = null,
    FalloutFinishedSpeechStageScope? FinishedSpeechStage = null,
    IReadOnlyList<FalloutQuestStageResultSnapshot>? QuestStageResults = null,
    FalloutQuestStageDriverFailure? StageResultFailure = null,
    IReadOnlyList<FalloutTerminalClosedSnapshot>? TerminalResults = null,
    FalloutPlayerPackageAudioSnapshot? PlayerPackageAudio = null,
    FalloutPlayerProgressSnapshot? PlayerProgress = null,
    RuntimeSaveRequestOrderSnapshot? SaveOrder = null,
    FalloutExperienceNotificationSnapshot? ExperienceNotifications = null,
    FalloutInterfaceActivationFrameSnapshot? InterfaceActivationFrames = null,
    FalloutPlayerPhysicalSnapshot? PlayerPhysical = null,
    FalloutCombatGroupsSnapshot? CombatGroups = null,
    FalloutActorPerceptionSnapshot? ActorPerception = null,
    FalloutActorProcessesSnapshot? ActorProcesses = null,
    FalloutSleepWaitSnapshot? SleepWait = null,
    FalloutRestAutoSaveSnapshot? RestAutoSave = null,
    FalloutRestWorldTimeSnapshot? RestWorldTime = null,
    FalloutRestInterfaceSoundSnapshot? RestInterfaceSounds = null,
    FalloutInterfaceFadeSnapshot? InterfaceFades = null,
    FalloutHardcoreNeedSnapshot? HardcoreNeeds = null,
    FalloutActorUpdateSnapshot? ActorUpdates = null, FalloutCellProcessesSnapshot? CellProcesses = null,
    FalloutActorProcessRuntimeSnapshot? ActorProcessRuntime = null, FalloutProcessCommonSnapshot? ActorProcessCommon = null,
    FalloutPlayerSkillValuesSnapshot? PlayerSkillValues = null, FalloutPlayerAbilityScriptsSnapshot? PlayerAbilityScripts = null,
    FalloutPlayerStatisticsSnapshot? PlayerStatistics = null, FalloutProcessQueueSnapshots? ProcessQueues = null,
    FalloutIndexedInterfaceSoundSnapshot? IndexedInterfaceSounds = null,
    FalloutSharedScriptRuntimeSnapshot? SharedScriptState = null);

internal sealed record FalloutNativeCampaignRestore(
    FalloutNativeCampaignState State,
    FalloutCampaignInventory Inventory, RuntimeSaveRequestColdLoad? SaveRequestLoad = null);

internal static partial class FalloutNativeCampaignSave
{
    internal const string ExpectedSchema = "opennv-native-campaign-save/v1";
    private const int PositionComponents = 3;
    private const int RotationComponents = 4;
    private const int PlayerControlCount = 7;
    private const int RolloverTextControlIndex = 5;
    private const int SneakingControlIndex = 6;
    private const float MinimumUnitQuaternionLengthSquared = 0.999f;
    private const float MaximumUnitQuaternionLengthSquared = 1.001f;

    internal static FalloutNativeCampaignState Capture(
        FalloutPluginStack records,
        string saveCompatibilityId,
        FalloutFormKey activeCell,
        FalloutOpeningInventoryGrant grant,
        string playerName,
        FalloutNativeRaceSexSelection character,
        FalloutNativeSpecialState special,
        IReadOnlyList<FalloutNativeSkillIdentity> tagSkills,
        IReadOnlyList<FalloutNativeTraitIdentity> traits,
        FalloutPlayerControlState playerControls,
        IReadOnlyList<float> playerPosition,
        IReadOnlyList<float> playerRotation,
        IReadOnlyList<FalloutQuestSnapshot>? quests = null,
        FalloutQuestScriptsSnapshot? scripts = null,
        FalloutGlobalStateSnapshot? globals = null,
        FalloutGameTimeSnapshot? gameTime = null,
        FalloutSkyLightingSnapshot? skyLighting = null,
        IReadOnlyList<FalloutReferenceSnapshot>? references = null, string? questEditorId = null,
        short stage = 0, bool characterCreationComplete = true, float playerViewPitchRadians = 0,
        FalloutPlayerActorValuesSnapshot? playerActorValues = null,
        FalloutPlayerTagSkillsSnapshot? tagSkillSlots = null,
        FalloutDetectionEventsSnapshot? detectionEvents = null,
        FalloutNativeFinishedSpeechSnapshot? finishedSpeech = null,
        FalloutFinishedSpeechStageScope? finishedSpeechStage = null,
        IReadOnlyList<FalloutQuestStageResultSnapshot>? questStageResults = null,
        FalloutQuestStageDriverFailure? stageResultFailure = null,
        IReadOnlyList<FalloutTerminalClosedSnapshot>? terminalResults = null,
        IReadOnlyList<FalloutNativeSkillIdentity>? skillCatalog = null,
        FalloutPlayerPackageAudioSnapshot? playerPackageAudio = null,
        GameplayVitals? vitals = null, FalloutWeaponHandlingSnapshot? weaponHandling = null,
        FalloutIngestiblesSnapshot? ingestibles = null, IReadOnlyList<FalloutActorOverrides>? actorOverrides = null,
        IReadOnlyList<FalloutEncounterZoneSnapshot>? encounterZones = null,
        IReadOnlyList<FalloutExplosionExposure>? explosionExposure = null,
        IReadOnlyList<FalloutFactionRelationSnapshot>? factionRelations = null,
        FalloutPlayerProgressSnapshot? playerProgress = null, RuntimeSaveRequestOrderSnapshot? saveOrder = null,
        FalloutExperienceNotificationSnapshot? experienceNotifications = null,
        FalloutInterfaceActivationFrameSnapshot? interfaceActivationFrames = null,
        FalloutPlayerPhysicalSnapshot? playerPhysical = null,
        FalloutCombatGroupsSnapshot? combatGroups = null,
        FalloutActorPerceptionSnapshot? actorPerception = null,
        FalloutActorProcessesSnapshot? actorProcesses = null,
        FalloutSleepWaitSnapshot? sleepWait = null,
        FalloutRestAutoSaveSnapshot? restAutoSave = null,
        FalloutRestWorldTimeSnapshot? restWorldTime = null,
        FalloutRestInterfaceSoundSnapshot? restInterfaceSounds = null,
        FalloutInterfaceFadeSnapshot? interfaceFades = null,
        FalloutHardcoreNeedSnapshot? hardcoreNeeds = null,
        FalloutActorUpdateSnapshot? actorUpdates = null, FalloutCellProcessesSnapshot? cellProcesses = null,
        FalloutActorProcessRuntimeSnapshot? actorProcessRuntime = null, FalloutProcessCommonSnapshot? actorProcessCommon = null,
        FalloutPlayerSkillValuesSnapshot? playerSkillValues = null, FalloutPlayerAbilityScriptsSnapshot? playerAbilityScripts = null,
        FalloutPlayerStatisticsSnapshot? playerStatistics = null, FalloutProcessQueueSnapshots? processQueues = null,
        FalloutIndexedInterfaceSoundSnapshot? indexedInterfaceSounds = null,
        FalloutSharedScriptRuntimeSnapshot? sharedScriptState = null)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(questEditorId);
        if (playerActorValues is null || tagSkillSlots is null)
            throw new InvalidDataException("Campaign capture requires its actual player values and indexed tag slots.");
        FalloutPlayerTagSkills.Validate(tagSkillSlots, tagSkills);
        skillCatalog ??= FalloutNativeTagSkillResolver.ResolveSkills(records);
        if (tagSkillSlots.Slots.Any(skill => skill is not null && !skillCatalog.Contains(skill)))
            throw new InvalidDataException("Captured player tag slot differs from its winning AVIF identity.");
        FalloutTraitMenuCatalogue.Validate(records, traits);
        var state = new FalloutNativeCampaignState(
            ExpectedSchema,
            saveCompatibilityId,
            activeCell,
            questEditorId,
            stage,
            playerName,
            character,
            special,
            tagSkills.OrderBy(value => value.RuntimeFormId).ToArray(),
            traits.OrderBy(value => value.RuntimeFormId).ToArray(),
            grant.Inventory.Items.OrderBy(value => value.RuntimeFormId)
                .Select(value => new FalloutNativeSavedItem(
                    value.RuntimeFormId,
                    value.EditorId,
                    value.RecordType,
                    value.Count, value.Variants, value.UnequipLocked))
                .ToArray(),
            grant.EquippedRuntimeFormIds.Order().ToArray(),
            [
                playerControls.Movement,
                playerControls.PipBoy,
                playerControls.Fighting,
                playerControls.PointOfView,
                playerControls.Looking,
                playerControls.RolloverText,
                playerControls.Sneaking,
            ],
            playerPosition.ToArray(),
            playerRotation.ToArray(), quests, scripts, globals, gameTime, skyLighting, references, grant.InventoryRandomState, characterCreationComplete,
            Vitals: vitals, WeaponHandling: weaponHandling, PlayerViewPitchRadians: playerViewPitchRadians,
            Ingestibles: ingestibles, ActorOverrides: actorOverrides, EncounterZones: encounterZones,
            ExplosionExposure: explosionExposure, PlayerActorValues: playerActorValues,
            TagSkillSlots: tagSkillSlots, FactionRelations: factionRelations, DetectionEvents: detectionEvents, FinishedSpeech: finishedSpeech,
            FinishedSpeechStage: finishedSpeechStage, QuestStageResults: questStageResults, StageResultFailure: stageResultFailure,
            TerminalResults: terminalResults, PlayerPackageAudio: playerPackageAudio, PlayerProgress: playerProgress, SaveOrder: saveOrder,
            ExperienceNotifications: experienceNotifications, InterfaceActivationFrames: interfaceActivationFrames,
            PlayerPhysical: playerPhysical, CombatGroups: combatGroups, ActorPerception: actorPerception, ActorProcesses: actorProcesses,
            SleepWait: sleepWait, RestAutoSave: restAutoSave, RestWorldTime: restWorldTime,
            RestInterfaceSounds: restInterfaceSounds, InterfaceFades: interfaceFades, HardcoreNeeds: hardcoreNeeds,
            ActorUpdates: actorUpdates, CellProcesses: cellProcesses, ActorProcessRuntime: actorProcessRuntime, ActorProcessCommon: actorProcessCommon,
            PlayerSkillValues: playerSkillValues, PlayerAbilityScripts: playerAbilityScripts, PlayerStatistics: playerStatistics, ProcessQueues: processQueues,
            IndexedInterfaceSounds: indexedInterfaceSounds, SharedScriptState: sharedScriptState);
        Validate(state, saveCompatibilityId);
        ValidateSaveOrderSource(records, state);
        ValidateExperienceNotificationSource(records, state);
        ValidatePlayerPhysicalSource(records, state);
        ValidateCurrentRestSource(records, state);
        ValidateCurrentPlayerAbilitySource(records, state);
        ValidateCurrentPlayerStatisticSource(records, state);
        return state;
    }

    internal static void Write(string path, FalloutNativeCampaignState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        Validate(state, state.SaveCompatibilityId);
        RuntimeSaveRequestOrder.RequirePublishedCapture(state.SaveOrder!);
        RuntimeAtomicSaveFile.Write(path, System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine));
    }

    internal static FalloutNativeCampaignRestore Read(
        string path,
        string expectedSaveCompatibilityId,
        FalloutPluginStack stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(stack);
        var fullPath = Path.GetFullPath(path);
        var state = JsonSerializer.Deserialize<FalloutNativeCampaignState>(
                File.ReadAllText(fullPath)) ??
            throw new InvalidDataException($"Native campaign save is empty: {fullPath}");
        Validate(state, expectedSaveCompatibilityId);
        ValidateCompiledScriptStorage(stack, state);
        ValidateExperienceNotificationSource(stack, state);
        ValidatePlayerPhysicalSource(stack, state);
        ValidateCurrentRestSource(stack, state);
        ValidateCurrentPlayerAbilitySource(stack, state);
        ValidateCurrentPlayerStatisticSource(stack, state);
        ValidateCurrentSourceSky(stack, state);
        if (state.PlayerPackageAudio is { } playerAudio)
            FalloutAnimationSoundEvents.ValidateSource(playerAudio.Events, stack, stack.RuntimeFormKey(0x14));
        var activeCell = stack.GetEffective(state.ActiveCell);
        if (activeCell.Signature != "CELL")
            throw new InvalidDataException(
                "Native campaign save active CELL differs from the live winning records.");
        if (state.FinishedSpeechStage is { } speechStage &&
            (stack.GetEffective(speechStage.Quest).Signature != "QUST" || stack.GetEffective(speechStage.Quest).IsDeleted))
            throw new InvalidDataException("Saved finished speech stage has no winning source quest.");
        var characterContract = FalloutNativeRaceSexResolver.Resolve(stack);
        FalloutNativeRaceSexResolver.Validate(characterContract, state.Character);
        if (state.Character.Face is { } face)
        {
            var player = stack.GetEffective(characterContract.Player);
            foreach (var (signature, bytes) in new[] { ("FGGS", face.SymmetricGeometry), ("FGGA", face.AsymmetricGeometry), ("FGTS", face.SymmetricTexture) })
                if (player.ReadSubrecords().Single(row => row.Signature == signature).Data.Length != bytes.Length)
                    throw new InvalidDataException("Saved face coefficient extent differs from the owned player model.");
            foreach (var id in face.HeadParts)
                if (stack.GetEffective(stack.RuntimeFormKey(id)).Signature != "HDPT") throw new InvalidDataException("Saved player head part is not an owned HDPT.");
        }
        _ = new FalloutPlayerActorValues(stack, state.PlayerActorValues);
        _ = new FalloutPlayerTagSkills(stack, FalloutNativeTagSkillResolver.ResolveSkills(stack), snapshot: state.TagSkillSlots);
        FalloutTraitMenuCatalogue.Validate(stack, state.Traits);
        var inventory = FalloutCampaignInventoryResolver.Resolve(
            stack,
            state.Inventory.Select(value => new FalloutCampaignInventoryRequest(
                value.RuntimeFormId,
                value.EditorId,
                value.RecordType,
                value.Count)).ToArray(),
            null);
        var savedItems = state.Inventory.ToDictionary(item => item.RuntimeFormId);
        inventory = inventory with
        {
            Items = inventory.Items.Select(item => item with
            { Variants = savedItems[item.RuntimeFormId].Variants, UnequipLocked = savedItems[item.RuntimeFormId].UnequipLocked }).ToArray()
        };
        var validatedInventory = new FalloutPlayerInventory();
        validatedInventory.Restore(inventory, state.EquippedRuntimeFormIds.ToArray(), state.InventoryRandomState);
        if (!inventory.Items.OrderBy(value => value.RuntimeFormId)
                .Select(value => (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))
                .SequenceEqual(state.Inventory.OrderBy(value => value.RuntimeFormId)
                    .Select(value =>
                        (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))))
            throw new InvalidDataException(
                "Native campaign save inventory differs from the live winning records.");
        FalloutQuestState? validatedQuests = null;
        FalloutScriptValueStore? validatedValues = null;
        if (state.Quests is not null)
        {
            validatedQuests = new FalloutQuestState(stack);
            validatedQuests.Restore(state.Quests);
        }
        if (state.QuestStageResults is { } stageResults)
        {
            var stages = new FalloutQuestStages(stack, validatedQuests ?? new FalloutQuestState(stack),
                (_, _, _) => throw new InvalidDataException("Stage-result validation cannot execute source programs."),
                _ => throw new InvalidDataException("Stage-result validation cannot execute source predicates."));
            stages.RestoreResults(stageResults);
        }
        if (state.TerminalResults is { } terminalResults)
            FalloutTerminalMenu.ValidateClosed(stack, terminalResults, state.QuestStageResults ?? []);
        if (state.Scripts is { } savedScripts)
        {
            new FalloutQuestObjectFlags(stack).Restore(savedScripts.Session?.QuestObjects);
            using (var challengeWorld = new FalloutReferenceWorld(stack))
            {
                challengeWorld.ConfigureCampaignPlayerRuntime(new(), state.PlayerStatistics ??
                    throw new InvalidDataException("Current challenge state has no player statistics."),
                    savedScripts.Challenges ?? throw new InvalidDataException("Current challenge state is absent."),
                    state.IndexedInterfaceSounds ?? throw new InvalidDataException("Current indexed audio state is absent."));
                challengeWorld.CampaignIndexedInterfaceSounds.RequireRestContinuation(state.RestInterfaceSounds ??
                    throw new InvalidDataException("Current rest sound caller state is absent."));
            }
            using var radioWorld = new FalloutReferenceWorld(stack);
            new FalloutRadioStations(stack, radioWorld, new()).Restore(savedScripts.Radio);
            validatedValues = new FalloutScriptValueStore();
            validatedValues.Restore(savedScripts.Values);
            if (state.Quests is not null)
                ValidateQuestValueHandles(stack, state.Quests, validatedValues);
        }
        if (state.Globals is not null) FalloutGlobalState.Read(stack).Restore(state.Globals);
        if (state.SkyLighting is not null) FalloutSkyLightingState.ValidateSnapshot(stack, state.SkyLighting);
        if (state.References is not null)
        {
            using var references = new FalloutReferenceWorld(stack, validatedValues);
            references.RestoreEncounterZones(state.EncounterZones);
            references.Restore(state.References);
            references.RestoreDetection(state.DetectionEvents);
            if (state.FinishedSpeech is { } finishedSpeech) RuntimeNativeSpeech.ValidateFinishedState(stack, references, finishedSpeech);
            references.ValidateValueHandles();
            references.RestoreActorOverrides(state.ActorOverrides);
            references.RestoreFactionRelations(state.FactionRelations);
        }
        validatedValues?.Arrays.ValidateRestoredRoots();
        foreach (var form in validatedValues?.Arrays.Forms ?? [])
            if (form is not (0 or 0x14)) _ = stack.GetEffective(stack.RuntimeFormKey(form));
        return new FalloutNativeCampaignRestore(state, inventory, ReadSaveOrder(fullPath, stack, state));
    }

    private static void ValidateQuestValueHandles(FalloutPluginStack stack,
        IReadOnlyList<FalloutQuestSnapshot> snapshots, FalloutScriptValueStore values)
    {
        foreach (var snapshot in snapshots)
        {
            var quest = stack.GetEffective(snapshot.Quest);
            var script = FalloutScriptLocals.AttachedScript(stack, quest);
            if (script is null) continue;
            foreach (var declaration in FalloutScriptLocals.ReadDeclarations(script).Values)
            {
                if (declaration.Kind is not (FalloutScriptLocalKind.String or FalloutScriptLocalKind.Array)) continue;
                if (!snapshot.Variables.TryGetValue(declaration.Index, out var raw))
                    throw new InvalidDataException("Saved quest value local is absent from its winning declaration.");
                values.ValidateLocal(declaration.Kind, raw, $"{snapshot.Quest}:{declaration.Index}");
            }
        }
    }

    internal static FalloutNativeCampaignState WithWorldState(
        FalloutNativeCampaignState state,
        FalloutFormKey activeCell,
        IReadOnlyList<float> playerPosition,
        IReadOnlyList<float> playerRotation, float? playerViewPitchRadians = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state, state.SaveCompatibilityId);
        var updated = state with
        {
            ActiveCell = activeCell,
            PlayerPosition = playerPosition.ToArray(),
            PlayerRotation = playerRotation.ToArray(),
            PlayerViewPitchRadians = playerViewPitchRadians ?? RestorePlayerViewPitch(state),
        };
        Validate(updated, state.SaveCompatibilityId);
        return updated;
    }

    internal static float RestorePlayerViewPitch(FalloutNativeCampaignState state)
    {
        Validate(state, state.SaveCompatibilityId);
        return state.PlayerViewPitchRadians!.Value;
    }

    internal static float[] RestorePlayerPosition(FalloutNativeCampaignState state)
    {
        Validate(state, state.SaveCompatibilityId);
        return state.PlayerPosition.ToArray();
    }

    internal static FalloutPlayerControlState RestorePlayerControls(
        FalloutNativeCampaignState state)
    {
        Validate(state, state.SaveCompatibilityId);
        return new FalloutPlayerControlState(
            state.PlayerControls[0],
            state.PlayerControls[1],
            state.PlayerControls[2],
            state.PlayerControls[3],
            state.PlayerControls[4],
            state.PlayerControls[RolloverTextControlIndex],
            state.PlayerControls[SneakingControlIndex]);
    }

}
