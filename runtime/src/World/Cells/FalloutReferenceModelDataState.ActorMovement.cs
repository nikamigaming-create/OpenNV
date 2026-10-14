using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceModelDataState
{
    internal void RequireActorMovementData(FalloutReferenceModelDataRead borrowed,
        FalloutPlayerLateFrameInvocation invocation, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        var current = ReadForMover(invocation, requireActual);
        if (borrowed != current || current.Data is null)
            throw new InvalidDataException("Actor movement crossed a source data/model writer or borrowed another current reference object.");
    }
}
