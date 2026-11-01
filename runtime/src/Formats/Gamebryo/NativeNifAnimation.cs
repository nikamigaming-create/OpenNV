using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record RuntimeNifUnboundAnimationChannel(FalloutNifControllerLink Source, string Reason);
internal sealed record RuntimeNifSnapshotTransform(string Target, byte Priority, FalloutNifAnimationSample Local);

internal sealed partial class RuntimeNativeNifAnimation
{
    private readonly RuntimeNativeNifSkeleton _skeleton;
    private readonly List<(int Bone, byte Priority, FalloutNifAnimationSampler Sampler)> _transforms = [];
    private readonly Dictionary<int, FalloutNifTransformComponents> _transformComponents = [];
    private readonly List<Action<float>> _otherChannels = [];
    private readonly Dictionary<FalloutNifScalarTarget, RuntimeNifScalarBinding> _scalars = [];
    private readonly List<RuntimeNifUnboundAnimationChannel> _unbound = [];
    private readonly List<FalloutNifControllerLink> _absentTargets = [];
    private readonly Dictionary<FalloutNifControllerLink, Action<float>> _snapshotCandidateActions = [];
    private readonly Dictionary<string, (Node3D Node, byte Priority, FalloutNifAnimationSampler Sampler)> _snapshotTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<Node3D, NativeNifAnimationBlend.Bone> _snapshotBlendScratch = [];

    // The caller selects the source sequence and owns its clock. Construction
    // never starts playback, resets poses, or chooses a locomotion/idle package.
    internal RuntimeNativeNifAnimation(
        FalloutNifFile source,
        FalloutNifControllerSequence sequence,
        RuntimeNativeNifSkeleton skeleton,
        Func<FalloutNifControllerLink, Action<float>?>? bindOtherChannel = null,
        Action<FalloutNifAnimationSample>? accumulationRoot = null,
        IReadOnlySet<string>? externalObjectTargets = null,
        Func<FalloutNifControllerLink, RuntimeNifScalarBinding?>? bindScalarChannel = null,
        bool externalAccumulationRoot = false)
    {
        if (!float.IsFinite(sequence.Weight) || sequence.Weight < 0)
            throw new InvalidDataException("Source animation sequence weight is invalid.");
        source.RequireNoActiveAnimationNotes(sequence);
        Source = source;
        Sequence = sequence;
        _skeleton = skeleton;
        TextKeys = sequence.TextKeys < 0 ? [] :
            (source.ReadObject(sequence.TextKeys) as FalloutNifTextKeyExtraData ??
                throw new InvalidDataException("Sequence text-key link is not text-key data.")).Keys;
        var targets = new HashSet<int>();
        var floatTargets = new HashSet<(string Node, string Name)>();
        var rootBound = false;
        foreach (var link in sequence.ControlledBlocks)
        {
            try
            {
                // Weapon groups contain channels for several alternative
                // models, including targets without a ## prefix. A target
                // absent from both source palettes is a null blend slot. A
                // declared source target without a runtime binding still fails.
                if (externalObjectTargets is not null && !skeleton.HasSourceTarget(link.NodeName) &&
                    !skeleton.MaterialChannels.HasSourceTarget(link.NodeName) && !externalObjectTargets.Contains(link.NodeName))
                {
                    if (link.Interpolator >= 0) _ = source.ReadObject(link.Interpolator);
                    _absentTargets.Add(link); continue;
                }
                if (link.ControllerType == "NiTransformController" &&
                    link.PropertyType.Length == 0 && link.Variable1.Length == 0 && link.Variable2.Length == 0)
                {
                    if (link.NodeName == sequence.TargetName && (accumulationRoot is not null || externalAccumulationRoot))
                    {
                        if (rootBound) throw new NotSupportedException("Multiple accumulation channels require a blend owner.");
                        rootBound = true;
                        var rootSampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                        if (externalAccumulationRoot) { _accumulationSampler = rootSampler; _accumulationPriority = link.Priority; }
                        if (accumulationRoot is not null) _otherChannels.Add(time => accumulationRoot(rootSampler.Sample(time)));
                        continue;
                    }
                    if (!skeleton.TryBoneIndex(link.NodeName, out var bone))
                    {
                        if (bindOtherChannel?.Invoke(link) is { } applyObject)
                        {
                            _otherChannels.Add(applyObject);
                            _snapshotCandidateActions.Add(link, applyObject);
                        }
                        else
                            _unbound.Add(new(link, "The source transform target has no skeleton or animation-object owner."));
                        continue;
                    }
                    var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                    if (!targets.Add(bone))
                        throw new NotSupportedException("Multiple transform links require a blend/priority owner.");
                    _transformComponents.Add(bone, sampler.Sample(sequence.StartTime).Components);
                    _transforms.Add((bone, link.Priority, sampler));
                }
                else if (link.ControllerType == "NiFloatExtraDataController")
                {
                    if (!floatTargets.Add((link.NodeName, link.Variable1)))
                        throw new NotSupportedException("Multiple float links require a blend/priority owner.");
                    var scalar = skeleton.FloatExtraData.Bind(source, link);
                    _scalars.Add(scalar.Target, scalar);
                }
                else if ((skeleton.BindScalarChannel(source, link) ?? bindScalarChannel?.Invoke(link)) is { } boundScalar)
                {
                    if (!_scalars.TryAdd(boundScalar.Target, boundScalar))
                        throw new NotSupportedException("Multiple scalar links require a blend/priority owner.");
                }
                else if (skeleton.BindVisualChannel(source, link) is { } visual)
                    _otherChannels.Add(visual);
                else if (bindOtherChannel?.Invoke(link) is { } apply)
                    _otherChannels.Add(apply);
                else if (link.ControllerType == "NiVisController" && link.PropertyType.Length == 0 &&
                    link.Variable1.Length == 0 && link.Variable2.Length == 0 && !skeleton.HasSourceTarget(link.NodeName))
                {
                    // The native sequence keeps a null controller/blend slot
                    // for a visibility target absent from its source palette.
                    // Preserve that disposition explicitly; a declared source
                    // target without its runtime binding still fails above.
                    _ = new FalloutNifBoolAnimation(source, link.Interpolator);
                    _absentTargets.Add(link);
                }
                else
                    _unbound.Add(new(link, "The source controller channel has no runtime owner."));
            }
            catch (Exception error) when (error is NotSupportedException or InvalidDataException)
            {
                _unbound.Add(new(link, error.Message));
            }
        }
    }

    internal FalloutNifFile Source { get; }
    internal FalloutNifControllerSequence Sequence { get; }
    internal IReadOnlyList<FalloutNifTextKey> TextKeys { get; }
    internal IReadOnlyList<RuntimeNifUnboundAnimationChannel> UnboundChannels => _unbound;
    internal IReadOnlyList<FalloutNifControllerLink> AbsentSourceTargets => _absentTargets;
    internal int TransformChannelCount => _transforms.Count;
    internal int ScalarChannelCount => _scalars.Count;

    internal IReadOnlyList<FalloutNifSnapshotScalar> CaptureSnapshotScalars() => _scalars.Values
        .Select(binding => new FalloutNifSnapshotScalar(binding.Target, binding.Priority, binding.Read())).ToArray();

    internal void ValidateSnapshotScalars(IReadOnlyList<FalloutNifSnapshotScalar> values)
    {
        if (values.Count != _scalars.Count)
            throw new InvalidDataException("Snapshot scalar bindings differ from their source.");
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null) throw new InvalidDataException("Snapshot scalar is absent.");
            value.Validate();
            if (!_scalars.TryGetValue(value.Target, out var binding) || binding.Priority != value.Priority ||
                binding.Normalize(value.Value) != value.Value)
                throw new InvalidDataException("Snapshot scalar target, priority or value differs from its source binding.");
            for (var previous = 0; previous < index; previous++)
                if (values[previous].Target == value.Target)
                    throw new InvalidDataException("Snapshot scalar target is duplicated.");
        }
    }

    internal void RegisterSnapshotTransformTarget(FalloutNifControllerLink link, Node3D target)
    {
        if (!GodotObject.IsInstanceValid(target) || target.IsQueuedForDeletion() ||
            !_snapshotCandidateActions.TryGetValue(link, out var action) || _snapshotTargets.ContainsKey(link.NodeName))
            throw new InvalidOperationException("Snapshot transform target has no unique live source binding.");
        var sampler = new FalloutNifAnimationSampler(Source, link.Interpolator);
        _snapshotTargets.Add(link.NodeName, (target, link.Priority, sampler));
        _otherChannels.Remove(action); _snapshotCandidateActions.Remove(link);
    }

    internal IReadOnlyList<RuntimeNifSnapshotTransform> CaptureSnapshotTransforms() => _snapshotTargets
        .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var (node, priority, _) = pair.Value;
            if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion())
                throw new InvalidOperationException("Snapshot transform target was retired.");
            var p = node.Position; var q = node.Quaternion; var s = node.Scale;
            if (s.X != s.Y || s.X != s.Z || !s.IsFinite() || s.X <= 0)
                throw new NotSupportedException("Snapshot target requires uniform positive local scale.");
            var units = _skeleton.UnitsToMetres;
            return new RuntimeNifSnapshotTransform(pair.Key, priority,
                new(new(p.X / units, -p.Z / units, p.Y / units), new(q.W, q.X, -q.Z, q.Y), s.X));
        }).ToArray();

    internal void RequireSnapshotTransitionOver(RuntimeNativeNifAnimation? background)
    {
        if (background is not null && (_skeleton != background._skeleton || background._unbound.Count != 0) ||
            _unbound.Count != 0 || _otherChannels.Count != 0 || _transforms.Count + _snapshotTargets.Count + _scalars.Count == 0)
            throw new NotSupportedException("Snapshot transition requires complete typed transform/scalar bindings on one skeleton.");
    }

    internal void ApplySnapshotTransition(float sourceTime, IReadOnlyList<FalloutNifAnimationSample> snapshot,
        float snapshotWeight, float incomingWeight, RuntimeNativeNifAnimation? background = null, float backgroundTime = 0,
        IReadOnlyList<RuntimeNifSnapshotTransform>? external = null, IReadOnlyList<FalloutNifSnapshotScalar>? scalars = null)
    {
        if (background is null)
            ApplySnapshotTransitionOverLayers(sourceTime, snapshot, snapshotWeight, incomingWeight, [], external, scalars);
        else
            ApplySnapshotTransitionOverLayers(sourceTime, snapshot, snapshotWeight, incomingWeight,
                [(background, backgroundTime, 1)], external, scalars);
    }

    internal void ApplySnapshotTransitionOverLayers(float sourceTime, IReadOnlyList<FalloutNifAnimationSample> snapshot,
        float snapshotWeight, float incomingWeight,
        ReadOnlySpan<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> background,
        IReadOnlyList<RuntimeNifSnapshotTransform>? external = null, IReadOnlyList<FalloutNifSnapshotScalar>? scalars = null)
    {
        if (_unbound.Count != 0 || _otherChannels.Count != 0 || snapshot.Count != _skeleton.Node.GetBoneCount() ||
            !float.IsFinite(snapshotWeight) || !float.IsFinite(incomingWeight) || snapshotWeight < 0 || incomingWeight < 0 ||
            snapshotWeight + incomingWeight <= 0 || !float.IsFinite(sourceTime) || sourceTime < Sequence.StartTime || sourceTime > Sequence.StopTime)
            throw new InvalidDataException("Snapshot transition has invalid weights, phase or source bindings.");
        external ??= [];
        scalars ??= [];
        ValidateSnapshotScalars(scalars);
        if (external.Count != _snapshotTargets.Count || external.Select(value => value.Target).Distinct(StringComparer.Ordinal).Count() != external.Count ||
            external.Any(value => !_snapshotTargets.TryGetValue(value.Target, out var target) || target.Priority != value.Priority))
            throw new InvalidDataException("Snapshot external transform bindings differ from their source.");
        var layers = _skeleton.AnimationBlend.LayerScratch(background.Length + 1);
        background.CopyTo(layers);
        layers[^1] = (this, sourceTime, 1);
        try { ApplyControllerLayers(layers, [new(this, snapshot, external, scalars, snapshotWeight, incomingWeight)]); }
        finally { layers.Clear(); }
    }

    // The same whole-bone priority selection used by ApplyLayers determines
    // which components have a current source owner. Missing components retain
    // their previously published pose and need an independent continuation.
    internal static FalloutNifTransformComponents[] TransformCoverage(params RuntimeNativeNifAnimation[] layers)
    {
        if (layers.Length == 0) throw new ArgumentException("Pose coverage needs a source layer.", nameof(layers));
        var skeleton = layers[0]._skeleton;
        var channels = new List<FalloutNifTransformCoverageChannel>();
        foreach (var animation in layers)
        {
            if (animation._skeleton != skeleton || animation.UnboundChannels.Count != 0)
                throw new NotSupportedException("Pose coverage requires complete source layers on one skeleton.");
            foreach (var (bone, priority, _) in animation._transforms)
                channels.Add(new(bone, priority, animation.Sequence.Weight, animation._transformComponents[bone]));
        }
        return FalloutNifTransformCoverage.Resolve(skeleton.Node.GetBoneCount(), channels);
    }

    internal void ApplySourceTime(float sourceTime)
    {
        if (_unbound.Count != 0)
            throw new NotSupportedException($"Selected source sequence {Sequence.Name} has {_unbound.Count} unbound channels: " +
                string.Join("; ", _unbound.Select(item => $"{item.Source.NodeName}/{item.Source.ControllerType}: {item.Reason}")));
        ApplyTransformChannels(sourceTime);
        foreach (var binding in _scalars.Values) binding.Publish(binding.Sample(sourceTime));
        foreach (var apply in _otherChannels)
            apply(sourceTime);
    }

    // Explicit partial-channel diagnostic: unlike ApplySourceTime this does not
    // certify a complete clip and does not consume/clear UnboundChannels.
    internal void ApplyTransformChannels(float sourceTime)
    {
        if (!float.IsFinite(sourceTime) || sourceTime < Sequence.StartTime || sourceTime > Sequence.StopTime)
            throw new ArgumentOutOfRangeException(nameof(sourceTime), "The animation owner must resolve source sequence timing.");
        foreach (var (bone, _, sampler) in _transforms)
        {
            var sample = sampler.Sample(sourceTime);
            if (sample.Translation is { } translation)
                _skeleton.Node.SetBonePosePosition(bone,
                    GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * _skeleton.UnitsToMetres);
            if (sample.Rotation is { } rotation)
            {
                var quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W);
                if (!quaternion.IsFinite() || quaternion.LengthSquared() <= 0.0f)
                    throw new InvalidDataException($"Source animation produced an invalid quaternion for bone {bone}.");
                _skeleton.Node.SetBonePoseRotation(bone, quaternion.Normalized());
            }
            if (sample.Scale is { } scale)
            {
                if (!float.IsFinite(scale) || scale <= 0.0f)
                    throw new InvalidDataException($"Source animation produced an invalid scale for bone {bone}.");
                _skeleton.Node.SetBonePoseScale(bone, Vector3.One * scale);
            }
        }
        foreach (var (_, target) in _snapshotTargets)
        {
            var sample = target.Sampler.Sample(sourceTime);
            if (sample.Translation is { } translation)
                target.Node.Position = GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * _skeleton.UnitsToMetres;
            if (sample.Rotation is { } rotation)
                target.Node.Quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized();
            if (sample.Scale is { } scale) target.Node.Scale = Vector3.One * scale;
        }
    }

    internal static void ApplyLayers(params (RuntimeNativeNifAnimation Animation, float Time)[] layers)
        => ApplyLayers(layers.AsSpan());

    internal static void ApplyLayers(ReadOnlySpan<(RuntimeNativeNifAnimation Animation, float Time)> layers)
    {
        if (layers.Length == 0) return;
        var scratch = layers[0].Animation._skeleton.AnimationBlend.LayerScratch(layers.Length);
        for (var index = 0; index < layers.Length; index++) scratch[index] = (layers[index].Animation, layers[index].Time, 1);
        try { ApplyWeightedLayers(scratch); }
        finally { scratch.Clear(); }
    }

    // Ease is transient controller state; it scales, never replaces, the
    // authored sequence weight. Attachment priority survives zero contribution.
    internal static void ApplyWeightedLayers(ReadOnlySpan<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers)
        => ApplyControllerLayers(layers, []);

    internal readonly record struct ControllerSnapshot(RuntimeNativeNifAnimation Owner,
        IReadOnlyList<FalloutNifAnimationSample> Samples, IReadOnlyList<RuntimeNifSnapshotTransform> Targets,
        IReadOnlyList<FalloutNifSnapshotScalar> Scalars, float SnapshotWeight, float IncomingWeight);

    // A single attached interpolator normalizes to one, including zero ease
    // or sequence weight. Keep the existing reduction/publication arithmetic,
    // while avoiding three priority passes and repeated empty snapshot scans.
    // External transform aliases retain the ordinary multi-target reducer.
    private void ApplySingleController(float time, float ease)
    {
        var blend = _skeleton.AnimationBlend;
        blend.Clear();
        _snapshotBlendScratch.Clear();
        if (UnboundChannels.Count != 0)
            throw new NotSupportedException("Animation layers require one skeleton and complete source bindings.");
        if (!float.IsFinite(ease) || ease < 0 || ease > 1 || !float.IsFinite(Sequence.Weight) || Sequence.Weight < 0 ||
            !float.IsFinite(time) || time < Sequence.StartTime || time > Sequence.StopTime)
            throw new InvalidDataException("Animation layer has an invalid ease, source weight or clock.");
        foreach (var (bone, _, sampler) in _transforms)
        {
            ref var current = ref blend.Bones[bone];
            current.Active = true;
            current.Add(sampler.Sample(time), 1);
        }
        foreach (var binding in _scalars.Values)
        {
            var current = new NativeNifAnimationBlend.Scalar { Binding = binding };
            current.Add(binding.Sample(time), 1);
            blend.Scalars.Add(binding.Target, current);
        }
        blend.Publish(_skeleton);
        foreach (var current in blend.Scalars.Values)
            if (current.Weight > 0) current.Binding.Publish(current.Value);
        blend.Scalars.Clear();
        if (ease > 0 && Sequence.Weight > 0)
            foreach (var apply in _otherChannels) apply(time);
    }

    internal static void ApplyControllerLayers(ReadOnlySpan<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers,
        ReadOnlySpan<ControllerSnapshot> snapshots)
    {
        if (layers.Length == 0) return;
        if (layers.Length == 1 && snapshots.Length == 0 && layers[0].Animation._snapshotTargets.Count == 0)
        {
            var single = layers[0];
            single.Animation.ApplySingleController(single.Time, single.Ease);
            return;
        }
        var skeleton = layers[0].Animation._skeleton;
        var blend = skeleton.AnimationBlend;
        blend.Clear();
        var scalarBlend = blend.Scalars;
        var nodes = layers[0].Animation._snapshotBlendScratch;
        nodes.Clear();
        static float SequenceWeight(RuntimeNativeNifAnimation animation, ReadOnlySpan<ControllerSnapshot> snapshots)
        {
            foreach (var item in snapshots)
                if (ReferenceEquals(animation, item.Owner)) return animation.Sequence.Weight * item.IncomingWeight;
            return animation.Sequence.Weight;
        }
        for (var index = 0; index < snapshots.Length; index++)
        {
            var item = snapshots[index];
            item.Owner.RequireSnapshotTransitionOver(layers[0].Animation);
            if (item.Samples.Count != skeleton.Node.GetBoneCount() || !float.IsFinite(item.SnapshotWeight) ||
                !float.IsFinite(item.IncomingWeight) || item.SnapshotWeight < 0 || item.IncomingWeight < 0 ||
                item.SnapshotWeight + item.IncomingWeight <= 0 || item.Targets.Count != item.Owner._snapshotTargets.Count)
                throw new InvalidDataException("Controller snapshot has invalid bindings or weights.");
            item.Owner.ValidateSnapshotScalars(item.Scalars);
            foreach (var value in item.Targets)
                if (!item.Owner._snapshotTargets.TryGetValue(value.Target, out var target) || target.Priority != value.Priority)
                    throw new InvalidDataException("Controller snapshot external target differs from its binding.");
            for (var previous = 0; previous < index; previous++)
                if (ReferenceEquals(snapshots[previous].Owner, item.Owner))
                    throw new InvalidDataException("A controller has duplicate snapshot owners.");
        }
        foreach (var (animation, time, ease) in layers)
        {
            if (animation._skeleton != skeleton || animation.UnboundChannels.Count != 0)
                throw new NotSupportedException("Animation layers require one skeleton and complete source bindings.");
            if (!float.IsFinite(ease) || ease < 0 || ease > 1 || !float.IsFinite(animation.Sequence.Weight) || animation.Sequence.Weight < 0 ||
                !float.IsFinite(time) || time < animation.Sequence.StartTime || time > animation.Sequence.StopTime)
                throw new InvalidDataException("Animation layer has an invalid ease, source weight or clock.");
            foreach (var (bone, priority, _) in animation._transforms)
            {
                ref var current = ref blend.Bones[bone];
                current.Weights.Attach(priority);
                current.Active = true;
            }
            foreach (var (_, target) in animation._snapshotTargets)
            {
                if (!GodotObject.IsInstanceValid(target.Node) || target.Node.IsQueuedForDeletion())
                    throw new InvalidOperationException("Animated external transform target was retired.");
                nodes.TryGetValue(target.Node, out var current);
                current.Weights.Attach(target.Priority); nodes[target.Node] = current;
            }
            foreach (var binding in animation._scalars.Values)
            {
                scalarBlend.TryGetValue(binding.Target, out var current);
                current.Binding = binding; current.Weights.Attach(binding.Priority); scalarBlend[binding.Target] = current;
            }
        }
        foreach (var item in snapshots)
        {
            var snapshotOwner = item.Owner; var external = item.Targets; var scalars = item.Scalars;
            foreach (var (bone, priority, _) in snapshotOwner._transforms) blend.Bones[bone].Weights.Attach(priority);
            foreach (var value in external!)
            {
                var node = snapshotOwner._snapshotTargets[value.Target].Node;
                var current = nodes[node]; current.Weights.Attach(value.Priority); nodes[node] = current;
            }
            foreach (var value in scalars!)
            {
                var current = scalarBlend[value.Target]; current.Weights.Attach(value.Priority); scalarBlend[value.Target] = current;
            }
        }
        foreach (var (animation, time, ease) in layers)
        {
            foreach (var (bone, priority, _) in animation._transforms)
                blend.Bones[bone].Weights.Gather(priority, SequenceWeight(animation, snapshots), ease);
            foreach (var (_, target) in animation._snapshotTargets)
            {
                var current = nodes[target.Node]; current.Weights.Gather(target.Priority, SequenceWeight(animation, snapshots), ease);
                nodes[target.Node] = current;
            }
            foreach (var binding in animation._scalars.Values)
            {
                var current = scalarBlend[binding.Target]; current.Weights.Gather(binding.Priority, SequenceWeight(animation, snapshots), ease);
                scalarBlend[binding.Target] = current;
            }
        }
        foreach (var item in snapshots)
        {
            var snapshotOwner = item.Owner; var external = item.Targets; var scalars = item.Scalars; var snapshotWeight = item.SnapshotWeight;
            foreach (var (bone, priority, _) in snapshotOwner._transforms) blend.Bones[bone].Weights.Gather(priority, snapshotWeight, 1);
            foreach (var value in external!)
            {
                var node = snapshotOwner._snapshotTargets[value.Target].Node;
                var current = nodes[node]; current.Weights.Gather(value.Priority, snapshotWeight, 1); nodes[node] = current;
            }
            foreach (var value in scalars!)
            {
                var current = scalarBlend[value.Target]; current.Weights.Gather(value.Priority, snapshotWeight, 1);
                scalarBlend[value.Target] = current;
            }
        }
        foreach (var (animation, time, ease) in layers)
        {
            foreach (var (bone, priority, sampler) in animation._transforms)
            {
                ref var current = ref blend.Bones[bone];
                var weight = current.Weights.Normalize(priority, SequenceWeight(animation, snapshots), ease);
                if (weight > 0) current.Add(sampler.Sample(time), weight);
            }
            foreach (var (_, target) in animation._snapshotTargets)
            {
                var current = nodes[target.Node];
                var weight = current.Weights.Normalize(target.Priority, SequenceWeight(animation, snapshots), ease);
                if (weight > 0) current.Add(target.Sampler.Sample(time), weight);
                nodes[target.Node] = current;
            }
            foreach (var binding in animation._scalars.Values)
            {
                var current = scalarBlend[binding.Target];
                var weight = current.Weights.Normalize(binding.Priority, SequenceWeight(animation, snapshots), ease);
                if (weight > 0) current.Add(binding.Sample(time), weight);
                scalarBlend[binding.Target] = current;
            }
        }
        foreach (var item in snapshots)
        {
            var snapshotOwner = item.Owner; var external = item.Targets; var scalars = item.Scalars;
            var snapshotWeight = item.SnapshotWeight; var snapshot = item.Samples;
            foreach (var (bone, priority, _) in snapshotOwner._transforms)
            {
                ref var current = ref blend.Bones[bone];
                var weight = current.Weights.Normalize(priority, snapshotWeight, 1);
                if (weight > 0) current.Add(snapshot![bone], weight);
            }
            foreach (var value in external!)
            {
                var node = snapshotOwner._snapshotTargets[value.Target].Node;
                var current = nodes[node]; var weight = current.Weights.Normalize(value.Priority, snapshotWeight, 1);
                if (weight > 0) current.Add(value.Local, weight);
                nodes[node] = current;
            }
            foreach (var value in scalars!)
            {
                var current = scalarBlend[value.Target]; var weight = current.Weights.Normalize(value.Priority, snapshotWeight, 1);
                if (weight > 0) current.Add(value.Value, weight);
                scalarBlend[value.Target] = current;
            }
        }
        blend.Publish(skeleton);
        foreach (var (node, current) in nodes)
        {
            if (current.PositionWeight > 0) node.Position = current.Position / current.PositionWeight * skeleton.UnitsToMetres;
            if (current.Hemisphere is not null)
                node.Quaternion = new Quaternion(current.Rotation.X, current.Rotation.Y, current.Rotation.Z, current.Rotation.W).Normalized();
            if (current.ScaleWeight > 0) node.Scale = Vector3.One * (current.Scale / current.ScaleWeight);
        }
        nodes.Clear();
        foreach (var current in scalarBlend.Values)
            if (current.Weight > 0) current.Binding.Publish(current.Value);
        scalarBlend.Clear();
        foreach (var (animation, time, ease) in layers)
            if (ease > 0 && animation.Sequence.Weight > 0)
                foreach (var apply in animation._otherChannels) apply(time);
    }
}
