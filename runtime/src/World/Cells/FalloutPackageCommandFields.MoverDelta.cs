using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal FalloutPlayerMoverDeltaFirstAnimationRead ReadMoverDeltaFirstAnimation(
        FalloutPlayerLateFrameInvocation invocation, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); var fault = _fault; Check();
        RequireLane(FalloutPackageCommandFieldLane.Animation);
        var value = _values.FirstPersonAnimation; Check();
        return new(Source.Identity, Stack, Process, Factory, Constructor, Actor, Epoch, _sequence, value);
        void Check()
        {
            if (_busy) { ++_fault; throw new InvalidOperationException("First animation pointer reentered its actual field owner."); }
            requireActual(invocation);
            if (_retired || _fault != fault || invocation.Common.Actor != Actor || invocation.Common.Process != Process ||
                invocation.Common.Epoch != Epoch || invocation.PlayerConstructor != Constructor ||
                invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId)
                throw new InvalidOperationException("Mover delta first animation changed its actual source/field/epoch/thread.");
        }
    }
}
