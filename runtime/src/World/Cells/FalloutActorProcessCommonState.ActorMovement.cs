using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal void RequireControllerActorMovement(FalloutControllerActorMovementInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); var frame = invocation.Parent.Frame;
        var entry = Require(frame.Common.Actor.Reference);
        if (_busy || frame.Common.Process != _process || frame.Common.Stack != _stack || entry.Source != frame.Common.Actor ||
            entry.Epoch != frame.Common.Epoch || entry.Gameplay?.Ownership != frame.Common.GameplayOwnership ||
            entry.Gameplay is not { Retired: false } || !_earlyControllerFields.TryGetValue(entry.Source.Reference, out var field) ||
            field.Lease is null || field.Lease.Controller.Identity != invocation.Context.Context ||
            field.Lease.Controller.SourceProcess != invocation.RuntimeProcess || invocation.Context.Process.Actor != entry.Source ||
            invocation.Context.Process.Process != _process || invocation.Context.Process.Epoch != entry.Epoch ||
            invocation.Context.Process.GameplayOwnership != frame.Common.GameplayOwnership)
            throw new InvalidDataException("Actor movement changed its actual current Common retained controller pointer.");
        // Pointer association only: the actual controller is allowed to be
        // busy inside its own child. Do not reenter its field/lifetime getter.
    }
}
