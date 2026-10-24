using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private RuntimeNativeNifAnimation? _scriptIdleAnimation;
    private FalloutIdleAnimationPlayback? _scriptIdleClock;
    private long _scriptIdleRevision;
    private bool _scriptIdlePosePublished;
    private bool _scriptIdleSourceCancelled;
    private Func<bool>? _scriptIdleReady;
    private Func<FalloutActorStoppedIdleAnimation?>? _scriptIdleCapture;
    private bool ScriptIdleActive => _scriptIdleClock is not null;

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
        var revision = checked(_scriptIdleRevision + 1);
        if (revision == long.MaxValue) throw new InvalidOperationException("Creature script idle revision overflowed.");
        var sequence = source.Animation.Sequence;
        var clock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency,
            sequence.CycleType, source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(),
            timing.SelectAdditionalLoops(state.SoundRandom.NextBounded));
        combat.StopPackageMotion();
        RuntimeNativeNifAnimation.ApplyLayers((_animation, SourceSeconds), (source.Animation, clock.SourceSeconds));
        var saved = new FalloutActorStoppedIdleAnimation("script", new(record.FormKey, Hash(record.ReadData()),
            idle.AnimationPath, source.Hash, clock.Capture(), revision, CaptureScriptIdlePose(), []));
        saved.Validate(stack);
        _scriptIdleAnimation = source.Animation; _scriptIdleClock = clock;
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
        _scriptIdleClock is { Complete: false } && _scriptIdleAnimation is not null &&
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
            { Clock = _scriptIdleClock!.Capture(), ResidualPose = CaptureScriptIdlePose() }
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
        var sequence = source.Animation.Sequence;
        var clock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency,
            sequence.CycleType, source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(),
            saved.Animation.Clock.SelectedAdditionalLoops);
        clock.Restore(saved.Animation.Clock);
        _scriptIdleAnimation = source.Animation; _scriptIdleClock = clock;
        _scriptIdleRevision = saved.Animation.Revision;
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
        RuntimeNativeNifAnimation.ApplyLayers((_animation, SourceSeconds), (_scriptIdleAnimation!, _scriptIdleClock!.SourceSeconds));
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
        if (_scriptIdleAnimation is not { } animation || _scriptIdleClock is not { } clock) return false;
        if (CancelDisabledScriptIdle()) return true;
        var state = _aiState ?? throw new InvalidOperationException("Creature script idle has no reference owner.");
        var saved = state.CreatureScriptIdle ?? throw new InvalidDataException("Creature script idle lost its phase owner.");
        var advanced = false;
        try
        {
            clock.Advance(delta, interval => DispatchIdleInterval(animation, interval, saved.Animation.Idle));
            RuntimeNativeNifAnimation.ApplyLayers((_animation, SourceSeconds), (animation, clock.SourceSeconds));
            advanced = true;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            state.ScriptError ??= $"Script idle {saved.Animation.Idle}: {error.Message}";
            throw;
        }
        finally
        {
            if (clock.Complete && advanced)
            {
                _scriptIdleAnimation = null; _scriptIdleClock = null; state.CreatureScriptIdle = null;
            }
            else if (!clock.Complete) state.CreatureScriptIdle = saved with { Animation = saved.Animation with { Clock = clock.Capture() } };
        }
        return true;
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
        _scriptIdleAnimation = null; _scriptIdleClock = null; state.CreatureScriptIdle = null;
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
