namespace OpenNV.Runtime.Content;

internal sealed record FalloutInterfaceActorDistanceSnapshot(int Count, IReadOnlyList<FalloutFormKey?> Slots,
    long Sequence, string? Failure);

// The owned class has fifty inline pointer slots and a separate signed count.
// Inactive slots remain independent state: native full-tail moves can retain
// entries beyond Count, so serializing only active entries loses source bytes.
internal sealed class FalloutInterfaceActorDistanceList
{
    internal const int Capacity = 50;
    private readonly FalloutFormKey?[] _slots = new FalloutFormKey?[Capacity];
    private int _count;
    private long _sequence, _reentry;
    private bool _busy;
    private string? _failure;
    internal string? SaveBlocker => _busy ? "source-interface-actor-distance-list-in-flight" : _failure;
    internal FalloutInterfaceActorDistanceSnapshot Capture()
    {
        RequireIdle(); return new(_count, _slots.ToArray(), _sequence, _failure);
    }
    internal void Restore(FalloutInterfaceActorDistanceSnapshot saved)
    {
        RequireIdle(); Validate(saved);
        saved.Slots.ToArray().CopyTo(_slots, 0); _count = saved.Count; _sequence = saved.Sequence; _failure = saved.Failure;
    }
    internal static void Validate(FalloutInterfaceActorDistanceSnapshot saved)
    {
        if (saved is null || saved.Count is < 0 or > Capacity || saved.Slots is not { Count: Capacity } || saved.Sequence < 0 ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure) ||
            saved.Slots.Any(key => key is { } actual && (actual.ObjectId == 0 || string.IsNullOrWhiteSpace(actual.OwnerPlugin))) ||
            saved.Slots.Take(saved.Count).Any(key => key is null) ||
            saved.Failure is null && saved.Slots.Take(saved.Count).Distinct().Count() != saved.Count ||
            saved.Sequence == 0 && (saved.Count != 0 || saved.Slots.Any(key => key is not null)))
            throw new InvalidDataException("Interface actor-distance list lost its inline extent, count, source reference or partial order.");
    }
    internal bool Add(FalloutFormKey? actor, int limit, Func<FalloutFormKey, float?> readContextValue) => Operation(() =>
    {
        ArgumentNullException.ThrowIfNull(readContextValue);
        if (actor is not { } source || Callback(() => readContextValue(source)) is not { } value) return false;
        if (_slots.Take(_count).Contains(source)) return false;
        var index = 0;
        for (; index < _count; ++index)
            if (Callback(() => readContextValue(_slots[index]!.Value)) is { } other && other > value) break;
        if (index >= Capacity || index >= limit) return false;
        Array.Copy(_slots, index, _slots, index + 1, Capacity - index - 1); Changed();
        _slots[index] = source; Changed();
        if (_count < limit && _count < Capacity) { ++_count; Changed(); }
        return true;
    });
    internal bool Remove(FalloutFormKey actor) => Operation(() =>
    {
        var index = Array.IndexOf(_slots, actor, 0, _count);
        if (index < 0) return false;
        Array.Copy(_slots, index + 1, _slots, index, Capacity - index - 1); Changed();
        _slots[_count - 1] = null; Changed(); --_count; Changed(); return true;
    });
    internal void Sort(int limit, Func<FalloutFormKey, float> readDistance) => Operation(() =>
    {
        ArgumentNullException.ThrowIfNull(readDistance);
        var last = _count - 1; var gap = 1;
        while (gap <= last / 9) gap = checked(gap * 3 + 1);
        for (; gap > 0; gap /= 3)
            for (var index = gap; index <= last; ++index)
            {
                var item = _slots[index]!.Value; var position = index;
                while (position >= gap)
                {
                    // Source queries the held candidate first on every shift,
                    // then the previous entry. Unordered/equal values stop.
                    var candidate = Callback(() => readDistance(item));
                    var previous = Callback(() => readDistance(_slots[position - gap]!.Value));
                    if (!(previous > candidate)) break;
                    _slots[position] = _slots[position - gap]; Changed(); position -= gap;
                }
                _slots[position] = item; Changed();
            }
        while (_count > limit)
        {
            if (_count == 0) throw new NotSupportedException("source-interface-negative-actor-limit-underflows-inline-array");
            _slots[_count - 1] = null; Changed(); --_count; Changed();
        }
        return true;
    });
    internal void Clear() => Operation(() =>
    {
        _count = 0; Changed(); Array.Clear(_slots); Changed(); return true;
    });
    private void RequireIdle()
    {
        if (_busy)
        {
            _reentry = checked(_reentry + 1);
            throw new InvalidOperationException("Interface actor list reentered its current source mutation.");
        }
    }
    private T Callback<T>(Func<T> read)
    {
        var before = _reentry; var result = read();
        if (_reentry != before) throw new InvalidOperationException("Interface actor-list callback swallowed a source reentry refusal.");
        return result;
    }
    private T Operation<T>(Func<T> update)
    {
        RequireIdle(); if (_failure is not null) throw new NotSupportedException(_failure); _busy = true;
        try { return update(); }
        catch (Exception error) { _failure ??= error.Message; Changed(); throw; }
        finally { _busy = false; }
    }
    private void Changed() => _sequence = checked(_sequence + 1);
}
