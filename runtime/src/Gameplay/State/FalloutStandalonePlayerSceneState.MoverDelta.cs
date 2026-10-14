namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal FalloutPlayerMoverDeltaViewRead ReadMoverDeltaView(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); requireActual(invocation); RequireThread();
        if (!_writing || _retired || invocation.PlayerProcess != _process || invocation.PlayerConstructor != _constructor ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || _prelude is not { } prelude ||
            prelude.Main != invocation.Caller.Main || prelude.MainOrdinal != invocation.Caller.Ordinal || prelude.Invocation != invocation.Prelude)
            throw new InvalidOperationException("Mover delta view lost its actual entered Scene/Main/Prelude scope.");
        var value = new FalloutPlayerMoverDeltaViewRead(Source.Identity, _stack, _process, _constructor, invocation.Caller, _sequence, _view);
        requireActual(invocation); return value;
    }
}
