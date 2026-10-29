using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private readonly Dictionary<string, FalloutNifFile> _boundSources = new(StringComparer.OrdinalIgnoreCase);
    internal FalloutNifSphereBound CurrentWorldBound(RuntimeLiveContentSource content)
        => NativeActorPosedBounds.Current(Parts, Skeleton, content, _boundSources);
    internal FalloutNifSphereBound? CurrentBoneWorldBound(RuntimeLiveContentSource content, int bone)
        => NativeActorPosedBounds.CurrentBone(Parts, Skeleton, content, _boundSources, bone);
}
