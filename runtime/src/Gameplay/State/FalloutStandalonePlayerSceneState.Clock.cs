namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal uint StoreSceneClock(Guid main, long ordinal, Func<FalloutSourceFrameDeltaRead> readCachedTimer)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(readCachedTimer);
        if (main == Guid.Empty || ordinal < 1 || _clock?.Main == main ||
            _clock is { Step: not FalloutStandaloneSceneClockStep.Returned })
            throw new InvalidOperationException("Scene clock changed or replayed its actual Main child invocation.");
        _writing = true;
        var before = _bracket; _bracket = unchecked((byte)(_bracket + 1));
        _clock = new(main, ordinal, before, _bracket, null, null, Next(), _sequence,
            FalloutStandaloneSceneClockStep.Incremented, null);
        try
        {
            // This independently admitted source arm is exactly an identity
            // operation for finite Float32 inputs. It avoids inventing a host
            // rounding mode for original extended-precision nonunit division.
            if (_clockKind == 4) throw new NotSupportedException("source-scene-clock-kind-four-selected-child-query-and-positive-scalar-unowned");
            if (_numerator != Content.FalloutStandalonePlayerSceneSource.One || _denominator != Content.FalloutStandalonePlayerSceneSource.One)
                throw new NotSupportedException("source-scene-clock-calling-thread-x87-nonunit-ratio-and-final-Float32-store-unowned");
            var fault = _reentry;
            var cached = readCachedTimer();
            RequireThread();
            if (_reentry != fault || cached is null || cached.Source != Source.Main.EngineSha256 ||
                cached.Process != _process || cached.Epoch < 1 || cached.Sequence < 0)
                throw new InvalidOperationException("Player scene clock changed its actual cached Main timer, thread or source process.");
            var bits = cached.ScaledSecondsBits;
            _clock = _clock with { CachedTimer = cached, CachedScaledBits = bits, Changed = Next() };
            var seconds = BitConverter.UInt32BitsToSingle(bits);
            if (!float.IsFinite(seconds) || (bits & 0x7f800000) == 0 && (bits & 0x007fffff) != 0)
                throw new NotSupportedException("source-scene-clock-calling-thread-x87-nonfinite-or-denormal-status-and-store-unowned");
            _clock = _clock with { ArgumentBits = bits, Step = FalloutStandaloneSceneClockStep.ArgumentStored, Changed = Next() };
            return bits;
        }
        catch (Exception error) { RetainClockFailure(error); throw; }
        finally { _writing = false; }
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
