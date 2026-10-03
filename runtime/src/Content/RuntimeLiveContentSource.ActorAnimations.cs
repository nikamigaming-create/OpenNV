namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    private FalloutActorAnimationResources? _actorAnimations;
    internal FalloutActorAnimationResources ActorAnimations => _actorAnimations ??= new(this);
}
