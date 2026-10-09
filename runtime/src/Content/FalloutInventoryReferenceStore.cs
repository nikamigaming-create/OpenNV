using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutInventoryReferenceEntry(ulong Frame, ulong Ordinal,
    FalloutFormKey Container, FalloutPluginRecord Item, int CountDelta, FalloutPlayerInventory Inventory,
    long InventoryRevision, FalloutItemVariant? Extra, int? Variant);

// A temporary entry is source argument state, not a placed actor. Publication
// requires a genuine native reference/entry/extra-data constructor separately.
internal sealed class FalloutInventoryReferenceStore(FalloutPluginStack records, FalloutReferenceWorld world,
    Func<int> playerLevel, Func<FalloutGlobalState?> globals)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<ulong, FalloutInventoryReferenceEntry> _entries = [];
    private ulong _frame, _lastFrame, _ordinal;
    private bool _retired;
    internal bool HasFrame => _frame != 0;
    internal string SourceIdentity => records.OwnedSource?.StackId ?? throw new InvalidOperationException("Inventory reference source graph has no owned identity.");
    internal object WorldIdentity => world;
    internal IReadOnlyList<FalloutInventoryReferenceEntry> Entries => _entries.Values.ToArray();

    internal void BeginFrame(ulong frame)
    {
        RequireOwner();
        if (_frame != 0 || _entries.Count != 0 || frame == 0 || frame <= _lastFrame)
            throw new InvalidOperationException("Inventory references require a new actual frame after prior native retirement.");
        _frame = _lastFrame = frame;
    }
    internal FalloutInventoryReferenceEntry PrepareCreate(FalloutFormKey container, FalloutFormKey item,
        int countDelta, int? extraVariant)
    {
        RequireFrame();
        if (!ReferenceEquals(world.NativeSourceRecords, records)) throw new InvalidDataException("Inventory reference world/source identities differ.");
        var source = records.GetEffective(item);
        if (source.IsDeleted) throw new InvalidDataException("Temporary inventory item was deleted by its winner.");
        var pool = world.Inventory(container, playerLevel(), globals()).Contents;
        FalloutItemVariant? extra = null;
        if (extraVariant is { } ordinal)
        {
            var owned = pool.Item(item) ?? throw new InvalidOperationException("Native extra data does not belong to a current item stack.");
            var variants = owned.Variants ?? [new(owned.Count)];
            if ((uint)ordinal >= variants.Count) throw new InvalidDataException("Native extra data is outside the exact item-instance stack.");
            extra = variants[ordinal];
        }
        // CountDelta is the original signed argument. Negative or zero entries
        // must not be silently rewritten into the current pool's total count.
        var entry = new FalloutInventoryReferenceEntry(_frame, checked(++_ordinal), container, source,
            countDelta, pool, pool.Revision, extra, extraVariant);
        _entries.Add(entry.Ordinal, entry); return entry;
    }
    internal void RequireCurrent(FalloutInventoryReferenceEntry entry)
    {
        RequireFrame();
        if (entry.Frame != _frame || !_entries.TryGetValue(entry.Ordinal, out var current) || !ReferenceEquals(current, entry) ||
            !ReferenceEquals(records.GetEffective(entry.Item.FormKey), entry.Item) || entry.Inventory.Revision != entry.InventoryRevision)
            throw new InvalidOperationException("Inventory-reference frame, source winner or actual item pool changed.");
        if (entry.Variant is { } variant)
        {
            var currentItem = entry.Inventory.Item(entry.Item.FormKey) ?? throw new InvalidOperationException("Inventory extra-data owner disappeared.");
            var variants = currentItem.Variants ?? [new(currentItem.Count)];
            if ((uint)variant >= variants.Count || variants[variant] != entry.Extra)
                throw new InvalidOperationException("Inventory reference extra-data stack changed.");
        }
    }
    internal void RetireEntry(FalloutInventoryReferenceEntry entry)
    {
        RequireOwner();
        if (entry.Frame != _frame || !_entries.TryGetValue(entry.Ordinal, out var current) || !ReferenceEquals(current, entry))
            throw new InvalidOperationException("Inventory reference entry is foreign or already retired.");
        _entries.Remove(entry.Ordinal);
    }
    internal void EndFrame(ulong frame)
    {
        RequireFrame();
        if (frame != _frame || _entries.Count != 0)
            throw new InvalidOperationException("Inventory reference frame closes only after every actual native entry has retired.");
        _frame = 0;
    }
    internal void Retire()
    {
        RequireOwner();
        if (_entries.Count != 0 || _frame != 0) throw new InvalidOperationException("Live inventory-reference entries retain their campaign owner.");
        _retired = true;
    }
    private void RequireFrame() { RequireOwner(); if (_frame == 0) throw new InvalidOperationException("Inventory reference creation has no actual source-frame lease."); }
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Inventory references belong to the actual campaign thread.");
    }
}
