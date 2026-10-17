using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    internal FalloutIdleAnimationContext ReadIdleAnimationContext(FalloutSourceAnimationContextState context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Error is not null || !IsInsideTree() || IsQueuedForDeletion())
            throw new NotSupportedException("Player animation context lost its actual first-person animation factory.");
        return context.ReadIdleContext(FalloutLoadedAnimationRole.FirstPerson, SourceSceneFactory);
    }
}
