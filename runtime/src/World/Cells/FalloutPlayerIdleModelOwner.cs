using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal interface IFalloutPlayerIdleModelOwner
{
    Guid Process { get; }
    string Stack { get; }
    string Source { get; }
    void RequireCurrent();
    FalloutIdleAnimationContext ReadFirstPersonAnimation();
    uint? ReadBoundRadius(FalloutSourceSceneNode model);
}
