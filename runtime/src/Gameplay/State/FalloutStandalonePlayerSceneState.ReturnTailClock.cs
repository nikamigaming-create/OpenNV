using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private readonly List<FalloutPlayerAnimationBracketStore> _returnTailBracketStores = [];
    private string? _returnTailBracketWriter;
    private bool _returnTailBracketWriting;
    internal string? ReturnTailBracketSaveBlocker => _returnTailBracketWriter;
    internal FalloutPlayerAnimationBracketStore StoreReturnTailAnimationBracket(FalloutPlayerLateFrameInvocation frame,
        Guid consumerFactory, FalloutPlayerAnimationBracketChild child, bool enter,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual);
        if (_returnTailBracketWriting)
        { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Shared Scene bracket writer reentered its actual child."); }
        var fault = _reentry; _returnTailBracketWriting = true;
        try
        {
            Check();
            if (_returnTailBracketWriter is { } writer) throw new NotSupportedException(writer);
            if (_returnTailBracketStores.Count > 0 && _returnTailBracketStores[0].Frame.Caller != frame.Caller)
            {
                if (_returnTailBracketStores[^1].Enter || frame.Caller.Ordinal <= _returnTailBracketStores[0].Frame.Caller.Ordinal)
                    throw new InvalidOperationException("Animation bracket replayed a frame or crossed an unreturned child.");
                _returnTailBracketStores.Clear();
            }
            var previous = _returnTailBracketStores.LastOrDefault();
            if (enter ? previous is { Enter: true } || _returnTailBracketStores.Any(row => row.Child == child) :
                previous is not { Enter: true } || previous.Child != child || previous.ConsumerFactory != consumerFactory)
                throw new InvalidOperationException("Animation bracket changed actual enter/return order or source child.");
            var before = _bracket; _bracket = unchecked((byte)(_bracket + (enter ? 1 : -1)));
            var store = new FalloutPlayerAnimationBracketStore(frame, Source.Identity, _process, _constructor,
                consumerFactory, child, enter, Next(), before, _bracket);
            _returnTailBracketStores.Add(store); Check(); return store;
        }
        finally { _returnTailBracketWriting = false; }
        void Check()
        {
            requireActual(frame); RequireThread();
            if (_retired || _reentry != fault || consumerFactory == Guid.Empty || frame.PlayerProcess != _process ||
                frame.PlayerConstructor != _constructor || frame.ThreadId != Environment.CurrentManagedThreadId || !Enum.IsDefined(child) ||
                _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered } currentClock ||
                currentClock.Main != frame.Caller.Main || currentClock.MainOrdinal != frame.Caller.Ordinal)
                throw new InvalidOperationException("Animation bracket store lost its actual Scene and entered late frame.");
        }
    }
    internal void ObserveUnownedReturnTailBracketWriter(string producer, Action requireActualWriter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producer); ArgumentNullException.ThrowIfNull(requireActualWriter);
        if (_returnTailBracketWriting)
        { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Unknown Scene bracket writer reentered its actual child."); }
        RequireIdle(); requireActualWriter(); RequireThread();
        _returnTailBracketWriter = "source-Scene-shared-bracket-current-writer-unowned:" + producer; Next();
        requireActualWriter(); RequireThread();
    }
    internal IReadOnlyList<FalloutPlayerAnimationBracketStore> CaptureReturnTailBracketStores() => _returnTailBracketStores.ToArray();
    internal FalloutPlayerAnimationClockRead ReadReturnTailAnimationClock(FalloutPlayerLateFrameInvocation invocation,
        Guid tailFactory, FalloutSourceFrameTimerState actualTimer, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); requireActual(invocation); RequireThread(); ArgumentNullException.ThrowIfNull(actualTimer);
        if (_retired || tailFactory == Guid.Empty || invocation.PlayerProcess != _process ||
            invocation.PlayerConstructor != _constructor || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered } clock ||
            clock.Main != invocation.Caller.Main || clock.MainOrdinal != invocation.Caller.Ordinal ||
            clock.ArgumentBits != invocation.ArgumentBits)
            throw new InvalidOperationException("Animation clock lost its actual Main cached argument and entered Scene.");
        if (_clockKind == 4 || _numerator != FalloutStandalonePlayerSceneSource.One || _denominator != FalloutStandalonePlayerSceneSource.One)
            throw new NotSupportedException("source-Player-animation-current-clock-kind-four-or-nonunit-ratio-unowned");
        var timer = actualTimer.ReadScaledDelta(); requireActual(invocation); RequireThread();
        if (timer.Process != _process || timer.ScaledSecondsBits != invocation.ArgumentBits ||
            timer.Sequence != clock.CachedTimer?.Sequence || timer.Epoch != clock.CachedTimer?.Epoch)
            throw new InvalidDataException("Animation clock replaced its actual current Main timer interval.");
        // Unit ratio with finite normal Float32 delta preserves the actual
        // source value exactly; no independent animation clock is created.
        var delta = BitConverter.UInt32BitsToSingle(timer.ScaledSecondsBits);
        if (!float.IsFinite(delta) || (timer.ScaledSecondsBits & 0x7f800000) == 0 &&
            (timer.ScaledSecondsBits & 0x007fffff) != 0)
            throw new NotSupportedException("source-Player-animation-x87-nonfinite-or-denormal-delta-unowned");
        return new(invocation, _process, _constructor, _sequence, _clockKind, _numerator, _denominator, timer, timer.ScaledSecondsBits);
    }
}
