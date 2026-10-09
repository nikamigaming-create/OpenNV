namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal uint StoreSceneClock(Guid main, long ordinal, uint delivered)
    {
        RequireIdle();
        if (main == Guid.Empty || ordinal < 1 || _clock?.Main == main ||
            _clock is { Step: not FalloutStandaloneSceneClockStep.Returned })
            throw new InvalidOperationException("Scene clock changed or replayed its actual Main child invocation.");
        var before = _bracket; _bracket = unchecked((byte)(_bracket + 1));
        _clock = new(main, ordinal, before, _bracket, delivered, null, Next(), _sequence,
            FalloutStandaloneSceneClockStep.Incremented, null);
        try
        {
            // This independently admitted source arm is exactly an identity
            // operation for finite Float32 inputs. It avoids inventing a host
            // rounding mode for original extended-precision nonunit division.
            var seconds = BitConverter.UInt32BitsToSingle(delivered);
            if (_clockKind == 4) throw new NotSupportedException("source-scene-clock-kind-four-selected-child-query-and-positive-scalar-unowned");
            if (_numerator != Content.FalloutStandalonePlayerSceneSource.One || _denominator != Content.FalloutStandalonePlayerSceneSource.One)
                throw new NotSupportedException("source-scene-clock-calling-thread-x87-nonunit-ratio-and-final-Float32-store-unowned");
            if (!float.IsFinite(seconds) || (delivered & 0x7f800000) == 0 && (delivered & 0x007fffff) != 0)
                throw new NotSupportedException("source-scene-clock-calling-thread-x87-nonfinite-or-denormal-status-and-store-unowned");
            _clock = _clock with { ArgumentBits = delivered, Step = FalloutStandaloneSceneClockStep.ArgumentStored, Changed = Next() };
            return delivered;
        }
        catch (Exception error) { RetainClockFailure(error); throw; }
    }
    internal uint EnterSceneVirtual(Guid main, long ordinal)
    {
        RequireIdle();
        if (_clock is not { Step: FalloutStandaloneSceneClockStep.ArgumentStored, ArgumentBits: { } argument } call ||
            call.Main != main || call.MainOrdinal != ordinal || call.Bracket != _bracket)
            throw new InvalidOperationException("Player virtual child lost its exact source clock argument/incremented bracket.");
        _clock = call with { Step = FalloutStandaloneSceneClockStep.VirtualEntered, Changed = Next() };
        return argument;
    }
    internal void RetainClockFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error); RetainFailure(error);
        if (_clock is not null) _clock = _clock with { Step = FalloutStandaloneSceneClockStep.Failed, Changed = _sequence, Failure = _failure };
        // The source decrement is after the virtual call returns. Finally or
        // session retirement must not pretend an unreturned child completed.
    }
}
