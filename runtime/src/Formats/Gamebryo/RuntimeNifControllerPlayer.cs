using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifControllerPlayer : Node
{
    private readonly Dictionary<string, RuntimeNifControllerSequence> _sequences =
        new(StringComparer.Ordinal);
    private RuntimeNifControllerSequence? _active;
    private double _elapsedSeconds;

    internal IReadOnlyCollection<string> SequenceNames => _sequences.Keys;
    internal string? ActiveSequence => _active?.Name;
    internal double SourceTimeSeconds { get; private set; }

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
        _elapsedSeconds = 0.0;
        Apply(sequence.StartTime);
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
        _elapsedSeconds = Math.Max(
            0.0,
            (sourceSeconds - _active.StartTime) / _active.Frequency);
        Apply(ResolveSourceTime(_active, _elapsedSeconds));
    }

    public override void _Process(double delta)
    {
        if (_active is null)
            return;
        _elapsedSeconds += delta;
        Apply(ResolveSourceTime(_active, _elapsedSeconds));
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
        var duration = sequence.StopTime - sequence.StartTime;
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
    IReadOnlyList<RuntimeNifControllerChannel> Channels);

internal sealed class RuntimeNifControllerChannel
{
    private readonly Action<float> _apply;

    internal RuntimeNifControllerChannel(Action<float> apply) => _apply = apply;

    internal void Apply(float sourceTime) => _apply(sourceTime);
}
