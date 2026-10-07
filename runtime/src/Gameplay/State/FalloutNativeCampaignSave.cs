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
    FalloutPlayerPackageAudioSnapshot? PlayerPackageAudio = null);

internal sealed record FalloutNativeCampaignRestore(
    FalloutNativeCampaignState State,
    FalloutCampaignInventory Inventory);

internal static class FalloutNativeCampaignSave
{
    internal const string ExpectedSchema = "opennv-native-fnv-campaign-save/v47";
    internal const string ObjectPcmSchema = "opennv-native-fnv-campaign-save/v46";
    internal const string CorpseTransferSchema = "opennv-native-fnv-campaign-save/v45";
    internal const string ActivationRelaySchema = "opennv-native-fnv-campaign-save/v44";
    internal const string NativeSoundHistorySchema = "opennv-native-fnv-campaign-save/v43";
    internal const string TerminalResultsSchema = "opennv-native-fnv-campaign-save/v42";
    internal const string ClosedStageSchema = "opennv-native-fnv-campaign-save/v41";
    internal const string FinishedSpeechSchema = "opennv-native-fnv-campaign-save/v40";
    internal const string OccupiedIdleSchema = "opennv-native-fnv-campaign-save/v39";
    internal const string FactionRelationSchema = "opennv-native-fnv-campaign-save/v38";
    internal const string DeathHistorySchema = "opennv-native-fnv-campaign-save/v37";
    internal const string ProcedureSchema = "opennv-native-fnv-campaign-save/v36";
    internal const string BroadcastSchema = "opennv-native-fnv-campaign-save/v35";
    internal const string IndexedTagSchema = "opennv-native-fnv-campaign-save/v34";
    internal const string UnindexedTagSchema = "opennv-native-fnv-campaign-save/v33";
    internal const string StoppedPackageSchema = "opennv-native-fnv-campaign-save/v32";
    internal const string CreatureTravelSchema = "opennv-native-fnv-campaign-save/v31";
    internal const string RadioSchema = "opennv-native-fnv-campaign-save/v30";
    internal const string DialoguePackageSchema = "opennv-native-fnv-campaign-save/v29";
    internal const string EditorTravelSchema = "opennv-native-fnv-campaign-save/v28";
    internal const string SignedRaceSchema = "opennv-native-fnv-campaign-save/v27";
    internal const string ReferenceAccessSchema = "opennv-native-fnv-campaign-save/v26";
    internal const string ReferenceAccessLegacySchema = "opennv-native-fnv-campaign-save/v25";
    internal const string RaceOverridesSchema = "opennv-native-fnv-campaign-save/v24";
    internal const string ScriptValuesSchema = "opennv-native-fnv-campaign-save/v23";
    internal const string ObjectAnimationSchema = "opennv-native-fnv-campaign-save/v22";
    internal const string DestructionSchema = "opennv-native-fnv-campaign-save/v21";
    internal const string HitReactionSchema = "opennv-native-fnv-campaign-save/v20";
    internal const string DeathEventSchema = "opennv-native-fnv-campaign-save/v19";
    internal const string PatrolSchema = "opennv-native-fnv-campaign-save/v18";
    internal const string EncounterZoneSchema = "opennv-native-fnv-campaign-save/v17";
    internal const string ActorOverridesSchema = "opennv-native-fnv-campaign-save/v16";
    internal const string PackageMotionSchema = "opennv-native-fnv-campaign-save/v15";
    internal const string IngestiblesSchema = "opennv-native-fnv-campaign-save/v14";
    internal const string ViewPitchSchema = "opennv-native-fnv-campaign-save/v13";
    internal const string ReferenceStateSchema = "opennv-native-fnv-campaign-save/v12";
    internal const string QuestClockSchema = "opennv-native-fnv-campaign-save/v11";
    internal const string SkyLightingSchema = "opennv-native-fnv-campaign-save/v10";
    internal const string GlobalClockSchema = "opennv-native-fnv-campaign-save/v9";
    internal const string QuestScriptsSchema = "opennv-native-fnv-campaign-save/v8";
    internal const string FeetAnchoredSchema = "opennv-native-fnv-campaign-save/v7";
    internal const string CapsuleCenteredSchema = "opennv-native-fnv-campaign-save/v6";
    internal const string OpeningQuestEditorId = "VCG01";
    internal const short CompletedOpeningStage = 200;
    private const int PositionComponents = 3;
    private const int RotationComponents = 4;
    private const int PlayerControlCount = 7;
    private const int RolloverTextControlIndex = 5;
    private const int SneakingControlIndex = 6;
    private const float MinimumUnitQuaternionLengthSquared = 0.999f;
    private const float MaximumUnitQuaternionLengthSquared = 1.001f;

    internal static FalloutNativeCampaignState Capture(
        string saveCompatibilityId,
        FalloutFormKey activeCell,
        FalloutOpeningInventoryGrant grant,
        string playerName,
        FalloutNativeRaceSexSelection character,
        FalloutNativeVigorContract? vigorContract,
        FalloutNativeSpecialState special,
        FalloutNativeTagSkillContract? tagSkillContract,
        IReadOnlyList<FalloutNativeSkillIdentity> tagSkills,
        FalloutNativeTraitFarewellContract? traitFarewellContract,
        IReadOnlyList<FalloutNativeTraitIdentity> traits,
        FalloutPlayerControlState playerControls,
        IReadOnlyList<float> playerPosition,
        IReadOnlyList<float> playerRotation,
        IReadOnlyList<FalloutQuestSnapshot>? quests = null,
        FalloutQuestScriptsSnapshot? scripts = null,
        FalloutGlobalStateSnapshot? globals = null,
        FalloutGameTimeSnapshot? gameTime = null,
        FalloutSkyLightingSnapshot? skyLighting = null,
        IReadOnlyList<FalloutReferenceSnapshot>? references = null, string questEditorId = OpeningQuestEditorId,
        short stage = CompletedOpeningStage, bool characterCreationComplete = true, float playerViewPitchRadians = 0,
        FalloutPlayerActorValuesSnapshot? playerActorValues = null,
        FalloutPlayerTagSkillsSnapshot? tagSkillSlots = null,
        FalloutDetectionEventsSnapshot? detectionEvents = null,
        FalloutNativeFinishedSpeechSnapshot? finishedSpeech = null,
        FalloutFinishedSpeechStageScope? finishedSpeechStage = null,
        IReadOnlyList<FalloutQuestStageResultSnapshot>? questStageResults = null,
        FalloutQuestStageDriverFailure? stageResultFailure = null,
        IReadOnlyList<FalloutTerminalClosedSnapshot>? terminalResults = null,
        IReadOnlyList<FalloutNativeSkillIdentity>? skillCatalog = null,
        FalloutPlayerPackageAudioSnapshot? playerPackageAudio = null)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (playerActorValues is null) FalloutNativeVigorResolver.Validate(vigorContract ??
            throw new NotSupportedException("Legacy player values require their source creation contract."), special, allowUnspent: !characterCreationComplete);
        if (tagSkillSlots is null)
            FalloutNativeTagSkillResolver.Validate(tagSkillContract ??
                throw new NotSupportedException("Legacy player tags require their source creation contract."), tagSkills, allowUnspent: !characterCreationComplete);
        tagSkillSlots ??= FalloutPlayerTagSkills.FromLegacy(tagSkills);
        FalloutPlayerTagSkills.Validate(tagSkillSlots, tagSkills);
        skillCatalog ??= tagSkillContract?.Skills ?? throw new NotSupportedException("Player tags have no winning skill catalog.");
        if (tagSkillSlots.Slots.Any(skill => skill is not null && !skillCatalog.Contains(skill)))
            throw new InvalidDataException("Captured player tag slot differs from its winning AVIF identity.");
        ValidateTraits(traitFarewellContract, traits);
        var state = new FalloutNativeCampaignState(
            references is not null ? playerPackageAudio is null ? ObjectPcmSchema : ExpectedSchema : skyLighting is not null ? QuestClockSchema : globals is null ? QuestScriptsSchema : GlobalClockSchema,
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
            PlayerViewPitchRadians: playerViewPitchRadians, EncounterZones: references is null ? null : [], PlayerActorValues: playerActorValues,
            TagSkillSlots: tagSkillSlots, FactionRelations: [], DetectionEvents: detectionEvents, FinishedSpeech: finishedSpeech,
            FinishedSpeechStage: finishedSpeechStage, QuestStageResults: questStageResults, StageResultFailure: stageResultFailure,
            TerminalResults: references is null ? terminalResults : terminalResults ?? [], PlayerPackageAudio: playerPackageAudio);
        Validate(state, saveCompatibilityId);
        return state;
    }

    internal static void Write(string path, FalloutNativeCampaignState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        Validate(state, state.SaveCompatibilityId);
        RuntimeAtomicSaveFile.Write(path, System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine));
    }

    internal static FalloutNativeCampaignRestore Read(
        string path,
        string expectedSaveCompatibilityId,
        FalloutPluginStack stack,
        FalloutNativeVigorContract? vigorContract,
        FalloutNativeTagSkillContract? tagSkillContract,
        FalloutOpeningInventoryGrant? openingGrant,
        FalloutNativeTraitFarewellContract? traitFarewellContract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(stack);
        var fullPath = Path.GetFullPath(path);
        var state = JsonSerializer.Deserialize<FalloutNativeCampaignState>(
                File.ReadAllText(fullPath)) ??
            throw new InvalidDataException($"Native campaign save is empty: {fullPath}");
        Validate(state, expectedSaveCompatibilityId);
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
        if (state.PlayerActorValues is null)
        {
            FalloutNativeVigorResolver.Validate(vigorContract ??
                throw new NotSupportedException("Legacy player values require their source creation contract."), state.Special, allowUnspent: !state.CharacterCreationComplete);
            // Legacy saves held only seven BASE integers. Runtime modifiers
            // start at zero; current winning source abilities are re-evaluated.
            state = state with { PlayerActorValues = new FalloutPlayerActorValues(stack, legacy: state.Special).Capture() };
        }
        else _ = new FalloutPlayerActorValues(stack, state.PlayerActorValues);
        if (state.TagSkillSlots is null)
            FalloutNativeTagSkillResolver.Validate(tagSkillContract ??
                throw new NotSupportedException("Legacy player tags require their source creation contract."), state.TagSkills, allowUnspent: !state.CharacterCreationComplete);
        var tags = new FalloutPlayerTagSkills(stack, tagSkillContract?.Skills ?? FalloutNativeTagSkillResolver.ResolveSkills(stack),
            tagSkillContract?.RequiredCount, state.TagSkillSlots, state.TagSkills);
        state = state with { TagSkillSlots = tags.Capture() };
        ValidateTraits(traitFarewellContract, state.Traits);
        var expectedGrant = state.Scripts is not null ? null : FalloutNativeTraitFarewellResolver.ResolveGrant(
            traitFarewellContract ?? throw new NotSupportedException("Legacy inventory requires its source farewell contract."),
            openingGrant ?? throw new NotSupportedException("Legacy inventory requires its source opening grant."),
            state.TagSkills);
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
        if (state.Scripts is null && (!state.Inventory.OrderBy(value => value.RuntimeFormId)
                .Select(value => (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))
                .SequenceEqual(expectedGrant!.Inventory.Items.OrderBy(value => value.RuntimeFormId)
                    .Select(value =>
                        (value.RuntimeFormId, value.EditorId, value.RecordType, value.Count))) ||
            !state.EquippedRuntimeFormIds.Order()
                .SequenceEqual(expectedGrant!.EquippedRuntimeFormIds.Order())))
            throw new InvalidDataException(
                "Native campaign save loadout differs from the live farewell/tag-skill contract.");
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
            new FalloutChallenges(stack, new()).Restore(savedScripts.Challenges);
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
        if (state.Schema is CorpseTransferSchema or ActivationRelaySchema or NativeSoundHistorySchema or TerminalResultsSchema or ClosedStageSchema or FinishedSpeechSchema or OccupiedIdleSchema or FactionRelationSchema) state = state with { Schema = ObjectPcmSchema };
        if (state.Schema is not (ExpectedSchema or ObjectPcmSchema) && state.References is not null)
            state = state with
            {
                Schema = state.Schema is DeathHistorySchema or ProcedureSchema ? ObjectPcmSchema : state.Schema,
                References = RestoreLegacyDeathCounts(state),
                FactionRelations = []
            };
        if (state.Schema is ExpectedSchema or ObjectPcmSchema && state.References is not null)
            state = state with { QuestStageResults = state.QuestStageResults ?? [], TerminalResults = state.TerminalResults ?? [] };
        return new FalloutNativeCampaignRestore(state, inventory);
    }

    internal static void ValidateTraits(FalloutNativeTraitFarewellContract? contract, IReadOnlyList<FalloutNativeTraitIdentity> traits)
    {
        if (contract is not null) FalloutNativeTraitFarewellResolver.ValidateTraits(contract, traits);
        else if (traits.Count != 0) throw new InvalidDataException("Saved traits have no source creation contract.");
    }

    private static IReadOnlyList<FalloutReferenceSnapshot>? RestoreLegacyDeathCounts(FalloutNativeCampaignState state) =>
        state.Schema is ExpectedSchema or ObjectPcmSchema or CorpseTransferSchema or ActivationRelaySchema or NativeSoundHistorySchema or TerminalResultsSchema or ClosedStageSchema or FinishedSpeechSchema or OccupiedIdleSchema or FactionRelationSchema or DeathHistorySchema ? state.References : state.References?.Select(reference => reference with
        { DeathCount = reference.Injury?.DeathInventoryGranted == true ? 1 : null }).ToArray();

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
            Schema = state.References is not null ? state.PlayerPackageAudio is null ? ObjectPcmSchema : ExpectedSchema : state.SkyLighting is not null ? QuestClockSchema : state.Globals is null ? QuestScriptsSchema : GlobalClockSchema,
            ActiveCell = activeCell,
            PlayerPosition = playerPosition.ToArray(),
            PlayerRotation = playerRotation.ToArray(),
            PlayerViewPitchRadians = playerViewPitchRadians ?? RestorePlayerViewPitch(state),
            EncounterZones = state.References is null ? null : state.EncounterZones ?? [],
            TagSkillSlots = state.TagSkillSlots ?? FalloutPlayerTagSkills.FromLegacy(state.TagSkills),
            References = RestoreLegacyDeathCounts(state),
            QuestStageResults = state.References is null ? null : state.QuestStageResults ?? [],
            TerminalResults = state.References is null ? null : state.TerminalResults ?? [],
        };
        Validate(updated, state.SaveCompatibilityId);
        return updated;
    }

    internal static float RestorePlayerViewPitch(FalloutNativeCampaignState state)
    {
        Validate(state, state.SaveCompatibilityId);
        // Saves through v12 did not capture the independent desktop look angle.
        return state.PlayerViewPitchRadians ?? 0;
    }

    internal static float[] RestorePlayerPosition(FalloutNativeCampaignState state, float legacyCapsuleCenterHeight)
    {
        Validate(state, state.SaveCompatibilityId);
        if (!float.IsFinite(legacyCapsuleCenterHeight) || legacyCapsuleCenterHeight <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(legacyCapsuleCenterHeight));
        var position = state.PlayerPosition.ToArray();
        if (state.Schema == CapsuleCenteredSchema)
            position[1] -= legacyCapsuleCenterHeight;
        if (position.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Native saved player anchor conversion is non-finite.");
        return position;
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

    private static void Validate(
        FalloutNativeCampaignState state,
        string expectedSaveCompatibilityId)
    {
        if (state.Inventory is null) throw new InvalidDataException("Saved campaign inventory is absent.");
        if (state.Schema != ExpectedSchema && state.PlayerPackageAudio is not null)
            throw new InvalidDataException("Legacy campaign schema contains future player package sound history.");
        if (state.Schema == ExpectedSchema && state.References is not null && state.PlayerPackageAudio is null)
            throw new InvalidDataException("Saved campaign is missing its player package sound owner.");
        state.PlayerPackageAudio?.Events.Validate();
        if (state.PlayerPackageAudio is { } playerAudio && state.Scripts?.Session?.PlayerPackage?.SoundRandomState is { } packageRandom &&
            playerAudio.RandomState != packageRandom)
            throw new InvalidDataException("Player package and audio random state disagree.");
        if (state.Schema == ObjectPcmSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.DoorMotion?.ScriptSequence is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future scripted door animation state.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference =>
            reference.ObjectAnimations?.Any(clock => !clock.ScriptSelected) == true) == true)
            throw new InvalidDataException("Legacy campaign schema contains future automatic object animation state.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference =>
            reference.AnimationSoundEvents is { } sounds && (sounds.OpaqueError is not null || sounds.Faults is not null ||
                sounds.Events.Any(entry => entry.Playback is not null))) == true)
            throw new InvalidDataException("Legacy campaign schema contains future PCM or source-fault continuation.");
        if (state.Schema == CorpseTransferSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.CorpseEquipment is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future corpse equipment continuation.");
        if (state.Schema == ActivationRelaySchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.ActivationRelay is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future activation-parent cycles.");
        if (state.Schema == NativeSoundHistorySchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.References?.Any(reference =>
            reference.AnimationSoundEvents is not null || reference.HitReactionFaults is not null ||
            reference.PackageBindingFailure?.IndependentIdle is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future native sound/hit-reaction history.");
        // A prior save supplies no historical completion. Empty history starts
        // only when a new native event is actually observed after cold loading.
        if (state.Schema == TerminalResultsSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.TerminalResults is not null)
            throw new InvalidDataException("Legacy campaign schema contains future closed terminal results.");
        if (state.Schema == ExpectedSchema && state.References is not null && state.TerminalResults is null)
            throw new InvalidDataException("Saved campaign is missing its closed terminal result owner.");
        if (state.TerminalResults is not null && state.References is null)
            throw new InvalidDataException("Saved terminal results have no retained reference world.");
        if (state.TerminalResults is { } terminalResults)
        {
            FalloutTerminalMenu.ValidateClosedShape(terminalResults);
            if (terminalResults.Any(menu => state.References!.Count(reference => reference.Reference == menu.Reference) != 1))
                throw new InvalidDataException("Saved terminal has no unique retained placed reference state.");
        }
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.PendingPackageSelection is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future pending actor selection.");
        if (state.Schema == ClosedStageSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && (state.QuestStageResults is not null || state.StageResultFailure is not null))
            throw new InvalidDataException("Legacy campaign schema contains future quest-stage execution receipts.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.HeadTracking is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future native head-tracking continuation.");
        if (state.Schema == ExpectedSchema && state.References is not null && state.QuestStageResults is null)
            throw new InvalidDataException("Saved campaign is missing its closed quest-stage result owner.");
        if (state.QuestStageResults is not null && state.References is null)
            throw new InvalidDataException("Saved quest-stage results have no retained reference world.");
        if (state.QuestStageResults is { Count: > 0 } && state.Quests is null)
            throw new InvalidDataException("Saved quest-stage results have no consumed quest state.");
        if (state.QuestStageResults is { } stageResults) FalloutQuestStages.ValidateSnapshotShape(stageResults);
        FalloutQuestStages.ValidateDriverFailure(state.QuestStageResults, state.StageResultFailure);
        // v40 could capture neither pending nor failed stage results. Its absent
        // journal retains no historical error/cursor and invokes no source code.
        // Reject supplied future fields against the original label first.
        if (state.Schema == FinishedSpeechSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.ActorOverrides?.Any(actor => actor.Alerted is not null) == true)
            throw new InvalidDataException("Legacy campaign schema contains future actor alert state.");
        if (state.Schema != ExpectedSchema && (state.DetectionEvents is not null || state.FinishedSpeech is not null || state.FinishedSpeechStage is not null ||
            state.References?.Any(reference => reference.ScriptStoppedFrame is not null || reference.CompletedScriptContinuation is not null) == true))
            throw new InvalidDataException("Legacy campaign schema contains future detection/speech invocation state.");
        if (state.FinishedSpeechStage is { } stageScope && (stageScope.Stage < 0 || state.FinishedSpeech?.Failure is null))
            throw new InvalidDataException("Saved finished speech stage scope has no ended failure receipt.");
        if ((state.DetectionEvents is not null || state.FinishedSpeech is not null) && state.References is null)
            throw new InvalidDataException("Saved detection/speech has no retained reference world.");
        if (state.References?.Any(reference => reference.ScriptStoppedFrame?.PreparedDetection is not null ||
            reference.CompletedScriptContinuation?.PreparedDetection is not null) == true && state.DetectionEvents is null)
            throw new InvalidDataException("Stopped detection invocation is missing its original simulation clock.");
        // v39 already owns attack selection and occupied animation. Check future
        // fields against the original label before sharing its mature validators.
        if (state.Schema == OccupiedIdleSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && (state.WeaponHandling?.AttackRandomState is not null ||
            state.References?.Any(reference => reference.AttackRandomState is not null ||
                reference.Engagement?.AttackRandomState is not null ||
                reference.Engagement?.WeaponHandling?.AttackRandomState is not null) == true))
            throw new InvalidDataException("Legacy campaign schema has attack selection random state.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference =>
            reference.FurnitureContinuation?.IdleState?.ActiveAnimation is not null ||
            reference.DialogueContinuation?.IdleState.ActiveAnimation is not null ||
            reference.PackageBindingFailure?.IdleState?.ActiveAnimation is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has an active package collection animation.");
        if (state.Schema is not (ExpectedSchema or FactionRelationSchema) && state.FactionRelations is { Count: > 0 })
            throw new InvalidDataException("Legacy campaign schema has mutable faction reactions.");
        if (state.Schema is ExpectedSchema or FactionRelationSchema && state.FactionRelations is null)
            throw new InvalidDataException("Saved campaign is missing its faction reaction state.");
        if (state.FactionRelations is { Count: > 0 } && state.References is null)
            throw new InvalidDataException("Saved faction reactions have no reference world.");
        if (state.Schema == FactionRelationSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema is not (ExpectedSchema or DeathHistorySchema) && state.References?.Any(reference => reference.DeathCount is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has cumulative actor death history.");
        if (state.Schema is ExpectedSchema or DeathHistorySchema && state.References?.Any(reference =>
            reference.Injury?.DeathInventoryGranted == true && reference.DeathCount is not > 0) == true)
            throw new InvalidDataException("Saved killed actor has no cumulative death history.");
        // v36 owned no resurrection/respawn command. Its once-only granted
        // death inventory proves one consumed death; source corpses prove none.
        if (state.Schema is DeathHistorySchema or ProcedureSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema is ExpectedSchema or BroadcastSchema or IndexedTagSchema && state.TagSkillSlots is null)
            throw new InvalidDataException("Saved campaign is missing indexed player tag skills.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.PackageEvents is { Count: > 0 }) == true)
            throw new InvalidDataException("Legacy campaign schema has pending actor package events.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.FurnitureContinuation is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has furniture continuation.");
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.SelectionFailure is not null ||
            reference.DialogueContinuation is not null || reference.PackageBindingFailure?.IdleState is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has NPC selection, dialogue or idle continuation.");
        if (state.Schema is not (ExpectedSchema or BroadcastSchema) && state.References?.Any(reference => reference.BroadcastState is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has mutable radio broadcast state.");
        if (state.TagSkillSlots is { } tags) FalloutPlayerTagSkills.Validate(tags, state.TagSkills);
        // v33 contained membership only. No indexed script writes were owned in
        // that schema; its stored selection is the deterministic legacy order.
        if (state.Schema is UnindexedTagSchema or IndexedTagSchema or BroadcastSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.Scripts?.Session?.QuestObjects is { Count: > 0 })
            throw new InvalidDataException("Legacy campaign schema has mutable quest-object flags.");
        if (state.Schema is not (ExpectedSchema or StoppedPackageSchema) && state.References?.Any(reference => reference.PackageBindingFailure is not null || reference.PackageMotion?.Guard is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has actor package binding-failure continuation.");
        // Established v31/v32 lanes remain readable. Local validation never
        // rewrites a legacy file or supplies a missing mutable-form change.
        if (state.Schema is StoppedPackageSchema or CreatureTravelSchema) state = state with { Schema = ExpectedSchema };
        if (state.Schema != ExpectedSchema && state.References?.Any(reference => reference.PackageMotion?.Travel is not null ||
            reference.PackageIdle is not null || reference.PackageStarts is { Count: > 0 }) == true)
            throw new InvalidDataException("Legacy campaign schema has Travel, package timing or event-idle state.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && (state.Scripts?.Radio is not null ||
            state.Scripts?.Notifications?.Pending.Any(value => value.Event.Kind == FalloutHudEventKind.RadioDiscovered) == true ||
            state.Scripts?.Notifications?.Current?.Event.Kind == FalloutHudEventKind.RadioDiscovered))
            throw new InvalidDataException("Legacy campaign schema has radio discovery state.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.References?.Any(reference => reference.PackageMotion?.DialogueCompleted == true) == true)
            throw new InvalidDataException("Legacy campaign schema has dialogue completion.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.References?.Any(reference => reference.PackageMotion?.EditorTravel is not null) == true)
            throw new InvalidDataException("Legacy campaign schema has editor travel progress.");
        state.Vitals?.Validate();
        if (state.PlayerActorValues is { } playerValues)
        {
            FalloutPlayerActorValues.Validate(playerValues);
            if (state.Special is null || state.Special.Values.Where((value, index) => playerValues.Values[index + 5].Base != (float)value).Any())
                throw new InvalidDataException("Legacy SPECIAL view differs from the saved player BASE pools.");
        }
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.Schema != ReferenceAccessSchema && state.References?.Any(reference =>
            reference.LockState is not null || reference.OwnershipOverride is not null) == true)
            throw new InvalidDataException("Legacy campaign save cannot contain reference lock or ownership overrides.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.ActorOverrides?.Any(actor => actor.Height is not null || actor.Hair is not null) == true)
            throw new InvalidDataException("Legacy campaign save cannot contain stored actor height or race hair state.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.Scripts?.Challenges is { } challenges &&
            (challenges.Entries is { Count: > 0 } || challenges.ChallengesCompleted != 0))
            throw new InvalidDataException("Challenge state requires the current campaign save schema.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.Schema != ReferenceAccessSchema && state.Schema != ReferenceAccessLegacySchema && state.ActorOverrides?.Any(actor => actor.FaceGeometry is not null) == true)
            throw new InvalidDataException("Legacy save cannot contain actor face geometry.");
        if (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema) && state.ActorOverrides?.Any(actor => actor.Race is not null) == true)
            throw new InvalidDataException("Legacy campaign save cannot contain actor race state.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.Schema != ReferenceAccessSchema && state.Schema != ReferenceAccessLegacySchema && state.Schema != RaceOverridesSchema && state.Schema != ScriptValuesSchema && (state.Scripts?.Values?.LastArrayId is > 0 ||
                state.Scripts?.Values?.Arrays is { Count: > 0 }))
            throw new InvalidDataException("Legacy campaign save cannot contain script array state.");
        if (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema) && state.References?.Any(reference => reference.ObjectAnimations is { Count: > 0 }) == true)
            throw new InvalidDataException("Legacy save cannot contain object animation state.");
        if (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema) && (state.References?.Any(reference => reference.KnockedDown || reference.Destruction is not null) == true ||
                state.ExplosionExposure is { Count: > 0 }))
            throw new InvalidDataException("Legacy save cannot contain knockdown, destruction or explosion exposure state.");
        if (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema) && state.References?.Any(reference =>
                reference.HitReaction is not null || reference.HitReactionRandomState is not null) == true)
            throw new InvalidDataException("Legacy save cannot contain hit-reaction state.");
        if (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema) && state.References?.Any(reference =>
                reference.Injury is { DeathEventPending: true } or { DeathEventElapsed: > 0 }) == true)
            throw new InvalidDataException("Legacy save cannot contain pending actor death events.");
        if (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema) && state.References?.Any(reference => reference.PackageMotion?.Patrol is not null) == true)
            throw new InvalidDataException("Patrol progress requires the current campaign save schema.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.Schema != ReferenceAccessSchema && state.References?.Any(reference => reference.PackageMotion?.Escort is not null) == true)
            throw new InvalidDataException("Escort progress requires the current campaign save schema.");
        if (state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && (state.Inventory.Any(item => item.UnequipLocked) ||
                state.References?.Any(reference => reference.Inventory?.Contents?.Inventory?.Items?.Any(item => item.UnequipLocked) == true) == true))
            throw new InvalidDataException("Equipment locks require the current campaign save schema.");
        if (state.Schema is ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema or EncounterZoneSchema && state.EncounterZones is null)
            throw new InvalidDataException("Saved campaign is missing encounter-zone state.");
        if (state.Ingestibles is { } ingestibles) FalloutPlayerIngestibles.Validate(ingestibles);
        if (state.WeaponHandling is { } handling) FalloutWeaponHandling.Validate(handling);
        if ((state.Schema != ExpectedSchema && state.Schema != RadioSchema && state.Schema != DialoguePackageSchema && state.Schema != EditorTravelSchema && state.Schema != SignedRaceSchema && state.Schema != ReferenceAccessSchema && state.Schema != ReferenceAccessLegacySchema && state.Schema != RaceOverridesSchema && state.Schema != ScriptValuesSchema && state.Schema != ObjectAnimationSchema && state.Schema != DestructionSchema && state.Schema != HitReactionSchema && state.Schema != DeathEventSchema && state.Schema != PatrolSchema && state.Schema != EncounterZoneSchema && state.Schema != ActorOverridesSchema && state.Schema != PackageMotionSchema && state.Schema != IngestiblesSchema && state.Schema != ViewPitchSchema && state.Schema != ReferenceStateSchema && state.Schema != QuestClockSchema && state.Schema != SkyLightingSchema && state.Schema != GlobalClockSchema && state.Schema != QuestScriptsSchema && state.Schema != FeetAnchoredSchema && state.Schema != CapsuleCenteredSchema) ||
            (state.Scripts is null) != (state.Quests is null) ||
            (state.Globals is null) != (state.GameTime is null) ||
            (state.Schema is ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema or EncounterZoneSchema or ActorOverridesSchema or PackageMotionSchema or IngestiblesSchema or ViewPitchSchema or ReferenceStateSchema or QuestClockSchema or SkyLightingSchema or GlobalClockSchema && state.Globals is null) ||
            (state.Schema is ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema or EncounterZoneSchema or ActorOverridesSchema or PackageMotionSchema or IngestiblesSchema or ViewPitchSchema or ReferenceStateSchema or QuestClockSchema or SkyLightingSchema && state.SkyLighting is null) ||
            (state.Schema is ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema or EncounterZoneSchema or ActorOverridesSchema or PackageMotionSchema or IngestiblesSchema or ViewPitchSchema or ReferenceStateSchema && state.References is null) ||
            (state.Schema is ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema or EncounterZoneSchema or ActorOverridesSchema or PackageMotionSchema or IngestiblesSchema or ViewPitchSchema && state.PlayerViewPitchRadians is null) ||
            (state.Ingestibles is not null && (state.Schema is not (ExpectedSchema or RadioSchema or DialoguePackageSchema or EditorTravelSchema or SignedRaceSchema or ReferenceAccessSchema or ReferenceAccessLegacySchema or RaceOverridesSchema or ScriptValuesSchema or ObjectAnimationSchema or DestructionSchema or HitReactionSchema or DeathEventSchema or PatrolSchema or EncounterZoneSchema or ActorOverridesSchema or PackageMotionSchema or IngestiblesSchema) || state.Vitals is null)) ||
            (state.PlayerViewPitchRadians is { } pitch && (!float.IsFinite(pitch) || MathF.Abs(pitch) > MathF.PI / 2)) ||
            (state.SkyLighting is not null && state.Globals is null) ||
            (state.GameTime is { } time && (!float.IsFinite(time.PreviousHour) || string.IsNullOrWhiteSpace(time.CalendarSha256))) ||
            string.IsNullOrWhiteSpace(expectedSaveCompatibilityId) ||
            state.SaveCompatibilityId != expectedSaveCompatibilityId ||
            string.IsNullOrWhiteSpace(state.ActiveCell.OwnerPlugin) ||
            state.ActiveCell.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(state.QuestEditorId) || state.Stage < 0 ||
            (!state.CharacterCreationComplete && (state.Quests is null || state.Scripts is null || state.References is null)) ||
            string.IsNullOrWhiteSpace(state.PlayerName) ||
            state.PlayerName != state.PlayerName.Trim() ||
            state.PlayerName.Any(char.IsControl) ||
            state.Character is null ||
            state.Character.RaceRuntimeFormId == 0 ||
            state.Character.HairRuntimeFormId == 0 ||
            state.Character.EyesRuntimeFormId == 0 ||
            string.IsNullOrWhiteSpace(state.Character.RaceEditorId) ||
            string.IsNullOrWhiteSpace(state.Character.HairEditorId) ||
            string.IsNullOrWhiteSpace(state.Character.EyesEditorId) ||
            state.Special is null ||
            state.Special.Values.Count != FalloutNativeVigorResolver.AttributeNames.Count ||
            state.TagSkills is null ||
            state.Traits is null ||
            state.Inventory.Any(value =>
                value.RuntimeFormId == 0 || string.IsNullOrWhiteSpace(value.EditorId) ||
                value.RecordType.Length != FalloutPlugin.SignatureSize || value.Count <= 0) ||
            state.Inventory.Any(value => value.UnequipLocked &&
                (value.RecordType is not ("ARMO" or "WEAP") || !state.EquippedRuntimeFormIds.Contains(value.RuntimeFormId))) ||
            state.Inventory.Select(value => value.RuntimeFormId).Distinct().Count() !=
                state.Inventory.Count ||
            state.EquippedRuntimeFormIds.Distinct().Count() != state.EquippedRuntimeFormIds.Count ||
            state.EquippedRuntimeFormIds.Any(value =>
                !state.Inventory.Any(item => item.RuntimeFormId == value)) ||
            state.PlayerControls.Count != PlayerControlCount ||
            state.PlayerPosition.Count != PositionComponents ||
            state.PlayerRotation.Count != RotationComponents ||
            state.PlayerPosition.Any(value => !float.IsFinite(value)) ||
            state.PlayerRotation.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Native campaign save state is invalid.");
        var rotationLengthSquared = state.PlayerRotation.Sum(value => value * value);
        if (rotationLengthSquared is < MinimumUnitQuaternionLengthSquared or
            > MaximumUnitQuaternionLengthSquared)
            throw new InvalidDataException("Native campaign save rotation is not normalized.");
        state.Scripts?.Validate();
        if (state.References is not null) FalloutReferenceSnapshot.Validate(state.References);
    }
}
