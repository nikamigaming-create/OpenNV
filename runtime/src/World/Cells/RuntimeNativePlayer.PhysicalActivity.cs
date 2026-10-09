using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPlayerPhysicalActivity? _playerPhysical;
    private FalloutPlayerPhysicalSnapshot? _restorePlayerPhysical;
    private FalloutPluginStack? _physicalRecords;
    private FalloutQuestState? _physicalQuests;
    private FalloutReferenceWorld? _physicalWorld;
    private Func<GameplayVitals>? _physicalVitals;
    private Func<string, double>? _physicalActorValue;
    private Func<FalloutCondition, float>? _physicalCondition;
    private FalloutAnimationSoundEvents? _physicalSoundEvents;
    private NativeOwnedAnimationSoundPlayer? _physicalSounds;
    private Action? _physicalInput;
    private Func<FalloutFormKey, Transform3D>? _physicalFurniturePlacement;
    internal event Action<FalloutFormKey>? SourceBedOccupied;

    internal void ConfigurePlayerPhysicalActivity(FalloutPluginStack records, FalloutQuestState quests,
        FalloutReferenceWorld world, FalloutAdvancementRuntimeReceipt runtime,
        Func<GameplayVitals> vitals, Func<string, double> actorValue, Func<FalloutCondition, float> evaluate,
        FalloutPlayerPhysicalSnapshot? restore = null,
        Func<FalloutFormKey, Transform3D>? liveFurniturePlacement = null)
    {
        if (_playerPhysical is not null) throw new InvalidOperationException("Player physical activity is already configured.");
        ArgumentNullException.ThrowIfNull(vitals); ArgumentNullException.ThrowIfNull(actorValue); ArgumentNullException.ThrowIfNull(evaluate);
        var source = FalloutPlayerPhysicalSource.Read(records, runtime);
        if (restore is not null && restore.Sounds.Reference != records.RuntimeFormKey(0x14))
            throw new InvalidDataException("Saved physical audio belongs to a different actual engine player.");
        _playerPhysical = new(source, restore);
        _physicalRecords = records; _physicalQuests = quests; _physicalWorld = world;
        _physicalVitals = vitals; _physicalActorValue = actorValue; _physicalCondition = evaluate;
        _physicalFurniturePlacement = liveFurniturePlacement;
        _restorePlayerPhysical = restore;
        _physicalSoundEvents = new(records.RuntimeFormKey(0x14), () =>
            _playerPhysical is { Published: true } && IsInsideTree());
        if (restore is not null)
        {
            _furnitureRandom.Restore(restore.RandomState);
            _physicalSoundEvents.Restore(restore.Sounds, records);
        }
        if (_thirdPerson is not null) PublishPhysicalPlayerBody();
    }
    internal FalloutAdvancementActivityObservation ObservePhysicalActivity(FalloutAdvancementActivityFact fact) =>
        _playerPhysical?.Observe(fact) ?? new(FalloutAdvancementActivityState.Unowned, "physical-player:source-owner-absent");
    internal int GetPlayerSleeping() => PhysicalPlayer.SleepingState;
    internal int GetPlayerKnockedState() => PhysicalPlayer.KnockedState;
    internal bool IsPcSleeping() => PhysicalPlayer.IsPcSleeping;
    internal string? PlayerPhysicalFailure => _playerPhysical?.Failure?.Error;
    internal void BeginSourceSleepClock(Action begin) => PhysicalPlayer.BeginSleepClock(begin);
    internal void EndSourceSleepClock(Action end) => PhysicalPlayer.EndSleepClock(end);
    private FalloutPlayerPhysicalActivity PhysicalPlayer => _playerPhysical ??
        throw new NotSupportedException("Player physical activity has no selected source owner.");
    internal string? PlayerPhysicalSaveBlocker
    {
        get
        {
            if (_playerPhysical is not { Published: true }) return "player-physical-publication";
            // The independent original sleep flag needs the actual time/effect
            // consumer's continuation. A bed pose cannot substitute for it.
            if (_playerPhysical.Sleeping) return "player-sleep-clock-continuation";
            if (_restorePlayerPhysical is not null) return "player-physical-cold-publication";
            if (_physicalSounds is null || !_physicalSounds.CanCaptureSilent || _physicalSoundEvents?.CanCapture != true)
                return "player-physical-animation-sound-continuation";
            if (_playerPhysical.KnockdownPhase == FalloutPlayerKnockdownPhase.Simulating && _playerRagdoll?.CaptureReady != true)
                return "player-physical-ragdoll-continuation";
            return null;
        }
    }
    internal object PlayerPhysicalState => new
    {
        source = _playerPhysical?.Source,
        published = _playerPhysical?.Published,
        independentSleepingFlag = _playerPhysical?.Sleeping,
        furniture = _playerPhysical?.FurniturePhase,
        furnitureKind = _playerPhysical?.FurnitureKind,
        knockdown = _playerPhysical?.KnockdownPhase,
        attempt = _playerPhysical?.Attempt,
        failure = _playerPhysical?.Failure,
        saveBlocker = PlayerPhysicalSaveBlocker,
        sounds = _physicalSounds?.State,
        ragdoll = _playerRagdoll?.Observation,
        unowned = "sleep-hour-menu,time-pass-effects,forced-knockout/paralysis,physical-camera-retail-parity",
    };
    private void RequirePhysicalPresentationReplacement()
    {
        if (_playerPhysical is null) return;
        if (_playerPhysical.HasPhysicalMotion || _physicalSounds?.ActiveNativeVoices.Count > 0)
        {
            var error = new NotSupportedException("Player appearance/equipment replacement requires the active physical pose and sound transfer owner.");
            _playerPhysical.Retain("replace-physical-player-body", error); throw error;
        }
        RetirePlayerPerceptionBody();
        _physicalSounds?.Free(); _physicalSounds = null;
        _playerPhysical.RetireNativeOwner();
    }
    private void PublishPhysicalPlayerBody()
    {
        if (_playerPhysical is null || _playerPhysical.Published) return;
        var actor = _thirdPerson?.Actor ?? throw new NotSupportedException("Physical player requires its actual current source body.");
        if (!IsInsideTree() || !actor.IsInsideTree() || _thirdPerson!.Error is not null || _presentationError is not null)
            throw new NotSupportedException("Physical player body did not publish a healthy native source instance.");
        var records = _physicalRecords!;
        var current = FalloutPlayerActorValueSource.Read(records);
        if (_playerPhysical.Source.Player != current.Player || _playerPhysical.Source.PlayerSha256 != current.PlayerSha256 ||
            _playerPhysical.Source.StatsOwner != current.StatsOwner || _playerPhysical.Source.StatsSha256 != current.StatsSha256)
            throw new InvalidDataException("Physical player source changed before native body publication.");
        actor.SetMeta("opennv_reference_form_key", records.RuntimeFormKey(0x14).ToString());
        // The source body/voices follow gameplay pause even when the XR player
        // input/camera adapter continues to process under an Always parent.
        actor.ProcessMode = ProcessModeEnum.Pausable;
        _playerPhysical.PublishNativeOwner();
        try
        {
            var saved = _restorePlayerPhysical;
            if (saved?.Sleeping == true)
                throw new NotSupportedException("Cold active player sleep requires its actual hour/time/effect continuation owner.");
            if (saved?.NativePose is { } pose) RestorePhysicalBones(pose);
            if (saved?.Furniture is { } furniture) RestorePlayerFurniture(furniture);
            if (saved?.Knockdown is { } knockdown) RestorePlayerKnockdown(knockdown);
            _physicalSounds = new(records, RuntimeLiveContentSource.Current ??
                throw new NotSupportedException("Player physical media source is absent."), actor, UnitsToMeters,
                _furnitureRandom, _physicalSoundEvents);
            actor.AddChild(_physicalSounds); _physicalSounds.RequirePcmRestored();
            if (!_physicalSounds.IsInsideTree()) throw new InvalidOperationException("Player physical sound owner did not attach.");
            _restorePlayerPhysical = null;
            PublishPlayerPerception();
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            _playerPhysical.Retain("publish-physical-player-body", error); throw;
        }
    }
    internal FalloutPlayerPhysicalSnapshot CapturePlayerPhysicalActivity()
    {
        if (PlayerPhysicalSaveBlocker is { } blocker) throw new NotSupportedException("Saving player physical state requires " + blocker);
        var state = PhysicalPlayer;
        var result = new FalloutPlayerPhysicalSnapshot(state.Source, state.Attempt, state.Sleeping, _furnitureRandom.State,
            _physicalSoundEvents!.Capture(), CapturePlayerFurniture(), CapturePlayerKnockdown(), state.Failure,
            state.HasPhysicalMotion ? CapturePhysicalBones() : null);
        result.Validate(); return result;
    }
    internal void PublishRequiredPlayerPhysicalBody()
    {
        if (_presentationRecords is null || _presentationInventory is null || !IsInsideTree())
            throw new NotSupportedException("Player physical initialization requires the configured attached source presentation.");
        if (_thirdPerson is null)
        {
            _presentationEquipment = _presentationInventory.Equipped.ToArray();
            _presentationInventoryRevision = _presentationInventory.Revision;
            _presentationAppearanceRevision = _appearanceRevision?.Invoke() ?? 0;
            RebuildPresentation(_presentationEquipment);
        }
        PublishPhysicalPlayerBody(); PhysicalPlayer.RequireHealthy();
    }
    private void DispatchPhysicalKey(FalloutNifTextKeyEvent key)
    {
        foreach (var declaration in FalloutNifTextKeyDeclarations.Read(key.Text))
            if (declaration.Kind == FalloutNifTextKeyDeclarationKind.Unbound)
                throw new NotSupportedException($"Player physical KF event {declaration.Text} has no gameplay/animation consumer.");
        var sound = _physicalSounds ?? throw new NotSupportedException("Player physical animation has no native event owner.");
        var result = sound.Dispatch(key);
        if (result.Contains("unbound", StringComparison.Ordinal) || sound.Unbound.Count != 0)
            throw new NotSupportedException("Player physical source event was refused: " + result);
    }
    private float EvaluatePlayerPhysicalCondition(FalloutCondition condition) => condition.Function switch
    {
        49 => PhysicalPlayer.SleepingState,
        107 => PhysicalPlayer.KnockedState,
        159 => SittingState,
        77 => _furnitureRandom.NextBounded(100),
        289 => _physicalWorld!.PlayerInCombat() ? 1 : 0,
        392 => _thirdPersonMode || _xr is not null ? 0 : 1,
        _ => (_physicalCondition ?? throw new NotSupportedException("Player physical condition has no source owner."))(condition),
    };
    private void RetirePlayerPhysicalActivity()
    {
        try { RetirePlayerPhysicalView(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _playerPhysical?.Retain("retire-player-physical-view", error); throw; }
        finally
        {
            try { _physicalInput?.Invoke(); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            { _playerPhysical?.Retain("retire-player-physical-input", error); throw; }
            finally { _physicalInput = null; _playerPhysical?.RetireNativeOwner(); }
        }
    }
    private static float[] PhysicalPose(Transform3D pose) => [pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z,
        pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z, pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z,
        pose.Origin.X, pose.Origin.Y, pose.Origin.Z];
    private static Transform3D PhysicalPose(float[] pose)
    {
        FalloutActorFurnitureContinuation.ValidatePose(pose);
        return new(new Vector3(pose[0], pose[1], pose[2]), new Vector3(pose[3], pose[4], pose[5]),
            new Vector3(pose[6], pose[7], pose[8]), new Vector3(pose[9], pose[10], pose[11]));
    }
}
