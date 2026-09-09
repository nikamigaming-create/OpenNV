namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifTextKeyEvent(long Cycle, int SourceOrdinal, float SourceSeconds, string Text);

// Enumerate the crossed source interval, including every complete loop. A seek
// is deliberately separate from advancement and must not replay past sounds.
internal sealed class FalloutNifTextKeyTimeline
{
    private readonly (FalloutNifTextKey Key, int Index)[] _keys;
    private readonly double _duration;
    private readonly float _start;
    private readonly float _frequency;
    private readonly uint _cycleType;

    internal FalloutNifTextKeyTimeline(IReadOnlyList<FalloutNifTextKey> keys,
        float start, float stop, uint cycleType, float frequency)
    {
        if (!float.IsFinite(start) || !float.IsFinite(stop) || stop <= start ||
            !float.IsFinite(frequency) || frequency <= 0 || cycleType is not (0 or 2) ||
            keys.Any(key => !float.IsFinite(key.Time)))
            throw new InvalidDataException("NIF text-key sequence clock is invalid.");
        _duration = (double)stop - start; _start = start; _cycleType = cycleType; _frequency = frequency;
        _keys = keys.Select((key, index) => (Key: key, Index: index))
            .Where(row => row.Key.Time >= start && row.Key.Time <= stop)
            .OrderBy(row => row.Key.Time).ThenBy(row => row.Index).ToArray();
    }

    internal IEnumerable<FalloutNifTextKeyEvent> Crossed(double from, double to, bool includeFrom)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || from < 0 || to < from)
            throw new InvalidDataException("NIF text-key interval is invalid.");
        var lower = from * _frequency;
        var upper = to * _frequency;
        if (!double.IsFinite(lower) || !double.IsFinite(upper))
            throw new InvalidDataException("NIF text-key clock overflowed.");
        var first = _cycleType == 0 ? checked((long)Math.Floor(lower / _duration)) : 0;
        var last = _cycleType == 0 ? checked((long)Math.Floor(upper / _duration)) : 0;
        if (includeFrom && first > 0 && lower == first * _duration) first--;
        for (var cycle = first; cycle <= last; cycle = checked(cycle + 1))
            foreach (var (key, index) in _keys)
            {
                var time = cycle * _duration + ((double)key.Time - _start);
                if (time <= upper && (time > lower || includeFrom && time == lower))
                    yield return new(cycle, index, key.Time, key.Value);
            }
    }
}
