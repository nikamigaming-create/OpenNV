using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void Validate(FalloutNativeCampaignState state, string expectedSaveCompatibilityId)
    {
        if (state.Schema != ExpectedSchema)
            throw new NotSupportedException("Campaign state requires the current complete source-owned schema.");
        if (state.Inventory is null || state.EquippedRuntimeFormIds is null || state.PlayerControls is null ||
            state.PlayerPosition is null || state.PlayerRotation is null || state.Character is null ||
            state.Special is null || state.TagSkills is null || state.Traits is null || state.Quests is null ||
            state.Scripts is null || state.Globals is null || state.GameTime is null || state.SkyLighting is null ||
            state.References is null || state.PlayerViewPitchRadians is null || state.PlayerActorValues is null ||
            state.TagSkillSlots is null || state.EncounterZones is null || state.FactionRelations is null ||
            state.QuestStageResults is null || state.TerminalResults is null || state.PlayerPackageAudio is null ||
            state.Vitals is null || state.WeaponHandling is null || state.Ingestibles is null ||
            state.ActorOverrides is null || state.ExplosionExposure is null || state.DetectionEvents is null ||
            state.InventoryRandomState is null || state.PlayerProgress is null || state.SaveOrder is null ||
            state.ExperienceNotifications is null || state.InterfaceActivationFrames is null || state.PlayerPhysical is null ||
            state.CombatGroups is null || state.ActorPerception is null || state.ActorProcesses is null ||
            state.SleepWait is null || state.RestAutoSave is null || state.RestWorldTime is null ||
            state.RestInterfaceSounds is null || state.InterfaceFades is null ||
            state.ActorUpdates is null || state.CellProcesses is null || state.ActorProcessRuntime is null || state.ActorProcessCommon is null ||
            state.PlayerSkillValues is null || state.PlayerAbilityScripts is null || state.PlayerStatistics is null || state.ProcessQueues is null ||
            state.Scripts.Challenges is null || state.IndexedInterfaceSounds is null)
            throw new InvalidDataException("Campaign state is missing an authoritative runtime owner.");
        if (state.IndexedInterfaceSounds.Schema != FalloutIndexedInterfaceSounds.Schema ||
            state.IndexedInterfaceSounds.Voices is null ||
            state.IndexedInterfaceSounds.LastOrdinal != state.IndexedInterfaceSounds.Voices.Count ||
            state.IndexedInterfaceSounds.Voices.Any(voice => voice is null || voice.Pending))
            throw new InvalidDataException("Current indexed audio has no settled source/native continuation.");
        ValidateResultAuthorityVersion(state);
        state.PlayerPackageAudio.Events.Validate();
        if (state.Scripts.Session?.PlayerPackage?.SoundRandomState is { } packageRandom &&
            state.PlayerPackageAudio.RandomState != packageRandom)
            throw new InvalidDataException("Player package and audio random state disagree.");
        state.Scripts.Session?.PlayerPackage?.Validate();
        FalloutTerminalMenu.ValidateClosedShape(state.TerminalResults);
        if (state.TerminalResults.Any(menu => state.References.Count(reference => reference.Reference == menu.Reference) != 1))
            throw new InvalidDataException("Saved terminal has no unique retained placed reference state.");
        FalloutQuestStages.ValidateSnapshotShape(state.QuestStageResults);
        FalloutQuestStages.ValidateDriverFailure(state.QuestStageResults, state.StageResultFailure);
        if (state.FinishedSpeechStage is { } stageScope && (stageScope.Stage < 0 || state.FinishedSpeech?.Failure is null))
            throw new InvalidDataException("Saved finished speech stage scope has no ended failure receipt.");
        if (state.References.Any(reference => reference.ScriptStoppedFrame?.PreparedDetection is not null ||
            reference.CompletedScriptContinuation?.PreparedDetection is not null) && state.DetectionEvents is null)
            throw new InvalidDataException("Stopped detection invocation is missing its original simulation clock.");
        if (state.References.Any(reference => reference.Injury?.DeathInventoryGranted == true && reference.DeathCount is not > 0))
            throw new InvalidDataException("Saved killed actor has no cumulative death history.");
        FalloutPlayerTagSkills.Validate(state.TagSkillSlots, state.TagSkills);
        FalloutPlayerActorValues.Validate(state.PlayerActorValues);
        ValidatePlayerProgress(state.PlayerProgress, state.Vitals, state.PlayerActorValues);
        ValidateCurrentPlayerAbilityState(state);
        state.PlayerStatistics.Validate();
        ValidateExperienceNotifications(state.ExperienceNotifications, state.Vitals);
        ValidateAdvancementFrameContinuation(state);
        state.PlayerPhysical.Validate();
        ValidateCurrentRestContinuation(state);
        ValidateCombatGroupContinuation(state);
        ValidateActorPerceptionContinuation(state);
        ValidateActorProcessContinuation(state);
        ValidateActualProcessRuntimeContinuation(state);
        ValidateProcessQueueContinuation(state);
        if (state.Special.Values.Count != FalloutNativeVigorResolver.AttributeNames.Count ||
            state.Special.Values.Where((value, index) => state.PlayerActorValues.Values[index + 5].Base != (float)value).Any())
            throw new InvalidDataException("Saved SPECIAL view differs from the player BASE pools.");
        state.Vitals.Validate();
        FalloutPlayerIngestibles.Validate(state.Ingestibles);
        FalloutWeaponHandling.Validate(state.WeaponHandling);
        if (!float.IsFinite(state.PlayerViewPitchRadians.Value) || MathF.Abs(state.PlayerViewPitchRadians.Value) > MathF.PI / 2 ||
            !float.IsFinite(state.GameTime.PreviousHour) || string.IsNullOrWhiteSpace(state.GameTime.CalendarSha256) ||
            string.IsNullOrWhiteSpace(expectedSaveCompatibilityId) || state.SaveCompatibilityId != expectedSaveCompatibilityId ||
            string.IsNullOrWhiteSpace(state.ActiveCell.OwnerPlugin) || state.ActiveCell.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(state.QuestEditorId) || state.Stage < 0 ||
            string.IsNullOrWhiteSpace(state.PlayerName) || state.PlayerName != state.PlayerName.Trim() || state.PlayerName.Any(char.IsControl) ||
            state.Character.RaceRuntimeFormId == 0 || state.Character.HairRuntimeFormId == 0 || state.Character.EyesRuntimeFormId == 0 ||
            string.IsNullOrWhiteSpace(state.Character.RaceEditorId) || string.IsNullOrWhiteSpace(state.Character.HairEditorId) ||
            string.IsNullOrWhiteSpace(state.Character.EyesEditorId) ||
            state.Special.Values.Count != FalloutNativeVigorResolver.AttributeNames.Count ||
            state.Inventory.Any(value => value is null || value.RuntimeFormId == 0 || string.IsNullOrWhiteSpace(value.EditorId) ||
                value.RecordType is null || value.RecordType.Length != FalloutPlugin.SignatureSize || value.Count <= 0) ||
            state.Inventory.Any(value => value.UnequipLocked &&
                (value.RecordType is not ("ARMO" or "WEAP") || !state.EquippedRuntimeFormIds.Contains(value.RuntimeFormId))) ||
            state.Inventory.Select(value => value.RuntimeFormId).Distinct().Count() != state.Inventory.Count ||
            state.EquippedRuntimeFormIds.Distinct().Count() != state.EquippedRuntimeFormIds.Count ||
            state.EquippedRuntimeFormIds.Any(value => !state.Inventory.Any(item => item.RuntimeFormId == value)) ||
            state.PlayerControls.Count != PlayerControlCount || state.PlayerPosition.Count != PositionComponents ||
            state.PlayerRotation.Count != RotationComponents || state.PlayerPosition.Any(value => !float.IsFinite(value)) ||
            state.PlayerRotation.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Native campaign save state is invalid.");
        var rotationLengthSquared = state.PlayerRotation.Sum(value => value * value);
        if (rotationLengthSquared is < MinimumUnitQuaternionLengthSquared or > MaximumUnitQuaternionLengthSquared)
            throw new InvalidDataException("Native campaign save rotation is not normalized.");
        state.Scripts.Validate();
        FalloutReferenceSnapshot.Validate(state.References);
    }
}
