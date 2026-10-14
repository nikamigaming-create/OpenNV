using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Formats.Gamebryo;

// A reusable source-space reducer. GPU/native skeleton fields are consumers.
// Missing components remain absent and the scene's preceding local fields survive.
internal sealed class FalloutNifSourcePoseBlend(int count)
{
    private struct Bone
    {
        internal bool Active;
        internal byte Priority;
        internal float X, Y, Z, PositionWeight, Scale, ScaleWeight;
        internal float Qx, Qy, Qz, Qw;
        internal FalloutNifQuaternion? Hemisphere;
    }
    private readonly Bone[] _bones = new Bone[count];
    internal void Clear() => Array.Clear(_bones);
    internal void Select(int bone, byte priority)
    {
        ref var current = ref _bones[bone];
        if (!current.Active || priority > current.Priority) current.Priority = priority;
        current.Active = true;
    }
    internal void Add(int bone, byte priority, float weight, FalloutNifAnimationSample sample)
    {
        if (!float.IsFinite(weight) || weight < 0) throw new InvalidDataException("Source pose layer has an invalid weight.");
        ref var current = ref _bones[bone];
        if (priority != current.Priority || weight == 0) return;
        if (sample.Translation is { } position)
        {
            current.X = F.Add(current.X, F.Multiply(position.X, weight)); current.Y = F.Add(current.Y, F.Multiply(position.Y, weight));
            current.Z = F.Add(current.Z, F.Multiply(position.Z, weight)); current.PositionWeight = F.Add(current.PositionWeight, weight);
        }
        if (sample.Rotation is { } value)
        {
            var normalized = Normalize(value);
            current.Hemisphere ??= normalized;
            var first = current.Hemisphere.Value;
            var dot = F.Add(F.Add(F.Add(F.Multiply(first.X, normalized.X), F.Multiply(first.Y, normalized.Y)),
                F.Multiply(first.Z, normalized.Z)), F.Multiply(first.W, normalized.W));
            if (dot < 0) normalized = new(-normalized.W, -normalized.X, -normalized.Y, -normalized.Z);
            current.Qx = F.Add(current.Qx, F.Multiply(normalized.X, weight)); current.Qy = F.Add(current.Qy, F.Multiply(normalized.Y, weight));
            current.Qz = F.Add(current.Qz, F.Multiply(normalized.Z, weight)); current.Qw = F.Add(current.Qw, F.Multiply(normalized.W, weight));
        }
        if (sample.Scale is { } scale)
        {
            if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Source pose layer has a nonpositive uniform scale.");
            current.Scale = F.Add(current.Scale, F.Multiply(scale, weight)); current.ScaleWeight = F.Add(current.ScaleWeight, weight);
        }
    }
    internal void Publish(Action<int, FalloutNifAnimationSample> publish)
    {
        for (var bone = 0; bone < _bones.Length; bone++)
        {
            ref var current = ref _bones[bone]; if (!current.Active) continue;
            FalloutNifVector3? position = current.PositionWeight > 0 ? new(F.Divide(current.X, current.PositionWeight),
                F.Divide(current.Y, current.PositionWeight), F.Divide(current.Z, current.PositionWeight)) : null;
            FalloutNifQuaternion? rotation = current.Hemisphere is not null ? Normalize(new(current.Qw, current.Qx, current.Qy, current.Qz)) : null;
            float? scale = current.ScaleWeight > 0 ? F.Divide(current.Scale, current.ScaleWeight) : null;
            publish(bone, new(position, rotation, scale));
        }
    }
    private static FalloutNifQuaternion Normalize(FalloutNifQuaternion value)
    {
        var length = F.Add(F.Add(F.Add(F.Multiply(value.X, value.X), F.Multiply(value.Y, value.Y)), F.Multiply(value.Z, value.Z)), F.Multiply(value.W, value.W));
        if (!float.IsFinite(length) || length <= 0) throw new InvalidDataException("Source pose layer has an invalid quaternion magnitude.");
        var scale = F.Divide(1, (float)Math.Sqrt(length));
        return new(F.Multiply(value.W, scale), F.Multiply(value.X, scale), F.Multiply(value.Y, scale), F.Multiply(value.Z, scale));
    }
}
