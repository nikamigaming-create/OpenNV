namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerTimedAction(Guid Identity, uint Kind, uint RemainingBits, Guid? Context);
internal sealed record FalloutPlayerTimedActionsSnapshot(bool Allocated, IReadOnlyList<FalloutPlayerTimedAction> Entries);

// A separate Player list, not the reference/3D queue or the Main utility queue.
// The source producer moves the first same-kind item to the head, reusing that
// item. Removal has an optional context match; zero is the source wildcard.
internal sealed class FalloutPlayerTimedActions
{
    private bool _allocated;
    private readonly List<FalloutPlayerTimedAction> _entries = [];
    internal FalloutPlayerTimedActionsSnapshot Capture() => new(_allocated, _entries.ToArray());
    internal FalloutPlayerTimedAction? Find(uint kind) => _entries.FirstOrDefault(entry => entry.Kind == kind);
    internal void Store(uint kind, uint remainingBits, Guid? context, byte suppressed)
    {
        if (suppressed != 0) return;
        if (context is not null)
            throw new NotSupportedException("source-Player-timed-action-nonnull-context-acquire-unbound");
        _allocated = true;
        var index = _entries.FindIndex(entry => entry.Kind == kind);
        var entry = index >= 0 ? _entries[index] : new FalloutPlayerTimedAction(Guid.NewGuid(), kind, 0, null);
        if (index >= 0) _entries.RemoveAt(index);
        _entries.Insert(0, entry with { RemainingBits = remainingBits, Context = context });
    }
    internal bool RemoveFirst(uint kind, Guid? context)
    {
        var index = _entries.FindIndex(entry => entry.Kind == kind && (context is null || entry.Context == context));
        if (index < 0) return false;
        _entries.RemoveAt(index); return true;
    }
    internal IReadOnlyList<Guid> Expire()
    {
        var removed = new List<Guid>();
        // The native below branch retains positive and unordered values.
        // Expiry therefore requires an ordered nonpositive Float32 value.
        for (var index = 0; index < _entries.Count;)
        {
            var entry = _entries[index];
            if (entry.Kind is not (5 or 8) && BitConverter.UInt32BitsToSingle(entry.RemainingBits) <= 0f)
            { removed.Add(entry.Identity); _entries.RemoveAt(index); index = 0; }
            else ++index;
        }
        return removed;
    }
    internal int Subtract(float scaledSeconds)
    {
        for (var index = 0; index < _entries.Count; ++index)
        {
            var entry = _entries[index];
            _entries[index] = entry with
            { RemainingBits = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(entry.RemainingBits) - scaledSeconds) };
        }
        return _entries.Count;
    }
    internal void Restore(FalloutPlayerTimedActionsSnapshot saved)
    {
        Validate(saved);
        _entries.Clear(); _entries.AddRange(saved.Entries); _allocated = saved.Allocated;
    }
    internal static void Validate(FalloutPlayerTimedActionsSnapshot saved)
    {
        if (saved is null || saved.Entries is null || !saved.Allocated && saved.Entries.Count != 0 ||
            saved.Entries.Any(entry => entry is null || entry.Identity == Guid.Empty || entry.Context == Guid.Empty) ||
            saved.Entries.Select(entry => entry.Identity).Distinct().Count() != saved.Entries.Count)
            throw new InvalidDataException("Player timed actions lost their actual allocation, item identity or canonical order.");
    }
}
