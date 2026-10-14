namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceActorAssignmentByteRead(string Source, string Stack, Guid Process,
    Guid Factory, Guid Constructor, OpenNV.Runtime.Gameplay.State.FalloutCombatActorIdentity Actor,
    long Sequence, byte Value);

internal sealed partial class FalloutSourceReferencePositionState
{
    internal FalloutReferenceActorAssignmentByteRead ReadActorAssignmentByte(FalloutReferencePositionInvocation invocation)
    {
        RequireRead(invocation); RequireLane(FalloutReferencePositionLane.ActorAssignmentByte);
        var read = new FalloutReferenceActorAssignmentByteRead(Source.Identity, Stack, Process, Factory,
            Constructor, Actor, _sequence, _values.ActorAssignmentByte);
        RequireRead(invocation); return read;
    }

    internal FalloutReferencePositionCall StoreActorAssignmentByteZero(FalloutReferencePositionInvocation invocation) =>
        Run(invocation, "raw-Actor-assignment-byte-zero-store", scope =>
        {
            if (invocation.Ingress != FalloutReferencePositionIngress.PackageCommand)
                throw new InvalidOperationException("Actor assignment byte store lost its original package caller.");
            Store(scope, FalloutReferencePositionStep.ActorAssignmentByte, 0);
            _writers.Remove(FalloutReferencePositionLane.ActorAssignmentByte);
        });
}
