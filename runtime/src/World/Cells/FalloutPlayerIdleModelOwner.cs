using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal interface IFalloutPlayerIdleModelOwner
{
    Guid Process { get; }
    string Stack { get; }
    string Source { get; }
    void RequireCurrent();
    FalloutIdleAnimationContext ReadFirstPersonAnimation(FalloutSourceAnimationContextState context);
    uint? ReadBoundRadius(FalloutSourceSceneNode model);
}
