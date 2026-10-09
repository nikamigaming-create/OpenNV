using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private IDisposable? _sourceProcessBodyLease;
    private void BindActualSourceProcessBody()
    {
        if (_sourceProcessBodyLease is not null) throw new InvalidOperationException("Actor source process body is already leased.");
        _sourceProcessBodyLease = _world.BindActualNativeProcessBody(_state.Reference, _skeletonPath,
            _skeleton.Source, _world.BodyParts(_state.Reference), PerceptionOwner, () =>
                GodotObject.IsInstanceValid(_actor) && _actor.IsInsideTree() && !_actor.IsQueuedForDeletion() &&
                GodotObject.IsInstanceValid(_skeleton.Node) && _skeleton.Node.IsInsideTree() && !_skeleton.Node.IsQueuedForDeletion());
    }
    private void RetireActualSourceProcessBody()
    {
        var lease = _sourceProcessBodyLease;
        lease?.Dispose(); _sourceProcessBodyLease = null;
    }
}
