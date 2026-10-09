using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// One package owns one ordered candidate registry and its cached timer epoch.
// Discovery and interval draws retain their entered prefixes even on failure.
internal sealed class FalloutSandboxActionRegistry
{
    private FalloutSandboxRegistrySnapshot? _state;
    private Exception? _failure;
    internal FalloutSandboxActionRegistry(FalloutSandboxRegistrySnapshot? saved = null)
    {
        saved?.Validate(); _state = saved?.Copy();
    }

    internal FalloutSandboxDiscovery Observe(FalloutSandboxTimerSample clock,
        (uint Minimum, uint Maximum) interval, Func<uint, uint> random,
        Func<FalloutSandboxDiscovery> discover, uint repeatMilliseconds)
    {
        clock.Validate(); RequireHealthy();
        if (interval.Maximum < interval.Minimum || interval.Maximum - interval.Minimum == uint.MaxValue)
            throw new InvalidDataException("Sandbox inclusive source rescan interval is invalid.");
        _state ??= new(clock.SourceSha256, clock.Epoch, clock.Milliseconds, 0, 0, null, false, null, null);
        if (_state.ClockSourceSha256 != clock.SourceSha256 || _state.Epoch != clock.Epoch)
            throw new NotSupportedException("Sandbox registry has no same-clock cold epoch restoration; deadlines cannot be relabelled.");
        _state = _state with { LastMilliseconds = clock.Milliseconds };
        if (_state.PreviousReference is { } previous && previous != _state.RepeatedReference)
            _state = _state with { RepeatedReference = previous, RepeatUntil = unchecked(clock.Milliseconds + repeatMilliseconds) };
        _state = _state with { PreviousReference = null };
        // Preserve the original independent unsigned comparison boundaries.
        if (_state.RepeatedReference is not null && clock.Milliseconds > _state.RepeatUntil)
            _state = _state with { RepeatedReference = null };
        if (_state.Registry is null || clock.Milliseconds >= _state.RescanAt)
        {
            _state = _state with { ScanEntered = true, Registry = null };
            try
            {
                var rebuilt = discover(); rebuilt.Validate();
                _state = _state with { Registry = rebuilt.Copy() };
                var width = interval.Maximum - interval.Minimum + 1;
                var draw = random(width);
                if (draw >= width) throw new InvalidDataException("Sandbox rescan RNG exceeded its inclusive source interval.");
                _state = _state with { RescanAt = unchecked(clock.Milliseconds + interval.Minimum + draw), ScanEntered = false };
            }
            catch (Exception failure) { RetainFailure(failure); throw; }
        }
        return _state.Registry!.Copy();
    }

    internal void RememberReturned(FalloutSandboxCandidate selected)
    {
        RequireHealthy(); selected.Validate();
        if (_state is null) throw new InvalidOperationException("Sandbox selected-reference return has no actual registry.");
        if (_state.PreviousReference is not null) throw new InvalidOperationException("Sandbox returned selection already awaits its next source election.");
        _state = _state with { PreviousReference = selected.Reference };
    }

    internal FalloutFormKey? RepeatedReference => _state?.RepeatedReference;
    internal FalloutSandboxRegistrySnapshot? Capture() { var result = _state?.Copy(); result?.Validate(); return result; }
    internal void RetainFailure(Exception failure)
    {
        _failure ??= failure;
        if (_state is not null) _state = _state with
        { Failure = _state.Failure ?? (string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message) };
    }
    private void RequireHealthy()
    {
        if (_failure is not null || _state?.Failure is not null)
            throw new InvalidOperationException("Sandbox registry retains an entered failed source suffix: " + (_state?.Failure ?? _failure!.Message), _failure);
    }
}
