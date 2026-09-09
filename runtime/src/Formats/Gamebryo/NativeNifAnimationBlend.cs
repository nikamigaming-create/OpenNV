using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Reusable per-skeleton layer reduction; no channel lists or per-bone objects.</summary>
internal sealed class NativeNifAnimationBlend(int boneCount)
{
    internal struct Bone
    {
        internal bool Active;
        internal byte Priority;
        internal Vector3 Position;
        internal Vector4 Rotation;
        internal Quaternion? Hemisphere;
        internal float PositionWeight, Scale, ScaleWeight;

        internal void Add(FalloutNifAnimationSample sample, float weight)
        {
            if (sample.Translation is { } translation)
            {
                Position += GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * weight;
                PositionWeight += weight;
            }
            if (sample.Rotation is { } rotation)
            {
                var quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized();
                Hemisphere ??= quaternion;
                if (Hemisphere.Value.Dot(quaternion) < 0) quaternion = -quaternion;
                Rotation += new Vector4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W) * weight;
            }
            if (sample.Scale is { } scale) { Scale += scale * weight; ScaleWeight += weight; }
        }
    }

    internal Bone[] Bones { get; } = new Bone[boneCount];
    internal void Clear() => Array.Clear(Bones);
    internal void Publish(RuntimeNativeNifSkeleton skeleton)
    {
        for (var index = 0; index < Bones.Length; index++)
        {
            ref var bone = ref Bones[index];
            if (!bone.Active) continue;
            if (bone.PositionWeight > 0)
                skeleton.Node.SetBonePosePosition(index, bone.Position / bone.PositionWeight * skeleton.UnitsToMetres);
            if (bone.Hemisphere is not null)
                skeleton.Node.SetBonePoseRotation(index, new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W).Normalized());
            if (bone.ScaleWeight > 0) skeleton.Node.SetBonePoseScale(index, Vector3.One * (bone.Scale / bone.ScaleWeight));
        }
    }
}
