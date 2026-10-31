using System.Runtime.InteropServices;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private sealed class PackageLayer(NativeActorCombatAnimation clip, double seconds, bool startPending)
    {
        internal NativeActorCombatAnimation Clip = clip;
        internal double Seconds = seconds;
        internal bool StartPending = startPending;
        internal FalloutIdleDeactivation? EaseIn;
        internal NativeActorCombatAnimation? EaseFrom;
        internal FalloutIdleTransition? Snapshot;
        internal FalloutActorIdleSnapshotTransition? SnapshotData;
        internal IReadOnlyList<FalloutNifAnimationSample>? Samples;
        internal IReadOnlyList<RuntimeNifSnapshotTransform>? Targets;
        internal IReadOnlyList<FalloutNifSnapshotScalar>? Scalars;
        internal FalloutPackageAnimationLayer Capture() => new(Clip.Path, Clip.Hash, Seconds, StartPending,
            EaseIn?.Capture(), Snapshot is null ? null : SnapshotData! with { Phase = Snapshot.Capture() },
            EaseFrom is null ? null : new(EaseFrom.Path, EaseFrom.Hash), Clip.Animation.AccumulationTime);
    }
    private sealed record PackageFade(PackageLayer Layer, FalloutIdleDeactivation EaseOut, bool HoldPhase,
        NativeActorCombatAnimation? Incoming, byte Slot);
    private PackageLayer? _packageIdleLayer, _packageMovementLayer;
    private readonly List<PackageFade> _packageOutgoingLayers = [];
    private readonly Dictionary<string, NativeActorCombatAnimation> _packageLayerClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FalloutIdleGroupTextKeys> _packageLayerKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> _packageBlendLayers = [];
    private readonly List<RuntimeNativeNifAnimation.ControllerSnapshot> _packageBlendSnapshots = [];
    private readonly List<FalloutNifTextKeyEvent> _packageLayerPendingEvents = [];
    private ulong _packageLayersAdvancedFrame = ulong.MaxValue;
    private bool _packageIdleClockOwned;
    private bool _packageLayersPublished;
    private bool _packageLayerPosePublished;
    private object? _packageLayerHandoff;
    internal object? PackageAnimationHandoff => _packageLayerHandoff;

    internal IReadOnlyList<object> ActivePackageAnimationLayers
    {
        get
        {
            if (!PackageOwnsPose || !_packageLayerPosePublished || _packageIdleLayer is null) return [];
            var result = new List<object>();
            void Add(PackageLayer layer, int slot, int state, float ease, bool outgoing)
            {
                var sequence = layer.Clip.Animation.Sequence;
                result.Add(new
                {
                    slot,
                    resource = layer.Clip.Path,
                    sourceHash = layer.Clip.Hash,
                    sourceSeconds = layer.Clip.Time(layer.Seconds),
                    accumulationSourceSeconds = layer.Clip.Animation.AccumulationTime,
                    elapsedSeconds = layer.Seconds,
                    begin = sequence.StartTime,
                    end = sequence.StopTime,
                    frequency = sequence.Frequency,
                    state,
                    weight = sequence.Weight,
                    ease,
                    outgoing,
                    snapshot = layer.Snapshot?.Capture(),
                    channelContributionsQualified = false
                });
            }
            var idle = _packageIdleLayer;
            Add(idle, 0, idle.Snapshot is not null ? 5 : idle.EaseIn is not null ? 2 : 1,
                idle.Snapshot?.Factors.IncomingWeight ?? 1 - (idle.EaseIn?.Weight ?? 0), false);
            if (_packageMovementLayer is { } movement)
                Add(movement, 1, movement.Snapshot is not null ? 5 : movement.EaseIn is not null ? 2 : 1,
                    movement.Snapshot?.Factors.IncomingWeight ?? 1 - (movement.EaseIn?.Weight ?? 0), false);
            foreach (var fade in _packageOutgoingLayers) Add(fade.Layer, fade.Slot, 3, fade.EaseOut.Weight, true);
            return result;
        }
    }

    private FalloutIdleTransition.Settings PackageBlendSettings() => new(
        _records.IniSettings.Float("fAnimationDefaultBlend:General"), _records.IniSettings.Float("fAnimationMult:General"));

    private FalloutIdleGroupTextKeys PackageKeys(NativeActorCombatAnimation clip)
    {
        if (!_packageLayerKeys.TryGetValue(clip.Path, out var keys))
            _packageLayerKeys.Add(clip.Path, keys = FalloutIdleGroupTextKeys.Read(clip.Animation.Sequence.Name, clip.Animation.TextKeys));
        return keys;
    }

    private NativeActorCombatAnimation PackageLayerClip(string path, string? hash = null)
    {
        if (!_packageLayerClips.TryGetValue(path, out var clip))
            _packageLayerClips.Add(path, clip = new(path, _content, _skeleton, null, true));
        if (hash is not null && !clip.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Retained package layer differs from its winning owned animation.");
        return clip;
    }

    private FalloutActorAnimationSnapshot PackageBaseClock() => _actor switch
    {
        RuntimeNativeCreature creature => creature.PackageBaseClock,
        RuntimeNativeNpc npc => npc.PackageBaseClock,
        _ => throw new NotSupportedException("Package base animation has no actor clock owner.")
    };

    private void AdoptPackageBaseClock()
    {
        var layer = _packageIdleLayer!;
        var clock = new FalloutActorAnimationSnapshot(layer.Clip.Path, layer.Clip.Hash, layer.Seconds, layer.StartPending);
        if (_actor is RuntimeNativeCreature creature) creature.AdoptPackageBaseClock(clock, layer.Clip.Animation);
        else ((RuntimeNativeNpc)_actor).AdoptPackageBaseClock(clock, layer.Clip.Animation);
    }

    private void EnsurePackageAnimationLayers(FalloutActorPackageMotion? retained)
    {
        if (_packageIdleLayer is not null) return;
        if (retained?.AnimationLayers is { } saved)
        {
            saved.Validate();
            _packageIdleLayer = RestorePackageLayer(saved.Idle);
            _packageMovementLayer = saved.Movement is null ? null : RestorePackageLayer(saved.Movement);
            foreach (var item in saved.Outgoing)
            {
                var layer = RestorePackageLayer(item.Layer);
                var incoming = item.Incoming is null ? null : PackageLayerClip(item.Incoming.Resource, item.Incoming.Sha256);
                var fade = new FalloutIdleDeactivation(PackageKeys(layer.Clip).Blend, PackageBlendSettings(),
                    incoming is null ? null : PackageKeys(incoming).Blend);
                fade.Restore(item.EaseOut);
                _packageOutgoingLayers.Add(new(layer, fade, item.HoldPhase, incoming, item.Slot));
            }
            if (!saved.OwnsIdleClock)
            {
                var clock = PackageBaseClock();
                if (!clock.Resource.Equals(saved.Idle.Resource, StringComparison.OrdinalIgnoreCase) ||
                    !clock.Sha256.Equals(saved.Idle.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Retired package base clock differs from its actual family owner.");
                _packageIdleLayer.Seconds = clock.ElapsedSeconds; _packageIdleLayer.StartPending = clock.StartPending;
            }
        }
        else
        {
            // Import the actual currently bound family clock, including its
            // phase. Older saves have no invented missing idle-layer history.
            var clock = PackageBaseClock();
            _packageIdleLayer = new(PackageLayerClip(clock.Resource, clock.Sha256), clock.ElapsedSeconds, clock.StartPending);
            if (retained is not null)
            {
                var (source, sequence) = NativeActorCombatAnimation.ReadSource(retained.Animation, _content);
                if (!source.Sha256.Equals(retained.AnimationSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Retained package source changed.");
                if (sequence.Name.Split('_')[0].ToLowerInvariant() is "forward" or "fastforward" or "turnleft" or "turnright")
                    _packageMovementLayer = new(PackageLayerClip(retained.Animation, retained.AnimationSha256), retained.Seconds, retained.StartPending);
            }
        }
        if (!PackageIsIdle(_packageIdleLayer.Clip) || _packageIdleLayer.Clip.Animation.Sequence.CycleType != 0)
            throw new NotSupportedException("Ordinary package locomotion requires its actual looping Idle0 source owner; furniture/stance handoff is unbound.");
        if (_packageMovementLayer is { } movement && PackageIsIdle(movement.Clip) ||
            _packageOutgoingLayers.Any(fade => PackageIsIdle(fade.Layer.Clip) != (fade.Slot == 0)))
            throw new InvalidDataException("Retained package movement slot contains a base-idle group.");
        AdoptPackageBaseClock();
    }

    private void RestorePackageAnimationLayers(FalloutActorPackageMotion motion)
    {
        if (motion.AnimationLayers is null) return;
        EnsurePackageAnimationLayers(motion);
        PublishPackageAnimationLayers();
        _packageLayersPublished = true;
        PackageOwnsPose = true;
    }

    private static bool PackageIsIdle(NativeActorCombatAnimation clip)
    {
        var group = clip.Animation.Sequence.Name.Split('_')[0];
        return group.Equals("Idle", StringComparison.OrdinalIgnoreCase) || group.Equals("DynamicIdle", StringComparison.OrdinalIgnoreCase);
    }

    private PackageLayer RestorePackageLayer(FalloutPackageAnimationLayer saved)
    {
        saved.Validate();
        var layer = new PackageLayer(PackageLayerClip(saved.Resource, saved.Sha256), saved.Seconds, saved.StartPending);
        layer.Clip.Animation.RestoreAccumulationTime(saved.AccumulationTime);
        if (saved.EaseIn is { } ease)
        {
            layer.EaseFrom = PackageLayerClip(saved.EaseFrom!.Resource, saved.EaseFrom.Sha256);
            layer.EaseIn = new(PackageKeys(layer.EaseFrom).Blend, PackageBlendSettings(), PackageKeys(layer.Clip).Blend);
            layer.EaseIn.Restore(ease);
        }
        if (saved.Snapshot is { } snapshot)
        {
            if (snapshot.Settings != PackageBlendSettings())
                throw new InvalidDataException("Package snapshot settings changed since admission.");
            var keys = PackageKeys(layer.Clip);
            layer.Snapshot = FalloutIdleTransition.Restore(snapshot.Phase,
                new(snapshot.Phase.Slot.Index, false, keys.HasSynchronization, false, false, false), keys.Blend, null, snapshot.Settings);
            BindPackageSnapshot(layer, snapshot.Copy());
        }
        return layer;
    }

    private void BindPackageSnapshot(PackageLayer layer, FalloutActorIdleSnapshotTransition data)
    {
        data.Pose.ValidateBinding(_skeletonPath, _skeleton.Source.Sha256,
            Enumerable.Range(0, _skeleton.Node.GetBoneCount()).Select(index =>
                (_skeleton.Node.GetBoneName(index).ToString(), FalloutNifTransformComponents.None)).ToArray());
        var samples = new FalloutNifAnimationSample[_skeleton.Node.GetBoneCount()];
        foreach (var link in layer.Clip.Animation.Sequence.ControlledBlocks.Where(link => link.ControllerType == "NiTransformController" &&
            link.PropertyType.Length == 0 && link.Variable1.Length == 0 && link.Variable2.Length == 0))
        {
            if (!_skeleton.TryBoneIndex(link.NodeName, out var index)) continue;
            var bone = data.Pose.Bones[index]; var p = bone.Position!; var q = bone.Rotation!; var scale = bone.Scale!;
            if (scale[0] != scale[1] || scale[0] != scale[2])
                throw new NotSupportedException("Package snapshot controlled target has nonuniform local scale.");
            var units = _skeleton.UnitsToMetres;
            samples[index] = new(new(p[0] / units, -p[2] / units, p[1] / units), new(q[3], q[0], -q[2], q[1]), scale[0]);
        }
        layer.Clip.Animation.RequireSnapshotTransitionOver(_packageIdleLayer?.Clip.Animation);
        layer.Targets = (data.Targets ?? []).Select(value => new RuntimeNifSnapshotTransform(value.Name, value.Priority,
            new(new(value.Position[0], value.Position[1], value.Position[2]),
                new(value.Rotation[3], value.Rotation[0], value.Rotation[1], value.Rotation[2]), value.Scale))).ToArray();
        layer.Scalars = data.Scalars ?? [];
        layer.Clip.Animation.ValidateSnapshotScalars(layer.Scalars);
        layer.Samples = samples; layer.SnapshotData = data;
    }

    private void BeginPackageSnapshot(PackageLayer layer, byte slot)
    {
        var keys = PackageKeys(layer.Clip); var settings = PackageBlendSettings();
        if (!float.IsFinite(settings.DefaultBlendSeconds) || !float.IsFinite(settings.AnimationMultiplier) || settings.AnimationMultiplier <= 0)
            throw new InvalidDataException("Package transition requires finite animation settings and a positive multiplier.");
        var duration = (keys.Blend.Blend == 0 && keys.Blend.BlendIn == 0 ? settings.DefaultBlendSeconds :
            Math.Max(keys.Blend.Blend, keys.Blend.BlendIn) / 30f) / settings.AnimationMultiplier;
        if (!float.IsFinite(duration)) throw new InvalidDataException("Package snapshot has an invalid duration.");
        if (duration <= 0.009999999776482582) return;
        layer.Snapshot = FalloutIdleTransition.CreateSnapshot(new(slot, false, keys.HasSynchronization, false, false, false),
            keys.Blend, null, settings);
        var node = _skeleton.Node;
        var pose = new FalloutActorResidualPose(_skeletonPath, _skeleton.Source.Sha256,
            Enumerable.Range(0, node.GetBoneCount()).Select(index =>
            {
                var p = node.GetBonePosePosition(index); var q = node.GetBonePoseRotation(index); var s = node.GetBonePoseScale(index);
                return new FalloutActorResidualBonePose(index, node.GetBoneName(index).ToString(),
                    [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z]);
            }).ToArray());
        var targets = layer.Clip.Animation.CaptureSnapshotTransforms().Select(value =>
        {
            var p = value.Local.Translation!.Value; var q = value.Local.Rotation!.Value;
            return new FalloutActorIdleSnapshotTarget(value.Target, value.Priority, [p.X, p.Y, p.Z],
                [q.X, q.Y, q.Z, q.W], value.Local.Scale!.Value);
        }).ToArray();
        BindPackageSnapshot(layer, new(layer.Snapshot.Capture(), settings, pose, targets, layer.Clip.Animation.CaptureSnapshotScalars()));
    }

    private void StopPackageLayer(ref PackageLayer? owner, byte slot, NativeActorCombatAnimation? incoming = null)
    {
        if (owner is not { } selected) return;
        var fade = new FalloutIdleDeactivation(PackageKeys(selected.Clip).Blend, PackageBlendSettings(),
            incoming is null ? null : PackageKeys(incoming).Blend);
        var held = selected.Snapshot is not null;
        selected.EaseIn = null; selected.EaseFrom = null; selected.Snapshot = null; selected.SnapshotData = null;
        selected.Samples = null; selected.Targets = null; selected.Scalars = null;
        if (!fade.Complete) _packageOutgoingLayers.Add(new(selected, fade, held, incoming, slot));
        owner = null;
    }

    private void SelectPackageLayer(ref PackageLayer? owner, NativeActorCombatAnimation? requested, byte slot)
    {
        if (slot == 1 && requested is not null && PackageIsIdle(requested)) requested = null;
        if (requested is null) { StopPackageLayer(ref owner, slot); return; }
        _packageLayerClips.TryAdd(requested.Path, requested);
        _packageOutgoingLayers.RemoveAll(fade => fade.Layer.Clip.Path.Equals(requested.Path, StringComparison.OrdinalIgnoreCase));
        if (owner is { } selected)
        {
            if (selected.Clip.Path.Equals(requested.Path, StringComparison.OrdinalIgnoreCase)) return;
            if (selected.Snapshot is not null || selected.EaseIn is { Complete: false })
            {
                if (slot == 1) return;
                // Native cancels a transitioning nonmovement slot immediately,
                // then admits the incoming sequence from the actual current pose.
                owner = null;
            }
            else
            {
                if (PackageKeys(selected.Clip).HasSynchronization || PackageKeys(requested).HasSynchronization)
                    throw new NotSupportedException("Synchronized package transition has no qualified phase owner.");
                var ease = new FalloutIdleDeactivation(PackageKeys(selected.Clip).Blend, PackageBlendSettings(), PackageKeys(requested).Blend);
                if (ease.DurationSeconds <= 0.009999999776482582)
                { requested.Animation.RestoreAccumulationTime(null); owner = new(requested, 0, true); return; }
                StopPackageLayer(ref owner, slot, requested);
                requested.Animation.RestoreAccumulationTime(null);
                owner = new(requested, 0, true) { EaseIn = ease, EaseFrom = selected.Clip };
                return;
            }
        }
        if (PackageKeys(requested).HasSynchronization)
            throw new NotSupportedException("Synchronized package activation has no qualified phase owner.");
        requested.Animation.RestoreAccumulationTime(null);
        owner = new(requested, 0, true);
        BeginPackageSnapshot(owner, slot);
    }

    private void AdvanceSelectedPackageLayer(PackageLayer layer, double delta)
    {
        layer.EaseIn?.Advance(delta);
        if (layer.EaseIn?.Complete == true) { layer.EaseIn = null; layer.EaseFrom = null; }
        if (layer.Snapshot is { } snapshot && !snapshot.Advance(delta))
        {
            layer.Snapshot = null; layer.SnapshotData = null; layer.Samples = null;
            layer.Targets = null; layer.Scalars = null;
        }
        if (layer.Snapshot is null) AdvancePackageLayerClock(layer, delta);
    }

    private void AdvancePackageLayerClock(PackageLayer layer, double delta)
    {
        var from = layer.Seconds; var include = layer.StartPending;
        var next = include ? from : from + delta;
        _packageLayerPendingEvents.AddRange(layer.Clip.Events.Crossed(from, next, include));
        layer.Seconds = next; layer.StartPending = false;
    }

    private (NativeActorCombatAnimation Clip, double From, double To) AdvancePackageAnimationLayers(
        FalloutActorPackageMotion? retained, NativeActorCombatAnimation? requested, double delta, NativeActorCombatAnimation? requestedIdle = null)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        _packageLayersAdvancedFrame = Engine.GetPhysicsFrames();
        _packageLayerPendingEvents.Clear();
        EnsurePackageAnimationLayers(retained);
        var idle = _packageIdleLayer!;
        if (!_packageIdleClockOwned)
        {
            var clock = PackageBaseClock();
            if (!clock.Resource.Equals(idle.Clip.Path, StringComparison.OrdinalIgnoreCase) ||
                !clock.Sha256.Equals(idle.Clip.Hash, StringComparison.OrdinalIgnoreCase))
            {
                var actual = PackageLayerClip(clock.Resource, clock.Sha256);
                if (!PackageIsIdle(actual) || actual.Animation.Sequence.CycleType != 0 ||
                    _packageMovementLayer is not null || _packageOutgoingLayers.Count != 0)
                    throw new InvalidDataException("Package resumed before its independent base owner returned a settled Idle0.");
                _packageIdleLayer = idle = new(actual, clock.ElapsedSeconds, clock.StartPending);
            }
            idle.Seconds = clock.ElapsedSeconds; idle.StartPending = clock.StartPending;
        }
        _packageIdleClockOwned = true; _packageLayersPublished = true;
        if (requestedIdle is not null) SelectPackageLayer(ref _packageIdleLayer, requestedIdle, 0);
        SelectPackageLayer(ref _packageMovementLayer, requested, 1);
        idle = _packageIdleLayer!;
        var idleFrom = idle.Seconds;
        AdvanceSelectedPackageLayer(idle, delta);
        for (var index = _packageOutgoingLayers.Count - 1; index >= 0; --index)
        {
            var fade = _packageOutgoingLayers[index]; fade.EaseOut.Advance(delta);
            if (fade.EaseOut.Complete) { _packageOutgoingLayers.RemoveAt(index); continue; }
            if (!fade.HoldPhase) AdvancePackageLayerClock(fade.Layer, delta);
        }
        var active = _packageMovementLayer ?? idle; var from = _packageMovementLayer is null ? idleFrom : active.Seconds;
        if (_packageMovementLayer is { } movement) AdvanceSelectedPackageLayer(movement, delta);
        AdoptPackageBaseClock();
        return (active.Clip, from, active.Seconds);
    }

    private bool _packageAccumulationBlocked;
    private ulong _packageAccumulationFrame = ulong.MaxValue;
    private bool PackageAccumulationAwaitingTranslation => !OwnsPose && _packageAccumulationFrame != ulong.MaxValue &&
        Engine.GetPhysicsFrames() - _packageAccumulationFrame <= 1 && _packageAccumulationBlocked;

    private Vector3 ConsumePackageAccumulation()
    {
        _packageBlendLayers.Clear(); _packageBlendSnapshots.Clear();
        AppendPackageAnimationLayers(_packageBlendLayers, _packageBlendSnapshots);
        if (_actor is RuntimeNativeCreature creature) creature.AppendConcurrentIdleLayers(_packageBlendLayers, _packageBlendSnapshots);
        else if (_actor is RuntimeNativeNpc npc) npc.AppendConcurrentIdleLayers(_packageBlendLayers, _packageBlendSnapshots);
        var result = RuntimeNativeNifAnimation.ConsumeAccumulationLayers(CollectionsMarshal.AsSpan(_packageBlendLayers),
            CollectionsMarshal.AsSpan(_packageBlendSnapshots));
        _packageAccumulationFrame = Engine.GetPhysicsFrames();
        _packageAccumulationBlocked = result.IsZeroApprox() &&
            _packageBlendLayers.Any(layer => layer.Animation.HasAccumulationController);
        _packageBlendLayers.Clear(); _packageBlendSnapshots.Clear();
        return result;
    }

    private void PublishPackageAnimationLayers()
    {
        _packageLayerPosePublished = true;
        if (_headless) return;
        _packageBlendLayers.Clear();
        var idle = _packageIdleLayer!;
        // Accumulation is extracted into the real actor body. Preserve the
        // other bones' current source components rather than resetting them.
        var root = _skeleton.BoneIndex(idle.Clip.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        _packageBlendSnapshots.Clear();
        AppendPackageAnimationLayers(_packageBlendLayers, _packageBlendSnapshots);
        if (_actor is RuntimeNativeCreature creature) creature.AppendConcurrentIdleLayers(_packageBlendLayers, _packageBlendSnapshots);
        else if (_actor is RuntimeNativeNpc npc) npc.AppendConcurrentIdleLayers(_packageBlendLayers, _packageBlendSnapshots);
        RuntimeNativeNifAnimation.ApplyControllerLayers(CollectionsMarshal.AsSpan(_packageBlendLayers),
            CollectionsMarshal.AsSpan(_packageBlendSnapshots));
        _packageBlendSnapshots.Clear();
        _packageBlendLayers.Clear();
    }

    private void AppendPackageAnimationLayers(List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers,
        List<RuntimeNativeNifAnimation.ControllerSnapshot> snapshots)
    {
        void Add(PackageLayer layer)
        {
            layers.Add((layer.Clip.Animation, layer.Clip.Time(layer.Seconds), 1 - (layer.EaseIn?.Weight ?? 0)));
            if (layer.Snapshot is { } snapshot)
            {
                var factors = snapshot.Factors;
                snapshots.Add(new(layer.Clip.Animation, layer.Samples!, layer.Targets!, layer.Scalars!,
                    factors.SnapshotWeight, factors.IncomingWeight));
            }
        }
        Add(_packageIdleLayer!);
        foreach (var fade in _packageOutgoingLayers)
            layers.Add((fade.Layer.Clip.Animation, fade.Layer.Clip.Time(fade.Layer.Seconds), fade.EaseOut.Weight));
        if (_packageMovementLayer is { } movement) Add(movement);
    }

    // Slot7 composition reads the actual package clocks; it never advances or
    // replaces their physical root-motion owner.
    internal void ComposeIdleLayers(List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers,
        RuntimeNativeNifAnimation? family, float familySeconds,
        RuntimeNativeNifAnimation.ControllerSnapshot? snapshot = null)
    {
        _packageBlendLayers.Clear(); _packageBlendSnapshots.Clear();
        if (PackageOwnsPose && _packageLayerPosePublished && _packageIdleLayer is not null)
            AppendPackageAnimationLayers(_packageBlendLayers, _packageBlendSnapshots);
        else if (family is not null) _packageBlendLayers.Add((family, familySeconds, 1));
        _packageBlendLayers.AddRange(layers);
        if (snapshot is { } pose) _packageBlendSnapshots.Add(pose);
        try
        {
            RuntimeNativeNifAnimation.ApplyControllerLayers(CollectionsMarshal.AsSpan(_packageBlendLayers),
                CollectionsMarshal.AsSpan(_packageBlendSnapshots));
        }
        finally { _packageBlendLayers.Clear(); _packageBlendSnapshots.Clear(); }
    }

    private void DispatchPackageLayerEvents()
    {
        foreach (var key in _packageLayerPendingEvents) PackageSounds().Dispatch(key);
        _packageLayerPendingEvents.Clear();
    }

    private FalloutActorPackageAnimationLayers CapturePackageAnimationLayers() => new(_packageIdleLayer!.Capture(),
        _packageMovementLayer?.Capture(), _packageOutgoingLayers.Select(fade =>
            new FalloutPackageAnimationFade(fade.Layer.Capture(), fade.EaseOut.Capture(), fade.HoldPhase,
                fade.Incoming is null ? null : new(fade.Incoming.Path, fade.Incoming.Hash), fade.Slot)).ToArray(), _packageIdleClockOwned);

    private void AdvanceRetiringPackageAnimationLayers(double delta)
    {
        if (_packageIdleLayer is null || !_packageLayersPublished || _packageLayersAdvancedFrame == Engine.GetPhysicsFrames() ||
            OwnsPose || Dead || !EffectiveEnabled) return;
        if (Error is not null || _state.ScriptError is not null || _actor is RuntimeNativeNpc { AiError: not null } ||
            _actor is RuntimeNativeCreature { PackageAnimationError: not null }) return;
        try
        {
            var family = PackageBaseClock();
            if (PackageOwnsPose || !family.Resource.Equals(_packageIdleLayer.Clip.Path, StringComparison.OrdinalIgnoreCase))
            {
                // A real family procedure has already published another base
                // binding (furniture, eating, etc.). Yield to that existing
                // owner; do not write the retired walking clock into it.
                _packageLayerHandoff = new
                {
                    source = _packageIdleLayer.Clip.Path,
                    destination = family.Resource,
                    owner = "existing-family-procedure",
                    nativeBlendQualified = false
                };
                _packageIdleLayer = null; _packageMovementLayer = null; _packageOutgoingLayers.Clear();
                _packageIdleClockOwned = false; _packageLayersPublished = false; _packageLayerPosePublished = false;
                if (_state.PackageMotion is { } prior) _state.PackageMotion = prior with { AnimationLayers = null };
                return;
            }
            if (_packageMovementLayer is not null || _packageOutgoingLayers.Count != 0 ||
                _packageIdleLayer.Snapshot is not null || _packageIdleLayer.EaseIn is not null)
            {
                AdvancePackageAnimationLayers(null, null, delta);
                DispatchPackageLayerEvents();
                PackageOwnsPose = _packageOutgoingLayers.Count != 0 || _packageIdleLayer!.Snapshot is not null || _packageIdleLayer.EaseIn is not null;
                PublishPackageAnimationLayers();
                if (!PackageOwnsPose) _packageIdleClockOwned = false;
                if (_state.PackageMotion is { } motion)
                {
                    var idle = _packageIdleLayer!;
                    _state.PackageMotion = motion with
                    {
                        Animation = idle.Clip.Path,
                        AnimationSha256 = idle.Clip.Hash,
                        Seconds = idle.Seconds,
                        StartPending = idle.StartPending,
                        AnimationLayers = CapturePackageAnimationLayers()
                    };
                }
            }
            else
            {
                _packageIdleClockOwned = false;
                if (_state.PackageMotion is { AnimationLayers: { } layers } prior)
                    _state.PackageMotion = prior with { AnimationLayers = layers with { OwnsIdleClock = false } };
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
        {
            Error = error.Message; _state.ScriptError ??= "Package animation continuation: " + error.Message;
            GD.PushError($"OPENNV_PACKAGE_ANIMATION_UNBOUND reference={_state.Reference}: {error.Message}");
        }
    }
}
