using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorProcessCohortSnapshot(string Schema,
    IReadOnlyList<FalloutFormKey?> Slots, int[] Starts, int[] Ends, int[] Cursors, long Revision)
{
    internal FalloutActorProcessCohortSnapshot Copy() => this with
    { Slots = Slots.ToArray(), Starts = Starts.ToArray(), Ends = Ends.ToArray(), Cursors = Cursors.ToArray() };
}

// This is the original segmented array, including null holes and displaced
// boundary entries. Per-tier Lists change the original ordering after removal.
internal sealed class FalloutActorProcessCohort
{
    internal const string Schema = "opennv-actor-process-cohort/v1";
    private readonly Func<FalloutFormKey, bool> _sourceObject;
    private readonly List<FalloutFormKey?> _slots = [];
    private int[] _starts = new int[4], _ends = new int[4], _cursors = new int[4];
    private long _revision;

    internal FalloutActorProcessCohort(Func<FalloutFormKey, bool> sourceObject,
        FalloutActorProcessCohortSnapshot? restore = null)
    {
        _sourceObject = sourceObject ?? throw new ArgumentNullException(nameof(sourceObject));
        if (restore is not null) Restore(restore);
    }

    // Original numeric classes are independent of the C# enum's declaration order.
    internal static int Tier(FalloutDetectionProcessLevel level) => level switch
    {
        FalloutDetectionProcessLevel.High => 0,
        FalloutDetectionProcessLevel.MiddleHigh => 1,
        FalloutDetectionProcessLevel.MiddleLow => 2,
        FalloutDetectionProcessLevel.Low => 3,
        _ => throw new NotSupportedException("Original manager has no registration segment for this process class."),
    };
    internal int Start(int tier) { RequireTier(tier); return _starts[tier]; }
    internal int End(int tier) { RequireTier(tier); return _ends[tier]; }
    internal int Cursor(int tier) { RequireTier(tier); return _cursors[tier]; }
    internal long Revision => _revision;
    internal FalloutFormKey? At(int index) => index >= 0 && index < _slots.Count ? _slots[index] :
        throw new InvalidDataException("Actor manager read exceeds its actual source array.");
    internal IReadOnlyList<FalloutFormKey?> Segment(int tier)
    { RequireTier(tier); return _slots.Skip(_starts[tier]).Take(_ends[tier] - _starts[tier]).ToArray(); }

    internal void Add(FalloutFormKey key, FalloutDetectionProcessLevel level)
    {
        if (!_sourceObject(key)) throw new InvalidDataException("Process registration has no exact winning source object.");
        if (_slots.Any(value => value == key)) throw new InvalidOperationException("Source process object is already registered.");
        AddBoundary(key, Tier(level)); Normalize(); _revision = checked(_revision + 1);
    }
    private void AddBoundary(FalloutFormKey? key, int tier)
    {
        if (tier < 3 && checked(_ends[tier] + 1) > _starts[tier + 1])
        {
            // Empty trailing segments still move a null boundary. That null
            // is part of the original array operation, not an actor proxy.
            var boundary = ReadOrNull(_starts[tier + 1]);
            AddBoundary(boundary, tier + 1);
            _starts[tier + 1] = checked(_ends[tier] + 1);
        }
        Write(_ends[tier], key); _ends[tier] = checked(_ends[tier] + 1);
    }
    private FalloutFormKey? ReadOrNull(int index) => index < _slots.Count ? _slots[index] : null;
    private void Write(int index, FalloutFormKey? key)
    {
        while (_slots.Count <= index) _slots.Add(null);
        _slots[index] = key;
    }

    internal bool Remove(FalloutFormKey key, FalloutDetectionProcessLevel requestedTier)
    {
        var start = _starts[Tier(requestedTier)]; var index = -1;
        // Both originals search to the global low end, then derive the actual
        // segment from the found index. Restricting lookup to one tier differs.
        for (var at = start; at < _ends[3]; at++) if (_slots[at] == key) { index = at; break; }
        if (index < 0) return false;
        var tier = Enumerable.Range(0, 4).Single(value => index >= _starts[value] && index < _ends[value]);
        _ends[tier]--;
        if (index != _ends[tier]) _slots[index] = _slots[_ends[tier]];
        _slots[_ends[tier]] = null;
        Normalize(); _revision = checked(_revision + 1); return true;
    }

    internal void AdvanceCursor(int tier)
    {
        RequireTier(tier); _cursors[tier] = checked(_cursors[tier] + 1);
        Normalize(); _revision = checked(_revision + 1);
    }
    private void Normalize()
    {
        for (var tier = 0; tier < 4; tier++)
            if (_cursors[tier] < _starts[tier] || _cursors[tier] >= _ends[tier]) _cursors[tier] = _starts[tier];
    }
    internal FalloutActorProcessCohortSnapshot Capture() =>
        new(Schema, _slots.ToArray(), _starts.ToArray(), _ends.ToArray(), _cursors.ToArray(), _revision);
    private void Restore(FalloutActorProcessCohortSnapshot saved)
    {
        Validate(saved);
        if (saved.Slots.Where(value => value is not null).Any(value => !_sourceObject(value!.Value)))
            throw new InvalidDataException("Cold process cohort contains a foreign/deleted winning object.");
        _slots.AddRange(saved.Slots); _starts = saved.Starts.ToArray(); _ends = saved.Ends.ToArray();
        _cursors = saved.Cursors.ToArray(); _revision = saved.Revision;
    }
    internal static void Validate(FalloutActorProcessCohortSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Slots is null || saved.Starts is not { Length: 4 } ||
            saved.Ends is not { Length: 4 } || saved.Cursors is not { Length: 4 } || saved.Revision < 0 || saved.Starts[0] != 0)
            throw new InvalidDataException("Cold process cohort has an invalid original array shape.");
        for (var tier = 0; tier < 4; tier++)
            if (saved.Starts[tier] < 0 || saved.Ends[tier] < saved.Starts[tier] || saved.Ends[tier] > saved.Slots.Count ||
                tier < 3 && saved.Ends[tier] > saved.Starts[tier + 1] ||
                saved.Cursors[tier] < saved.Starts[tier] ||
                (saved.Ends[tier] == saved.Starts[tier] ? saved.Cursors[tier] != saved.Starts[tier] : saved.Cursors[tier] >= saved.Ends[tier]))
                throw new InvalidDataException("Cold process cohort lost segment/cursor bounds.");
        var active = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        for (var at = 0; at < saved.Slots.Count; at++)
            if (saved.Slots[at] is { } key && (!Enumerable.Range(0, 4).Any(tier => at >= saved.Starts[tier] && at < saved.Ends[tier]) || !active.Add(key)))
                throw new InvalidDataException("Cold process cohort has a live object in a gap or a duplicate registration.");
    }
    private static void RequireTier(int tier)
    { if (tier is < 0 or > 3) throw new InvalidDataException("Process cohort tier is outside its actual source domain."); }
}
