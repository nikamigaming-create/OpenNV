namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandaloneInterfaceState
{
    internal FalloutPlayerMoverDeltaMenuRead ReadMoverDeltaDialogPredicate(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); requireActual(invocation);
        var value = Read(FalloutStandaloneInterfaceQuery.FirstPredicate) != 0;
        var result = new FalloutPlayerMoverDeltaMenuRead(Source.Identity, Stack, Process, Constructor,
            _sequence, _manager?.Identity, _enabled, _dialogByte, value);
        requireActual(invocation); RequireCurrent();
        if (invocation.ThreadId != Environment.CurrentManagedThreadId || invocation.Common.ExecutableSha256 != Source.Main.EngineSha256)
            throw new InvalidOperationException("Mover delta menu predicate lost its actual Interface/source/current thread.");
        return result;
    }
}
