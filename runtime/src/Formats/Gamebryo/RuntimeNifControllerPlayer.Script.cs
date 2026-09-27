using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifControllerPlayer
{
    private string? _pendingSequence;
    private bool _scriptSelected;

    internal void RequestSourceSequence(string name, int initialization)
    {
        if (!_sequences.TryGetValue(name, out var sequence)) throw new KeyNotFoundException($"Unknown source animation group {name}.");
        if (SourceController < 0 || sequence.DirectClock is not null)
            throw new NotSupportedException("PlayGroup requires a managed object animation sequence.");
        if (initialization is < 0 or > 2) throw new InvalidDataException("PlayGroup initialization must be 0, 1 or 2.");
        var start = sequence.StartTime;
        if (initialization == 2)
        {
            var loops = sequence.TextKeys.Where(key => key.Value.Trim().Equals("loop start", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (loops.Length > 1 || loops.Length == 0 && sequence.CycleType != 0)
                throw new NotSupportedException("Immediate-loop animation needs a unique authored loop start.");
            if (loops.Length == 1) start = loops[0].Time;
            if (start < sequence.StartTime || start >= sequence.StopTime)
                throw new InvalidDataException("Animation loop start is outside its sequence.");
        }
        _scriptSelected = true;
        if (initialization == 0 && Playing)
        {
            if (_pendingSequence != name) { _pendingSequence = name; _generation++; }
            return;
        }
        PlaySourceSequence(name);
        if (start != sequence.StartTime)
        {
            _elapsedSeconds = (start - sequence.StartTime) / sequence.Frequency;
            Apply(start);
        }
    }

    internal FalloutObjectAnimationSnapshot? CaptureScriptState() => !_scriptSelected || _active is null ? null :
        new(SourceController, SourceSha256, _active.Name, _elapsedSeconds, _includeStart, _pendingSequence);

    internal void RestoreScriptState(FalloutObjectAnimationSnapshot state)
    {
        state.Validate();
        if (state.Controller != SourceController || !state.Sha256.Equals(SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            !_sequences.ContainsKey(state.Sequence) || state.PendingSequence is { } pending && !_sequences.ContainsKey(pending))
            throw new NotSupportedException("Saved object animation differs from the winning model.");
        PlaySourceSequence(state.Sequence);
        _scriptSelected = true;
        _elapsedSeconds = state.ElapsedSeconds;
        _includeStart = state.StartPending;
        _pendingSequence = state.PendingSequence;
        Apply(ResolveSourceTime(_active!, _elapsedSeconds));
        SetProcess(Playing || _includeStart || _pendingSequence is not null);
    }
}
