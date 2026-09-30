using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifControllerPlayer : Node
{
    private readonly Dictionary<string, RuntimeNifControllerSequence> _sequences =
        new(StringComparer.OrdinalIgnoreCase);
    private RuntimeNifControllerSequence? _active;
    private float[] _boundaries = [];
    private double _elapsedSeconds;
    private bool _includeStart;
    private long _generation;
    private long _textKeyCount;
    private object? _lastTextKey;
    private FalloutNifTextKeyTimeline? _textKeys;
    private readonly SortedSet<string> _unboundTextKeys = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> UnboundTextKeys => _unboundTextKeys;
    internal static Action<object>? TextKeyObserver { get; set; }
    internal Func<FalloutNifTextKeyEvent, string>? TextKeyHandler { get; set; }
    internal bool HasTextKeys => _sequences.Values.Any(sequence => sequence.TextKeys.Count != 0);
    internal bool CompletedDirectInitialization => _active is { CycleType: 2, DirectClock: not null } &&
        SourceTimeSeconds >= _active.StopTime && _sequences.Count == 1 && !HasTextKeys;
    internal long TextKeyCount => _textKeyCount;

    internal IReadOnlyCollection<string> SequenceNames => _sequences.Keys;
    internal string? ActiveSequence => _active?.Name;
    internal int SourceController { get; init; } = -1;
    internal string SourceSha256 { get; init; } = "";
    internal bool HasSequence(string name) => _sequences.ContainsKey(name);
    internal bool Playing => _active is not null && (_active.CycleType == 0 || SourceTimeSeconds < _active.StopTime);
    internal double FiniteEffectDuration
    {
        get
        {
            var sequence = _active ?? (_sequences.Count == 1 ? _sequences.Values.Single() :
                throw new NotSupportedException("Effect has no unique active source sequence."));
            var duration = (double)(sequence.StopTime - sequence.StartTime) / sequence.Frequency;
            return double.IsFinite(duration) && duration >= 0 ? duration :
                throw new InvalidDataException("Source effect duration is not finite.");
        }
    }
    internal double SourceTimeSeconds { get; private set; }
    internal double SecondsToBoundary
    {
        get
        {
            if (_active is null) return double.PositiveInfinity;
            foreach (var boundary in _boundaries)
                if (boundary > SourceTimeSeconds + 1e-10)
                    return (boundary - SourceTimeSeconds) / _active.Frequency;
            return _active.CycleType == 0 ? (_active.StopTime - SourceTimeSeconds) / _active.Frequency : double.PositiveInfinity;
        }
    }
    internal object Observation => new
    {
        active = ActiveSequence,
        playing = Playing,
        pending = _pendingSequence,
        awaitingSelection = _active is null && _sequences.Count > 1,
        sourceTimeSeconds = SourceTimeSeconds,
        elapsedSeconds = _elapsedSeconds,
        textKeyCount = _textKeyCount,
        lastTextKey = _lastTextKey,
        unboundTextKeys = _unboundTextKeys.ToArray(),
        sequences = _sequences.Values.Select(sequence => new
        {
            sequence.Name,
            sequence.CycleType,
            sequence.Frequency,
            sequence.StartTime,
            sequence.StopTime,
            channels = sequence.Channels.Count,
            textKeys = sequence.TextKeys
        }).ToArray(),
    };

    public override void _Ready()
    {
        if (_sequences.Count == 0)
            throw new InvalidOperationException("A NIF controller entered the scene without its C# source bindings.");
    }

    internal void Configure(IEnumerable<RuntimeNifControllerSequence> sequences)
    {
        if (_sequences.Count != 0)
            throw new InvalidOperationException("NIF controller player is already configured.");
        foreach (var sequence in sequences)
        {
            if (string.IsNullOrWhiteSpace(sequence.Name) || !float.IsFinite(sequence.StartTime) ||
                !float.IsFinite(sequence.StopTime) || sequence.StopTime <= sequence.StartTime ||
                !float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 || sequence.CycleType is not (0 or 2))
                throw new InvalidDataException("NIF source sequence clock is invalid.");
            if (!_sequences.TryAdd(sequence.Name, sequence))
                throw new InvalidDataException(
                    $"NIF controller manager has duplicate sequence name {sequence.Name}.");
        }
        var looping = _sequences.Values.Where(sequence => sequence.CycleType == 0 &&
            (sequence.DirectClock is null || (sequence.DirectClock.Flags & 8) != 0)).ToArray();
        // A manager can expose several alternative loops. Their cycle types
        // do not choose a group; a script/door/other source owner must do that.
        if (looping.Length == 1)
            PlaySourceSequence(looping[0].Name);
        else
            SetProcess(false);
    }

    internal void PlaySourceSequence(string name)
    {
        if (!_sequences.TryGetValue(name, out var sequence))
            throw new KeyNotFoundException($"NIF source sequence is not registered: {name}");
        _active = sequence;
        _pendingSequence = null;
        _boundaries = sequence.Channels.SelectMany(channel => channel.BoundaryTimes).Concat(sequence.TextKeys.Select(key => key.Time))
            .Where(time => time >= sequence.StartTime && time <= sequence.StopTime).Distinct().Order().ToArray();
        _textKeys = new(sequence.TextKeys, sequence.StartTime, sequence.StopTime, sequence.CycleType, sequence.Frequency);
        _elapsedSeconds = 0.0;
        _includeStart = true;
        _generation++;
        Apply(ResolveSourceTime(sequence, 0));
        SetProcess(true);
    }

    internal (float StartTime, float StopTime) SequenceRange(string name)
    {
        if (!_sequences.TryGetValue(name, out var sequence))
            throw new KeyNotFoundException($"NIF source sequence is not registered: {name}");
        return (sequence.StartTime, sequence.StopTime);
    }

    internal void SeekSourceTime(double sourceSeconds)
    {
        if (_active is null)
            throw new InvalidOperationException("NIF controller player has no active sequence.");
        if (!double.IsFinite(sourceSeconds))
            throw new ArgumentOutOfRangeException(nameof(sourceSeconds));
        _elapsedSeconds = _active.DirectClock is { } clock ? FalloutNifControllerClock.ElapsedAt(clock, sourceSeconds) : Math.Max(
            0.0,
            (sourceSeconds - _active.StartTime) / _active.Frequency);
        _includeStart = false;
        _generation++;
        Apply(ResolveSourceTime(_active, _elapsedSeconds));
    }

    public override void _Process(double delta)
    {
        if (_active is null)
            return;
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (_active.DirectClock is not null) { _ = Advance(delta); return; }
        do
        {
            var duration = ((double)_active.StopTime - _active.StartTime) / _active.Frequency;
            var boundary = _active.CycleType == 0 ? (Math.Floor(_elapsedSeconds / duration) + 1) * duration : duration;
            var cycleRemaining = Math.Max(0, boundary - _elapsedSeconds);
            var step = Math.Min(delta, Math.Min(SecondsToBoundary, cycleRemaining));
            var atCycleEnd = step >= cycleRemaining;
            if (!Advance(step, finishCycle: atCycleEnd && _pendingSequence is not null)) return;
            delta -= step;
            if (atCycleEnd && _pendingSequence is not null)
            {
                // A boundary callback can replace the queued request. An
                // immediate callback changes generation and already returned.
                PlaySourceSequence(_pendingSequence!);
                if (delta == 0) { _ = Advance(0); return; }
            }
            else if (delta == 0 || !Playing) return;
        }
        while (delta > 0);
    }

    private bool Advance(double delta, bool finishCycle = false)
    {
        var previous = _elapsedSeconds;
        _elapsedSeconds += delta;
        if (!double.IsFinite(_elapsedSeconds)) throw new InvalidDataException("NIF animation clock overflowed.");
        var sequence = _active!;
        var generation = _generation;
        Apply(finishCycle ? sequence.StopTime : ResolveSourceTime(sequence, _elapsedSeconds));
        if (_generation != generation) return false;
        var includeStart = _includeStart;
        _includeStart = false;
        if (sequence.TextKeys.Count != 0)
            foreach (var key in _textKeys!.Crossed(previous, _elapsedSeconds, includeStart))
            {
                if (_pendingSequence is not null && key.SourceSeconds == sequence.StartTime &&
                    key.Cycle > Math.Floor(previous * sequence.Frequency / ((double)sequence.StopTime - sequence.StartTime))) continue;
                var disposition = TextKeyHandler?.Invoke(key) ?? "unbound-runtime-event";
                if (disposition.Contains("unbound", StringComparison.Ordinal)) _unboundTextKeys.Add(key.Text);
                _lastTextKey = new { ordinal = ++_textKeyCount, sequence = sequence.Name, key, disposition };
                TextKeyObserver?.Invoke(new
                {
                    owner = GetParent().GetMeta("opennv_reference_form_key", "unbound").AsString(),
                    controller = Name.ToString(),
                    observation = _lastTextKey
                });
                if (_generation != generation) return false;
            }
        if (sequence.CycleType == 2 && SourceTimeSeconds >= sequence.StopTime)
            SetProcess(false);
        return true;
    }

    private void Apply(double sourceTime)
    {
        if (_active is null)
            return;
        SourceTimeSeconds = sourceTime;
        foreach (var channel in _active.Channels)
            channel.Apply((float)sourceTime);
    }

    private static double ResolveSourceTime(RuntimeNifControllerSequence sequence, double elapsed)
    {
        if (sequence.DirectClock is { } direct) return FalloutNifControllerClock.Resolve(direct, elapsed);
        var duration = (double)sequence.StopTime - sequence.StartTime;
        var scaled = elapsed * sequence.Frequency;
        if (sequence.CycleType == 0)
            return sequence.StartTime + scaled % duration;
        if (sequence.CycleType == 2)
            return Math.Min(sequence.StopTime, sequence.StartTime + scaled);
        throw new NotSupportedException(
            $"NIF source sequence {sequence.Name} uses unsupported reverse cycling.");
    }
}

internal sealed record RuntimeNifControllerSequence(
    string Name,
    uint CycleType,
    float Frequency,
    float StartTime,
    float StopTime,
    IReadOnlyList<RuntimeNifControllerChannel> Channels)
{
    internal IReadOnlyList<FalloutNifTextKey> TextKeys { get; init; } = [];
    internal FalloutNifTimeController? DirectClock { get; init; }
}

internal sealed class RuntimeNifControllerChannel
{
    private readonly Action<float> _apply;
    internal IReadOnlyList<float> BoundaryTimes { get; }

    internal RuntimeNifControllerChannel(Action<float> apply, IReadOnlyList<float>? boundaryTimes = null)
    { _apply = apply; BoundaryTimes = boundaryTimes ?? []; }

    internal void Apply(float sourceTime) => _apply(sourceTime);
}
