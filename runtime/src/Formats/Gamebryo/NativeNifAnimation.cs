using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record RuntimeNifUnboundAnimationChannel(FalloutNifControllerLink Source, string Reason);

internal sealed class RuntimeNativeNifAnimation
{
    private readonly RuntimeNativeNifSkeleton _skeleton;
    private readonly List<(int Bone, byte Priority, FalloutNifAnimationSampler Sampler)> _transforms = [];
    private readonly List<Action<float>> _otherChannels = [];
    private readonly List<RuntimeNifUnboundAnimationChannel> _unbound = [];
    private readonly List<FalloutNifControllerLink> _absentTargets = [];

    // The caller selects the source sequence and owns its clock. Construction
    // never starts playback, resets poses, or chooses a locomotion/idle package.
    internal RuntimeNativeNifAnimation(
        FalloutNifFile source,
        FalloutNifControllerSequence sequence,
        RuntimeNativeNifSkeleton skeleton,
        Func<FalloutNifControllerLink, Action<float>?>? bindOtherChannel = null,
        Action<FalloutNifAnimationSample>? accumulationRoot = null)
    {
        if (sequence.Weight != 1.0f)
            throw new NotSupportedException("Applying a weighted source sequence requires an animation blend owner.");
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
                if (link.ControllerType == "NiTransformController" &&
                    link.PropertyType.Length == 0 && link.Variable1.Length == 0 && link.Variable2.Length == 0)
                {
                    if (link.NodeName == sequence.TargetName && accumulationRoot is not null)
                    {
                        if (rootBound) throw new NotSupportedException("Multiple accumulation channels require a blend owner.");
                        rootBound = true;
                        var rootSampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                        _otherChannels.Add(time => accumulationRoot(rootSampler.Sample(time)));
                        continue;
                    }
                    if (!skeleton.TryBoneIndex(link.NodeName, out var bone))
                    {
                        if (bindOtherChannel?.Invoke(link) is { } applyObject)
                            _otherChannels.Add(applyObject);
                        else
                            _unbound.Add(new(link, "The source transform target has no skeleton or animation-object owner."));
                        continue;
                    }
                    var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                    if (!targets.Add(bone))
                        throw new NotSupportedException("Multiple transform links require a blend/priority owner.");
                    _transforms.Add((bone, link.Priority, sampler));
                }
                else if (link.ControllerType == "NiFloatExtraDataController")
                {
                    if (!floatTargets.Add((link.NodeName, link.Variable1)))
                        throw new NotSupportedException("Multiple float links require a blend/priority owner.");
                    _otherChannels.Add(skeleton.FloatExtraData.Bind(source, link));
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

    internal void ApplySourceTime(float sourceTime)
    {
        if (_unbound.Count != 0)
            throw new NotSupportedException($"Selected source sequence {Sequence.Name} has {_unbound.Count} unbound channels: " +
                string.Join("; ", _unbound.Select(item => $"{item.Source.NodeName}/{item.Source.ControllerType}: {item.Reason}")));
        ApplyTransformChannels(sourceTime);
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
    }

    internal static void ApplyLayers(params (RuntimeNativeNifAnimation Animation, float Time)[] layers)
    {
        if (layers.Length == 0) return;
        var skeleton = layers[0].Animation._skeleton;
        var selected = new Dictionary<int, (byte Priority, List<(FalloutNifAnimationSampler Sampler, float Time, float Weight)> Channels)>();
        foreach (var (animation, time) in layers)
        {
            if (animation._skeleton != skeleton || animation.UnboundChannels.Count != 0)
                throw new NotSupportedException("Animation layers require the same skeleton and complete source channel bindings.");
            if (!float.IsFinite(time) || time < animation.Sequence.StartTime || time > animation.Sequence.StopTime)
                throw new ArgumentOutOfRangeException(nameof(layers));
            foreach (var (bone, priority, sampler) in animation._transforms)
            {
                if (selected.TryGetValue(bone, out var current))
                {
                    if (priority < current.Priority) continue;
                    if (priority == current.Priority)
                    {
                        current.Channels.Add((sampler, time, animation.Sequence.Weight));
                        continue;
                    }
                }
                selected[bone] = (priority, [(sampler, time, animation.Sequence.Weight)]);
            }
        }
        foreach (var (bone, channel) in selected)
        {
            var position = Vector3.Zero;
            var positionWeight = 0.0f;
            var rotationSum = new Vector4();
            Quaternion? hemisphere = null;
            var scaleSum = 0.0f;
            var scaleWeight = 0.0f;
            foreach (var (sampler, time, weight) in channel.Channels)
            {
                if (!float.IsFinite(weight) || weight < 0) throw new InvalidDataException("Animation layer has an invalid source weight.");
                if (weight == 0) continue;
                var sample = sampler.Sample(time);
                if (sample.Translation is { } translation)
                {
                    position += GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * weight;
                    positionWeight += weight;
                }
                if (sample.Rotation is { } rotation)
                {
                    var quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized();
                    hemisphere ??= quaternion;
                    if (hemisphere.Value.Dot(quaternion) < 0) quaternion = -quaternion;
                    rotationSum += new Vector4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W) * weight;
                }
                if (sample.Scale is { } scale) { scaleSum += scale * weight; scaleWeight += weight; }
            }
            if (positionWeight > 0) skeleton.Node.SetBonePosePosition(bone, position / positionWeight * skeleton.UnitsToMetres);
            if (hemisphere is not null)
                skeleton.Node.SetBonePoseRotation(bone, new Quaternion(rotationSum.X, rotationSum.Y, rotationSum.Z, rotationSum.W).Normalized());
            if (scaleWeight > 0) skeleton.Node.SetBonePoseScale(bone, Vector3.One * (scaleSum / scaleWeight));
        }
        foreach (var (animation, time) in layers)
            foreach (var apply in animation._otherChannels) apply(time);
    }
}
