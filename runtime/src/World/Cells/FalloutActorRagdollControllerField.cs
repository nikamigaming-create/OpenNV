using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorRagdollControllerFieldSnapshot(Guid Process, Guid Constructor,
    FalloutCombatActorIdentity Actor, Guid? Controller, FalloutReferenceModelDataInvocation? Writer, long Stores);

internal sealed class FalloutActorRagdollControllerField
{
    private readonly Guid _process, _constructor;
    private readonly FalloutCombatActorIdentity _actor;
    private FalloutLivingRagdollController? _current;
    private FalloutReferenceModelDataInvocation? _writer;
    private long _stores;

    internal FalloutActorRagdollControllerField(Guid process, Guid constructor, FalloutCombatActorIdentity actor)
    {
        actor.Validate();
        if (process == Guid.Empty || constructor == Guid.Empty)
            throw new InvalidDataException("Actor ragdoll field requires its actual independent constructor writer.");
        _process = process; _constructor = constructor; _actor = actor;
        // The Actor initializer writes this field null independently of the
        // Reference model field and of the controller in the Common process.
        _current = null;
    }
    internal FalloutLivingRagdollController? Read(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        Check(load, requireActual); _current?.RequireReturned(); return _current;
    }
    internal void Publish(FalloutReferenceModelDataInvocation load, FalloutLivingRagdollController controller,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        Check(load, requireActual); controller.RequireReturned();
        if (_current is not null || _stores != 0 || controller.Capture().Load != load)
            throw new InvalidOperationException("Actor ragdoll publication replayed or borrowed another model-load controller.");
        _current = controller; _writer = load; ++_stores;
        Check(load, requireActual);
    }
    internal FalloutActorRagdollControllerFieldSnapshot Capture() =>
        new(_process, _constructor, _actor, _current?.Identity, _writer, _stores);
    internal void OnAnimationNote(FalloutAnimationNote note)
    {
        if (_current is null) return;
        _current.RequireReturned();
        throw new NotSupportedException("source-ragdoll-arm-animation-note-current-field-consumer-unowned");
    }
    private void Check(FalloutReferenceModelDataInvocation load, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        requireActual(load);
        if (load.ReferenceProcess != _process || load.ReferenceConstructor != _constructor || load.Actor != _actor)
            throw new InvalidOperationException("Actor ragdoll field left its actual constructor, reference or load scope.");
    }
}
