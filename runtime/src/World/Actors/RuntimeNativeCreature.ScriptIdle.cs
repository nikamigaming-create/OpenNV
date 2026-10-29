using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature : IGamebryoReferenceEnableLifecycle
{
    private RuntimeNativeNifAnimation? _scriptIdleAnimation;
    private FalloutIdleAnimationPlayback? _scriptIdleClock;
    private FalloutIdleTransition? _scriptIdleTransition;
    private IReadOnlyList<FalloutNifAnimationSample>? _scriptIdleSnapshot;
    private IReadOnlyList<FalloutNifSnapshotScalar>? _scriptIdleSnapshotScalars;
    private long _scriptIdleRevision;
    private bool _scriptIdlePosePublished;
    private bool _scriptIdleSourceCancelled;
    private Func<bool>? _scriptIdleReady;
    private Func<FalloutActorStoppedIdleAnimation?>? _scriptIdleCapture;
    private bool ScriptIdleActive => _scriptIdleClock is not null && _scriptIdleRequestState == 2 && _scriptIdleSequenceState != 0;

    internal void PlayIdle(FalloutPluginStack stack, string editorId)
    {
        if (!ReferenceEquals(stack, _aiRecords) || _aiState is not { } state || !IsInsideTree())
            throw new InvalidOperationException("Creature script idle has no live source binding.");
        var combat = Combat ?? throw new InvalidOperationException("Creature script idle has no physical pose owner.");
        if (Error is not null || state.ScriptError is not null || _scriptIdleSourceCancelled || ScriptIdleActive ||
            _eventIdleClock is not null || _conversationTarget is not null ||
            combat is not { Dead: false, OwnsPose: false, PackageMoving: false, AnimationWeapon: null } ||
            state.Unconscious || state.Restrained || state.KnockedDown || !_aiWorld!.IsEnabled(state.Reference))
            throw new NotSupportedException("Creature script idle cannot compose its current pose or failed source owner.");
        var record = FalloutDialogueTopic.Find(stack, "IDLE", editorId);
        var idle = FalloutActorIdleSource.Resolve(stack, record);
        var source = ReadIdle(idle);
        var timing = FalloutIdleAnimationData.Read(record);
        var transition = PrepareScriptIdleTransition(source.Animation, timing);
        var snapshotPose = CaptureScriptIdlePose();
        var snapshotSamples = ReadScriptIdleSnapshot(snapshotPose);
        var snapshotScalars = source.Animation.CaptureSnapshotScalars();
        var revision = checked(_scriptIdleRevision + 1);
        if (revision == long.MaxValue) throw new InvalidOperationException("Creature script idle revision overflowed.");
        var sequence = source.Animation.Sequence;
        var clock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency,
            sequence.CycleType, source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(),
            timing.SelectAdditionalLoops(state.SoundRandom.NextBounded));
        RequireScriptIdleEnd(source.Animation, clock);
        combat.StopPackageMotion();
        source.Animation.ApplySnapshotTransition(clock.SourceSeconds, snapshotSamples, 1, 0, scalars: snapshotScalars);
        var saved = new FalloutActorStoppedIdleAnimation("script", new(record.FormKey, Hash(record.ReadData()),
            idle.AnimationPath, source.Hash, clock.Capture(), revision, CaptureScriptIdlePose(), []),
            new(transition.Capture(), ScriptIdleTransitionSettings(), snapshotPose, Scalars: snapshotScalars), new(2, 5));
        saved.Validate(stack);
        _scriptIdleAnimation = source.Animation; _scriptIdleClock = clock;
        _scriptIdleTransition = transition; _scriptIdleSnapshot = snapshotSamples;
        _scriptIdleSnapshotScalars = snapshotScalars;
        _scriptIdleRequestState = 2; _scriptIdleSequenceState = 5;
        _scriptIdleDeactivation = null; _scriptIdleEndPending = false;
        _scriptIdleRevision = revision; _scriptIdlePosePublished = true;
        state.CreatureScriptIdle = saved;
    }

    private void BindScriptIdleCapture()
    {
        _aiState!.CanCaptureCreatureScriptIdle = _scriptIdleReady = CanCaptureScriptIdle;
        _aiState.CaptureCreatureScriptIdle = _scriptIdleCapture = CaptureScriptIdle;
    }

    private bool CanCaptureScriptIdle() => !ScriptIdleActive || IsInsideTree() && !_sourceBindingsRetired &&
        _scriptIdlePosePublished && _aiState is { CreatureScriptIdle.Owner: "script", ScriptError: null, Deleted: false } state &&
        _aiWorld!.IsEnabled(state.Reference) &&
        Error is null && _eventIdleClock is null && _conversationTarget is null &&
        Combat is { Dead: false, OwnsPose: false, PackageMoving: false, PackageOwnsPose: false, AnimationWeapon: null, StoppedAiPoseCaptureReady: true } &&
        (_scriptIdleClock is { Complete: false } || _scriptIdleDeactivation is { Complete: false }) && _scriptIdleAnimation is not null &&
        _scriptIdleTransition is not null && _scriptIdleSnapshot is not null && _scriptIdleSnapshotScalars is not null &&
        state.CreatureScriptIdle.Transition is not null &&
        OpenNV.Runtime.SceneGraph.NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this)
            .All(sound => sound.CanCaptureSilent);

    private FalloutActorStoppedIdleAnimation? CaptureScriptIdle()
    {
        if (!CanCaptureScriptIdle()) throw new NotSupportedException("Creature script idle retains an independent continuation owner.");
        if (!ScriptIdleActive) return null;
        var saved = _aiState!.CreatureScriptIdle ?? throw new InvalidDataException("Creature script idle lost its source receipt.");
        var result = saved with
        {
            Animation = saved.Animation with
            { Clock = _scriptIdleClock!.Capture(), ResidualPose = CaptureScriptIdlePose() },
            Transition = saved.Transition! with { Phase = _scriptIdleTransition!.Capture() },
            ScriptPhase = CaptureScriptIdlePhase()
        };
        result.Validate(_aiRecords!);
        return result.Copy();
    }

    private FalloutActorResidualPose CaptureScriptIdlePose()
    {
        var bones = new List<FalloutActorResidualBonePose>();
        for (var index = 0; index < Skeleton.Node.GetBoneCount(); ++index)
        {
            var p = Skeleton.Node.GetBonePosePosition(index); var q = Skeleton.Node.GetBonePoseRotation(index);
            var s = Skeleton.Node.GetBonePoseScale(index);
            bones.Add(new(index, Skeleton.Node.GetBoneName(index).ToString(),
                [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z]));
        }
        return new(Appearance.SkeletonPath, Skeleton.Source.Sha256, bones);
    }

    private FalloutIdleTransition.Settings ScriptIdleTransitionSettings()
    {
        var settings = FalloutInstallationSettings.Read(_content);
        return new(settings.Number("General", "fAnimationDefaultBlend"), settings.Number("General", "fAnimationMult"));
    }

    private FalloutIdleTransition PrepareScriptIdleTransition(RuntimeNativeNifAnimation animation,
        FalloutIdleAnimationData timing, FalloutActorIdleSnapshotTransition? restore = null)
    {
        var keys = GroupKeys(animation);
        if (!keys.Group.Equals("SpecialIdle", StringComparison.OrdinalIgnoreCase) || animation.Sequence.StartTime != 0)
            throw new NotSupportedException("Creature snapshot transition requires its admitted zero-start SpecialIdle source group.");
        animation.RequireSnapshotTransitionOver(_animation);
        var slot = FalloutIdleTransition.ExecutionSlot.FromRecord(timing.Group);
        // The only other admitted native animation owner here is the base
        // slot. Active script/package-event owners are excluded by the caller.
        var admission = new FalloutIdleTransition.Admission(timing.Group, slot.Index == 0,
            keys.HasSynchronization, restore is null && GetTree().Paused, false, false);
        var settings = ScriptIdleTransitionSettings();
        if (restore is null) return FalloutIdleTransition.CreateSnapshot(admission, keys.Blend, null, settings);
        restore.Validate();
        if (restore.Settings != settings)
            throw new InvalidDataException("Saved script transition differs from its retained animation settings.");
        return FalloutIdleTransition.Restore(restore.Phase, admission, keys.Blend, null, settings);
    }

    private void ValidateScriptIdleSnapshotPose(FalloutActorResidualPose pose) =>
        pose.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            Enumerable.Range(0, Skeleton.Node.GetBoneCount()).Select(index =>
                (Skeleton.Node.GetBoneName(index).ToString(), FalloutNifTransformComponents.None)).ToArray());

    private IReadOnlyList<FalloutNifAnimationSample> ReadScriptIdleSnapshot(FalloutActorResidualPose pose)
    {
        ValidateScriptIdleSnapshotPose(pose);
        var result = new FalloutNifAnimationSample[pose.Bones.Count];
        foreach (var bone in pose.Bones)
        {
            var p = bone.Position!; var q = bone.Rotation!; var s = bone.Scale!;
            if (s[0] != s[1] || s[0] != s[2])
                throw new NotSupportedException("Snapshot transition requires source-representable uniform local scale.");
            var units = Skeleton.UnitsToMetres;
            result[bone.Index] = new(new(p[0] / units, -p[2] / units, p[1] / units), new(q[3], q[0], -q[2], q[1]), s[0]);
        }
        return Array.AsReadOnly(result);
    }

    private void RestoreScriptIdle()
    {
        if (_aiState?.CreatureScriptIdle is not { } saved) return;
        saved.Validate(_aiRecords!);
        if (saved.Owner != "script" || ScriptIdleActive || _eventIdleClock is not null || _conversationTarget is not null)
            throw new InvalidDataException("Creature script idle has a conflicting restoration owner.");
        var idle = FalloutActorIdleSource.Resolve(_aiRecords!, _aiRecords!.GetEffective(saved.Animation.Idle));
        var source = ReadIdle(idle);
        if (!source.Hash.Equals(saved.Animation.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Creature script idle differs from its winning KF.");
        ValidateScriptIdlePose(saved);
        var snapshot = saved.Transition ?? throw new NotSupportedException("Saved creature idle has unknown transition history.");
        ValidateScriptIdleSnapshotPose(snapshot.Pose);
        var restoredTransition = PrepareScriptIdleTransition(source.Animation,
            FalloutIdleAnimationData.Read(_aiRecords!.GetEffective(saved.Animation.Idle)), snapshot);
        var scalars = (snapshot.Scalars ?? []).ToArray();
        source.Animation.ValidateSnapshotScalars(scalars);
        var sequence = source.Animation.Sequence;
        var clock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency,
            sequence.CycleType, source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(),
            saved.Animation.Clock.SelectedAdditionalLoops);
        clock.Restore(saved.Animation.Clock);
        RequireScriptIdleEnd(source.Animation, clock);
        _scriptIdleAnimation = source.Animation; _scriptIdleClock = clock;
        _scriptIdleTransition = restoredTransition; _scriptIdleSnapshot = ReadScriptIdleSnapshot(snapshot.Pose);
        _scriptIdleSnapshotScalars = scalars;
        _scriptIdleRevision = saved.Animation.Revision;
        RestoreScriptIdlePhase(saved);
        Combat?.StopPackageMotion();
        if (IsNodeReady()) PublishScriptIdlePose();
    }

    private void ValidateScriptIdlePose(FalloutActorStoppedIdleAnimation saved) =>
        saved.Animation.ResidualPose!.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            Enumerable.Range(0, Skeleton.Node.GetBoneCount()).Select(index =>
                (Skeleton.Node.GetBoneName(index).ToString(), OpenNV.Runtime.Formats.Gamebryo.FalloutNifTransformComponents.None)).ToArray());

    private void PublishScriptIdlePose()
    {
        if (!ScriptIdleActive || _scriptIdlePosePublished || _aiState?.CreatureScriptIdle is not { } saved) return;
        ValidateScriptIdlePose(saved);
        PublishCurrentScriptIdlePose();
        foreach (var bone in saved.Animation.ResidualPose!.Bones)
        {
            var p = bone.Position!; var q = bone.Rotation!; var s = bone.Scale!;
            Skeleton.Node.SetBonePosePosition(bone.Index, new(p[0], p[1], p[2]));
            Skeleton.Node.SetBonePoseRotation(bone.Index, new(q[0], q[1], q[2], q[3]));
            Skeleton.Node.SetBonePoseScale(bone.Index, new(s[0], s[1], s[2]));
        }
        _scriptIdlePosePublished = true;
    }

    private bool AdvanceScriptIdle(double delta)
    {
        if (!ScriptIdleActive || _scriptIdleAnimation is not { } animation || _scriptIdleClock is not { } clock) return false;
        if (CancelDisabledScriptIdle()) return true;
        var state = _aiState ?? throw new InvalidOperationException("Creature script idle has no reference owner.");
        var saved = state.CreatureScriptIdle ?? throw new InvalidDataException("Creature script idle lost its phase owner.");
        try
        {
            var transition = _scriptIdleTransition ?? throw new InvalidDataException("Script idle has no transition phase owner.");
            AdvanceBaseSource(delta);
            if (_scriptIdleDeactivation is { } deactivation)
            {
                deactivation.Advance(delta);
                if (deactivation.Complete) { CompleteScriptIdle(); return true; }
                clock.Advance(delta, interval => DispatchIdleInterval(animation, interval, saved.Animation.Idle));
            }
            else
            {
                var held = transition.Advance(delta);
                if (held) clock.DispatchHeldBoundary(interval => DispatchIdleInterval(animation, interval, saved.Animation.Idle));
                else
                {
                    _scriptIdleSequenceState = 1;
                    clock.Advance(delta, interval => DispatchIdleInterval(animation, interval, saved.Animation.Idle));
                }
                if (_scriptIdleEndPending)
                {
                    if (held) throw new NotSupportedException("Creature idle End during a held snapshot requires its transition interruption owner.");
                    BeginScriptIdleDeactivation();
                    if (!ScriptIdleActive) return true;
                }
                if (clock.Complete && _scriptIdleDeactivation is null)
                    throw new NotSupportedException("Creature idle reached its KF endpoint without an authored final End event.");
            }
            PublishCurrentScriptIdlePose();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            state.ScriptError ??= $"Script idle {saved.Animation.Idle}: {error.Message}";
            throw;
        }
        finally
        {
            if (ScriptIdleActive) state.CreatureScriptIdle = saved with
            {
                Animation = saved.Animation with { Clock = clock.Capture() },
                Transition = saved.Transition! with { Phase = _scriptIdleTransition!.Capture() },
                ScriptPhase = CaptureScriptIdlePhase()
            };
        }
        return true;
    }

    void IGamebryoReferenceEnableLifecycle.BeforeReferenceDisabled()
    {
        try
        {
            // Apply(false) is also used for source staging. Only the existing
            // authoritative Disabled/Deleted gate may cancel a source phase.
            CancelDisabledScriptIdle();
        }
        catch (Exception error)
        {
            if (_aiState is { } state)
                state.ScriptError ??= $"Script idle disable cancellation: {error.Message}";
            throw;
        }
    }

    private bool CancelDisabledScriptIdle()
    {
        if (!ScriptIdleActive || _aiState is not { } state || _aiWorld!.IsEnabled(state.Reference) && !state.Deleted) return false;
        var saved = state.CreatureScriptIdle ?? throw new InvalidDataException("Disabled creature script idle lost its receipt.");
        // This is an authoritative disable/delete, not an animation fallback.
        // Source sound generations retain their normal cancellation history.
        _sounds.RetirePlayback();
        GD.Print($"OPENNV_CREATURE_SCRIPT_IDLE_CANCEL reference={state.Reference} idle={saved.Animation.Idle} " +
            $"revision={saved.Animation.Revision} sourceSeconds={_scriptIdleClock!.SourceSeconds:R} owner=reference-disabled-or-deleted");
        _scriptIdleSourceCancelled = true;
        ClearScriptIdleOwner();
        return true;
    }

    private void RetireScriptIdleCapture()
    {
        if (_aiState is not { } state || !ReferenceEquals(state.CaptureCreatureScriptIdle, _scriptIdleCapture)) return;
        var retained = false;
        try
        {
            CancelDisabledScriptIdle();
            state.CreatureScriptIdle = CaptureScriptIdle();
            retained = true;
        }
        finally
        {
            state.CaptureCreatureScriptIdle = null;
            if (ReferenceEquals(state.CanCaptureCreatureScriptIdle, _scriptIdleReady))
                state.CanCaptureCreatureScriptIdle = retained ? null : static () => false;
        }
    }
}
