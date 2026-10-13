using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private readonly List<FalloutNifFile> _sourceBoundParts = [];

    internal uint? ReadSourcePoseBoundRadius()
    {
        if (Error is not null || !IsInsideTree() || IsQueuedForDeletion() ||
            !GodotObject.IsInstanceValid(Skeleton.Node) || !Skeleton.Node.IsInsideTree() || Skeleton.Node.IsQueuedForDeletion())
            throw new NotSupportedException("Player bound lost its actual assembled skeleton and current pose.");
        FalloutNifSphereBound? bound = null;
        foreach (var part in _sourceBoundParts)
            if (FalloutNifPoseBounds.Read(part, ReadPose) is { } next)
                bound = bound?.Merge(next) ?? next;
        return bound is { } current ? BitConverter.SingleToUInt32Bits(current.Radius) : null;
        FalloutNifTransform ReadPose(string bone)
        {
            var pose = Actor.Transform * Skeleton.Node.Transform * Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex(bone));
            var scale = pose.Basis.Scale.Abs();
            if (!pose.IsFinite() || scale.X <= 0 || MathF.Abs(scale.X - scale.Y) > scale.X * .00001f ||
                MathF.Abs(scale.X - scale.Z) > scale.X * .00001f)
                throw new NotSupportedException("Player posed bounds require the source uniform bone scale.");
            var axes = new Basis(Vector3.Right, Vector3.Forward, Vector3.Up);
            var unit = new Basis(pose.Basis.X / scale.X, pose.Basis.Y / scale.X, pose.Basis.Z / scale.X);
            var rotation = axes.Transposed() * unit * axes;
            var position = pose.Origin / Skeleton.UnitsToMetres;
            return new(new(position.X, -position.Z, position.Y),
                [rotation.X.X, rotation.Y.X, rotation.Z.X, rotation.X.Y, rotation.Y.Y, rotation.Z.Y,
                    rotation.X.Z, rotation.Y.Z, rotation.Z.Z], scale.X);
        }
    }
}
