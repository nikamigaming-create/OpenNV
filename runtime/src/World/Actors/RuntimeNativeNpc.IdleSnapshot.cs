using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutIdleTransition? _idleSnapshotTransition;
    private RuntimeNativeNifAnimation? _idleSnapshotAnimation;
    private FalloutActorResidualPose? _idleSnapshotPose;
    private IReadOnlyList<FalloutNifAnimationSample>? _idleSnapshotSamples;
    private IReadOnlyList<RuntimeNifSnapshotTransform>? _idleSnapshotTargets;
    private IReadOnlyList<FalloutNifSnapshotScalar>? _idleSnapshotScalars;
    private FalloutIdleTransition.Settings _idleSnapshotSettings;

    private bool IdleSnapshotActive => _idleSnapshotTransition is not null;

    private bool BeginIdleSnapshot(RuntimeNativeNifAnimation incoming, FalloutIdleAnimationData timing)
    {
        if (IdleSnapshotActive) throw new InvalidOperationException("An idle snapshot transition already owns the selected slot.");
        var keys = FalloutIdleGroupTextKeys.Read(incoming.Sequence.Name, incoming.TextKeys);
        var settings = IdleTransitionSettings();
        if (!float.IsFinite(settings.DefaultBlendSeconds) || !float.IsFinite(settings.AnimationMultiplier) || settings.AnimationMultiplier <= 0)
            throw new NotSupportedException("Idle snapshot transition requires finite animation settings and a positive multiplier.");
        var count = Math.Max(keys.Blend.Blend, keys.Blend.BlendIn);
        var duration = (count == 0 ? settings.DefaultBlendSeconds : (float)(count / 30.0)) / settings.AnimationMultiplier;
        if (!float.IsFinite(duration)) throw new InvalidDataException("Idle snapshot transition duration is invalid.");
        if (duration <= 0.009999999776482582) return false;
        var transition = PrepareIdleSnapshot(incoming, timing, keys, settings);
        var pose = CaptureCompleteIdlePose();
        var samples = ReadIdleSnapshotPose(pose, incoming);
        var targets = incoming.CaptureSnapshotTransforms();
        var scalars = incoming.CaptureSnapshotScalars();
        incoming.ValidateSnapshotScalars(scalars);
        _idleSnapshotAnimation = incoming;
        _idleSnapshotPose = pose;
        _idleSnapshotSamples = samples;
        _idleSnapshotTargets = targets;
        _idleSnapshotScalars = scalars;
        _idleSnapshotSettings = settings;
        _idleSnapshotTransition = transition;
        return true;
    }

    private FalloutIdleTransition PrepareIdleSnapshot(RuntimeNativeNifAnimation incoming, FalloutIdleAnimationData timing,
        FalloutIdleGroupTextKeys keys, FalloutIdleTransition.Settings settings, FalloutIdleTransition.Snapshot? saved = null)
    {
        if (!keys.Group.Equals("SpecialIdle", StringComparison.OrdinalIgnoreCase) || incoming.Sequence.StartTime != 0)
            throw new NotSupportedException("Idle snapshot transition requires its qualified zero-start SpecialIdle source group.");
        incoming.RequireSnapshotTransitionOver(_baseAnimation);
        var admission = new FalloutIdleTransition.Admission(timing.Group, false, keys.HasSynchronization, false, false, false);
        return saved is null ? FalloutIdleTransition.CreateSnapshot(admission, keys.Blend, null, settings) :
            FalloutIdleTransition.Restore(saved, admission, keys.Blend, null, settings);
    }

    private IReadOnlyList<FalloutNifAnimationSample> ReadIdleSnapshotPose(FalloutActorResidualPose pose, RuntimeNativeNifAnimation incoming)
    {
        pose.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            Enumerable.Range(0, Skeleton.Node.GetBoneCount()).Select(index =>
                (Skeleton.Node.GetBoneName(index).ToString(), FalloutNifTransformComponents.None)).ToArray());
        var samples = new FalloutNifAnimationSample[pose.Bones.Count];
        // The full pose belongs to continuation. The temporary sequence only
        // captures targets actually controlled by this incoming sequence.
        foreach (var link in incoming.Sequence.ControlledBlocks.Where(link => link.ControllerType == "NiTransformController" &&
            link.PropertyType.Length == 0 && link.Variable1.Length == 0 && link.Variable2.Length == 0))
        {
            if (!Skeleton.TryBoneIndex(link.NodeName, out var index)) continue; // Typed ANIO targets have their own snapshot owner.
            var bone = pose.Bones[index];
            var p = bone.Position!; var q = bone.Rotation!; var s = bone.Scale!;
            if (s[0] != s[1] || s[0] != s[2])
            {
                var block = Skeleton.Node.GetBoneMeta(index, "opennv_nif_block").AsInt32();
                var sourceScale = Skeleton.Source.ReadNode(block).Transform.Scale;
                throw new NotSupportedException(FormattableString.Invariant(
                    $"Idle snapshot controlled target requires uniform local scale: bone={index}/{bone.Name} scale=[{s[0]:R},{s[1]:R},{s[2]:R}] sourceScale={sourceScale:R} binding={link.NodeName}/priority{link.Priority} sequence={incoming.Sequence.Name}/{incoming.Source.Sha256}."));
            }
            var units = Skeleton.UnitsToMetres;
            samples[bone.Index] = new(new(p[0] / units, -p[2] / units, p[1] / units), new(q[3], q[0], -q[2], q[1]), s[0]);
        }
        return Array.AsReadOnly(samples);
    }

    // The completion update still belongs to the held phase. Release on the
    // next update so neither overshoot nor a repeated start key enters the KF.
    private bool AdvanceIdleSnapshot(double delta)
    {
        if (_idleSnapshotTransition is not { } transition) return false;
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!ReferenceEquals(_animation, _idleSnapshotAnimation) || _idlePlayback is not { } clock)
            throw new InvalidOperationException("Idle snapshot lost its incoming animation or source clock.");
        if (!transition.Advance(delta))
        {
            _currentIdleRequest?.FinishTransition();
            ClearIdleSnapshot();
            return false;
        }
        clock.DispatchHeldBoundary(interval => PublishIdleTextKeys(_idleSnapshotAnimation!, interval));
        _animationSeconds = clock.SourceSeconds;
        return true;
    }

    private void PublishIdleSnapshot()
    {
        if (_idleSnapshotTransition is not { } transition) return;
        if (!ReferenceEquals(_animation, _idleSnapshotAnimation) || _idlePlayback is not { } clock)
            throw new InvalidOperationException("Idle snapshot lost its incoming presentation binding.");
        var factors = transition.Factors;
        _idleSnapshotAnimation!.ApplySnapshotTransition(clock.SourceSeconds, _idleSnapshotSamples!,
            factors.SnapshotWeight, factors.IncomingWeight, Combat?.PackageOwnsPose == true ? null : _baseAnimation,
            _baseAnimationSeconds, _idleSnapshotTargets, _idleSnapshotScalars);
    }

    private FalloutActorIdleSnapshotTransition? CaptureIdleSnapshot() => _idleSnapshotTransition is { } transition
        ? new(transition.Capture(), _idleSnapshotSettings, _idleSnapshotPose!.Copy(), _idleSnapshotTargets!.Select(value =>
        {
            var p = value.Local.Translation!.Value; var q = value.Local.Rotation!.Value;
            return new FalloutActorIdleSnapshotTarget(value.Target, value.Priority, [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], value.Local.Scale!.Value);
        }).ToArray(), _idleSnapshotScalars!.ToArray()) : null;

    private void RestoreIdleSnapshot(FalloutActorIdleSnapshotTransition saved)
    {
        if (IdleSnapshotActive || _animation is not { } incoming || _idleData is not { } timing || _idlePlayback is not { } clock)
            throw new InvalidOperationException("Idle snapshot restoration requires one bound incoming animation and fresh transition owner.");
        saved.Validate();
        var settings = IdleTransitionSettings();
        if (saved.Settings != settings) throw new InvalidDataException("Idle snapshot settings differ from their retained source.");
        var phase = clock.Capture();
        if (phase.SourceSeconds != 0 || phase.CompletedRepeats != 0 || phase.Complete || !saved.Phase.Started && !phase.IncludeStart)
            throw new InvalidDataException("Idle snapshot advanced its held incoming source phase or consumed its unstarted boundary.");
        var keys = FalloutIdleGroupTextKeys.Read(incoming.Sequence.Name, incoming.TextKeys);
        var transition = PrepareIdleSnapshot(incoming, timing, keys, settings, saved.Phase);
        var pose = saved.Pose.Copy();
        var samples = ReadIdleSnapshotPose(pose, incoming);
        var targets = (saved.Targets ?? []).Select(value => new RuntimeNifSnapshotTransform(value.Name, value.Priority,
            new(new(value.Position[0], value.Position[1], value.Position[2]),
                new(value.Rotation[3], value.Rotation[0], value.Rotation[1], value.Rotation[2]), value.Scale))).ToArray();
        var actual = incoming.CaptureSnapshotTransforms();
        if (!targets.Select(value => (value.Target, value.Priority)).SequenceEqual(actual.Select(value => (value.Target, value.Priority))))
            throw new InvalidDataException("Idle snapshot external targets differ from their source bindings.");
        var scalars = (saved.Scalars ?? []).ToArray();
        incoming.ValidateSnapshotScalars(scalars);
        _idleSnapshotAnimation = incoming;
        _idleSnapshotPose = pose;
        _idleSnapshotSamples = samples;
        _idleSnapshotTargets = targets;
        _idleSnapshotScalars = scalars;
        _idleSnapshotSettings = settings;
        _idleSnapshotTransition = transition;
        _currentIdleRequest?.SnapshotBlend();
    }

    private void ClearIdleSnapshot()
    {
        _idleSnapshotTransition = null; _idleSnapshotAnimation = null;
        _idleSnapshotPose = null; _idleSnapshotSamples = null;
        _idleSnapshotTargets = null;
        _idleSnapshotScalars = null;
    }
}
