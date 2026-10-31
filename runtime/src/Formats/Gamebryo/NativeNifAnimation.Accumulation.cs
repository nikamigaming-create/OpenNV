using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNativeNifAnimation
{
    private FalloutNifAnimationSampler? _accumulationSampler;
    private byte _accumulationPriority;
    internal bool HasAccumulationController => _accumulationSampler is not null;
    internal float? AccumulationTime { get; private set; }

    internal void RestoreAccumulationTime(float? time)
    {
        if (time is { } value && (_accumulationSampler is null || !float.IsFinite(value) ||
            value < Sequence.StartTime || value > Sequence.StopTime))
            throw new InvalidDataException("Saved accumulation phase differs from its source controller.");
        AccumulationTime = time;
    }

    private Vector3 AccumulationPosition(float time)
    {
        var sample = _accumulationSampler!.Sample(time);
        if (sample.Scale is { } scale && scale != 1 || sample.Rotation is { } rotation &&
            (MathF.Abs(rotation.W) < .99999f || MathF.Abs(rotation.X) + MathF.Abs(rotation.Y) + MathF.Abs(rotation.Z) > .0001f))
            throw new NotSupportedException("Actor accumulation rotation/scale requires its motion extraction owner.");
        return sample.Translation is { } point ? GamebryoCoordinate.ConvertVector(new(point.X, point.Y, point.Z)) : Vector3.Zero;
    }

    private Vector3 ConsumeAccumulation(float time)
    {
        if (!float.IsFinite(time) || time < Sequence.StartTime || time > Sequence.StopTime)
            throw new InvalidDataException("Accumulation sample is outside its source sequence.");
        var previous = AccumulationTime;
        // Native accumulation retains the last contributing source sample.
        // A new manager binding aligns at its first sample, not file time zero.
        AccumulationTime = time;
        if (previous is null || time == previous.Value) return Vector3.Zero;
        var result = AccumulationPosition(time) - AccumulationPosition(previous.Value);
        if (time < previous.Value)
            result += AccumulationPosition(Sequence.StopTime) - AccumulationPosition(Sequence.StartTime);
        return result;
    }

    internal static Vector3 ConsumeAccumulationLayers(
        ReadOnlySpan<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers,
        ReadOnlySpan<ControllerSnapshot> snapshots)
    {
        var weights = new FalloutNifBlendWeights();
        foreach (var (animation, _, _) in layers)
            if (animation._accumulationSampler is not null) weights.Attach(animation._accumulationPriority);
        foreach (var snapshot in snapshots)
            if (snapshot.Owner._accumulationSampler is not null) weights.Attach(snapshot.Owner._accumulationPriority);
        static float Weight(RuntimeNativeNifAnimation animation, ReadOnlySpan<ControllerSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots)
                if (ReferenceEquals(snapshot.Owner, animation)) return animation.Sequence.Weight * snapshot.IncomingWeight;
            return animation.Sequence.Weight;
        }
        foreach (var (animation, _, ease) in layers)
            if (animation._accumulationSampler is not null)
                weights.Gather(animation._accumulationPriority, Weight(animation, snapshots), ease);
        foreach (var snapshot in snapshots)
            if (snapshot.Owner._accumulationSampler is not null)
                weights.Gather(snapshot.Owner._accumulationPriority, snapshot.Owner.Sequence.Weight * snapshot.SnapshotWeight, 1);
        var motion = Vector3.Zero;
        foreach (var (animation, time, ease) in layers)
        {
            if (animation._accumulationSampler is null) continue;
            var weight = weights.Normalize(animation._accumulationPriority, Weight(animation, snapshots), ease);
            if (weight > 0) motion += animation.ConsumeAccumulation(time) * weight;
            // The actual accumulation node is extracted into the body, never
            // published again as a second visual root translation.
            var root = animation._skeleton.BoneIndex(animation.Sequence.TargetName);
            if (root >= 0) animation._skeleton.Node.SetBonePosePosition(root, Vector3.Zero);
        }
        return motion;
    }
}
