using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private IDisposable? _perceptionNativeLease;
    private string PerceptionOwner => "actual-actor-source-body/" + _state.Reference + "/" + _skeletonPath + "/" + _skeleton.Source.Sha256;

    private void BindActorPerception()
    {
        if (_perceptionNativeLease is not null) throw new InvalidOperationException("Actor source perception is already published.");
        if (!_world.ActorPerceptionConfigured) throw new NotSupportedException("Actor has no original perception world owner.");
        BindActualSourceProcessBody();
        _perceptionNativeLease = _world.BindNativePerception(_state.Reference, PerceptionOwner, ObserveSourcePerception);
    }
    private FalloutPerceptionNativeObservation ObserveSourcePerception(long lease)
    {
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.IsInsideTree() || _actor.IsQueuedForDeletion() ||
            !GodotObject.IsInstanceValid(_skeleton.Node) || !_skeleton.Node.IsInsideTree())
            throw new NotSupportedException("Perception source actor/skeleton publication has retired.");
        var placement = CaptureSpatialPlacement(); placement.Validate();
        var moving = _actor is CharacterBody3D body && !body.Velocity.IsZeroApprox();
        return new(_state.Reference, PerceptionOwner, lease, placement.Cell, placement.Position.ToArray(), placement.RotationRadians[2],
            true, Activity.InCombat, _world.IsInCombat(_state.Reference), Activity.Sneaking, Activity.Running,
            moving, Dead);
    }
    internal NativeDetectionSightPoints ReadSourceDetectionSight() => NativeDetectionSight.Read(_world.BodyParts(_state.Reference), _skeleton);
    private void RetireActorPerceptionBinding()
    {
        var failures = new List<Exception>();
        try { _perceptionNativeLease?.Dispose(); _perceptionNativeLease = null; } catch (Exception error) { failures.Add(error); }
        try { RetireActualSourceProcessBody(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Actor perception/process-body retirement retains actual failures.", failures);
    }
}
