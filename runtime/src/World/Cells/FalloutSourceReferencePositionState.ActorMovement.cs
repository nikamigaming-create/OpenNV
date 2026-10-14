using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceReferencePositionState
{
    internal FalloutReferenceFormKindRead ReadActorMovementFormKind(FalloutReferencePositionInvocation invocation)
    {
        RequireRead(invocation); RequireLane(FalloutReferencePositionLane.ReferenceKind);
        var read = new FalloutReferenceFormKindRead(Source.Identity, Stack, Process, Factory, Constructor,
            Actor, _sequence, _values.ReferenceKind);
        RequireRead(invocation); return read;
    }
    internal FalloutReferenceRawPositionRead ReadActorMovementRawPosition(FalloutReferencePositionInvocation invocation)
    {
        RequireRead(invocation); RequireLane(FalloutReferencePositionLane.Position);
        var read = new FalloutReferenceRawPositionRead(Source.Identity, Stack, Process, Factory, Constructor,
            Actor, _sequence, _values.Position);
        RequireRead(invocation); return read;
    }
}
