using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private readonly Dictionary<string, FalloutNifFile> _boundSources = new(StringComparer.OrdinalIgnoreCase);

    internal float SourceHeight
    {
        get
        {
            var source = Skeleton.Source;
            var bounds = source.Roots.Select(source.ReadNode).SelectMany(node => node.ExtraData)
                .Where(index => index >= 0).Select(source.ReadObject).OfType<FalloutNifBound>()
                .Where(bound => bound.Name == "BBX").ToArray();
            if (bounds.Length != 1 || !float.IsFinite(bounds[0].Dimensions.Z) || bounds[0].Dimensions.Z <= 0)
                throw new NotSupportedException("Actor height requires one authored BBX bound.");
            return bounds[0].Dimensions.Z * 2 * Appearance.Height;
        }
    }

    // NiSkinData stores each bone's bound in bone-local space. Recompute from
    // the current shared pose; neither source bind pose nor a fitted actor
    // radius can replace these bounds after animation.
    internal FalloutNifSphereBound CurrentWorldBound(RuntimeLiveContentSource content)
        => NativeActorPosedBounds.Current(Parts, Skeleton, content, _boundSources);
    internal FalloutNifSphereBound? CurrentBoneWorldBound(RuntimeLiveContentSource content, int bone)
        => NativeActorPosedBounds.CurrentBone(Parts, Skeleton, content, _boundSources, bone);
    internal FalloutNifSphereBound? CurrentMeshWorldBound(RuntimeLiveContentSource content, MeshInstance3D mesh)
        => NativeActorPosedBounds.CurrentMesh(Parts, Skeleton, content, _boundSources, mesh);
}
