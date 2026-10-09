using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPipBoyPage { Stats, Items, Data }

// Menu navigation and inventory invalidation belong to the gameplay UI owner;
// the native/VR device adapters consume the same selection and live items.
internal sealed class FalloutPipBoyState(FalloutPluginStack records, FalloutPlayerInventory inventory)
{
    private readonly FalloutFormKey _device = FalloutDialogueTopic.Find(records, "ARMO", "PipBoy").FormKey;
    private IReadOnlyList<FalloutCampaignItem>? _items;
    private long _inventoryRevision = -1;
    private readonly int[] _selections = [0, 0, 1];
    internal bool Available => inventory.Equipped.Contains(records.RuntimeFormId(_device));
    internal bool Open { get; private set; }
    internal FalloutPipBoyPage Page { get; private set; }
    internal int Selection { get; private set; }
    internal long Revision { get; private set; }
    internal IReadOnlyList<FalloutCampaignItem> Items
    {
        get
        {
            if (_items is null || _inventoryRevision != inventory.Revision)
            {
                _items = inventory.Items;
                _inventoryRevision = inventory.Revision;
            }
            return _items;
        }
    }
    internal void Reset()
    {
        Open = false; Page = FalloutPipBoyPage.Stats; Selection = 0;
        _selections[0] = _selections[1] = 0; _selections[2] = 1;
        _items = null; _inventoryRevision = -1; ++Revision;
    }
    internal void SetOpen(bool open)
    {
        if (open && !Available) throw new InvalidOperationException("Pip-Boy equipment is absent.");
        if (Open == open) return;
        Open = open; ++Revision;
    }
    internal void Select(FalloutPipBoyPage page, int index)
    {
        if (!Enum.IsDefined(page) || index is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(index));
        _selections[(int)page] = index;
        Page = page; Selection = index; ++Revision;
    }
    internal int SelectionFor(FalloutPipBoyPage page) => Enum.IsDefined(page) ? _selections[(int)page] :
        throw new ArgumentOutOfRangeException(nameof(page));
}
