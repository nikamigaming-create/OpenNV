using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifControllerPlayer : Node
{
    private readonly Dictionary<string, RuntimeNifControllerSequence> _sequences =
        new(StringComparer.Ordinal);
    private RuntimeNifControllerSequence? _active;
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
    internal double SourceTimeSeconds { get; private set; }
    internal object Observation => new
    {
        active = ActiveSequence,
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
            if (!_sequences.TryAdd(sequence.Name, sequence))
                throw new InvalidDataException(
                    $"NIF controller manager has duplicate sequence name {sequence.Name}.");
        var looping = _sequences.Values.Where(sequence => sequence.CycleType == 0).ToArray();
        if (looping.Length > 1)
            throw new NotSupportedException(
                "NIF controller manager has multiple automatic looping sequences.");
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
        var previous = _elapsedSeconds;
        _elapsedSeconds += delta;
        Apply(ResolveSourceTime(_active, _elapsedSeconds));
        var sequence = _active;
        var generation = _generation;
        var includeStart = _includeStart;
        _includeStart = false;
        if (sequence.TextKeys.Count != 0)
            foreach (var key in _textKeys!.Crossed(previous, _elapsedSeconds, includeStart))
            {
                var disposition = TextKeyHandler?.Invoke(key) ?? "unbound-runtime-event";
                if (disposition.Contains("unbound", StringComparison.Ordinal)) _unboundTextKeys.Add(key.Text);
                _lastTextKey = new { ordinal = ++_textKeyCount, sequence = sequence.Name, key, disposition };
                TextKeyObserver?.Invoke(new
                {
                    owner = GetParent().GetMeta("opennv_reference_form_key", "unbound").AsString(),
                    controller = Name.ToString(),
                    observation = _lastTextKey
                });
                if (_generation != generation) return;
            }
        if (_active.CycleType == 2 && SourceTimeSeconds >= _active.StopTime)
            SetProcess(false);
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

    internal RuntimeNifControllerChannel(Action<float> apply) => _apply = apply;

    internal void Apply(float sourceTime) => _apply(sourceTime);
}
