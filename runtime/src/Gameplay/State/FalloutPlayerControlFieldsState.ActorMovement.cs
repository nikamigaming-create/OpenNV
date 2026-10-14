namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerControlFieldsState
{
    internal FalloutPlayerFrameDisabledRead ReadActorMovementDisabledWord(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); requireActual(invocation);
        var word = ReadDisabledWord();
        if (invocation.PlayerProcess != _process || invocation.Common.Stack != _stack)
            throw new InvalidOperationException("Actor movement borrowed another actual Player disable-word owner.");
        requireActual(invocation); return new(_source.Identity, _stack, _process, _factory, _sequence, word);
    }
}
