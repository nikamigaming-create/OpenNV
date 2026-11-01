using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private string? _inventoryAction;
    internal Func<bool>? RepairJuryRigging { get; init; }
    private bool HasRepairJuryRigging() => (RepairJuryRigging ??
        throw new NotSupportedException("Repair has no current player perk owner."))();
    private FalloutFormKey _actionItem;
    private int _actionVariant;
    private long _actionRevision;
    internal Action<FalloutFormKey, int, int, long>? DropItem { get; init; }

    private string InventoryInstanceLabel(FalloutCampaignItem item, FalloutItemVariant extra)
    {
        var condition = item.RecordType is "WEAP" or "ARMO" ? $"  {(extra.Condition ?? 1) * 100:0}%" : "";
        var hotkey = extra.Hotkey is { } slot ? $"  [{slot}]" : "";
        var mods = extra.WeaponMods != 0 ? " +" : "";
        return NameOf(item.FormKey) + mods + (extra.Count > 1 ? $" ({extra.Count})" : "") + condition + hotkey;
    }

    private void BeginInventoryAction(string action, FalloutCampaignItem selected)
    {
        _inventoryAction = action; _actionItem = selected.FormKey; _actionVariant = _selectedVariant;
        _actionRevision = _inventory.Revision; _offset = 0; Refresh();
    }

    private void InventoryActionButton(string name, string label, Action action, bool enabled = true, bool forceVisible = false)
    {
        var tile = Tile(name);
        if (forceVisible) _tiles.Bind(tile, "visible", 1);
        _tiles.Text[tile] = label;
        _tiles.Bind(tile, "target", enabled ? 1 : 0);
        Target(tile, label, action);
        _buttons[^1].Button.Disabled = !enabled;
    }

    private void BindInventoryActions(FalloutCampaignItem selected)
    {
        if (DropItem is not null && !selected.UnequipLocked &&
            FalloutInventoryAccess.CanTransfer(_records, _records.GetEffective(selected.FormKey), true))
            InventoryActionButton("IM_DropButton", "Drop", () => BeginInventoryAction("drop", SelectedInventoryItem()));
        var canRepair = selected.RecordType is "WEAP" or "ARMO" && !selected.UnequipLocked &&
            _inventory.RepairMaterials(_records, selected.FormKey, _selectedVariant, _references.FormLists, HasRepairJuryRigging).Count != 0;
        InventoryActionButton("IM_RepairButton", Setting("sInventoryRepair"),
            () => BeginInventoryAction("repair", SelectedInventoryItem()), canRepair);
        if (selected.RecordType is "WEAP" or "ARMO" or "ALCH" or "INGR")
            InventoryActionButton("IM_HotkeyButton", "Hotkey", () => BeginInventoryAction("hotkey", SelectedInventoryItem()));
        var instance = _inventory.Instances(selected.FormKey)[_selectedVariant].Extra;
        var canModify = selected.RecordType == "WEAP" && FalloutWeaponModifications.Read(_records, selected.FormKey)
            .Any(modification => (instance.WeaponMods & (1 << modification.Slot)) == 0 &&
                _inventory.Item(modification.Item) is { Count: > 0 });
        InventoryActionButton("IM_ModButton", Setting("sInventoryMod"),
            () => BeginInventoryAction("mod", SelectedInventoryItem()), canModify);
    }

    private bool BuildInventoryActionList()
    {
        if (_inventoryAction is null) return false;
        if (_inventory.Revision != _actionRevision || _inventory.Item(_actionItem) is null)
        {
            _inventoryAction = null;
            return false;
        }
        Hide("IM_EquipButton", "IM_DropButton", "IM_RepairButton", "IM_HotkeyButton", "IM_ModButton");
        var rows = new List<(string Label, Action Action, bool Equipped)>();
        var selected = _inventory.Instances(_actionItem)[_actionVariant].Extra;
        var title = _inventoryAction switch
        {
            "repair" => "Choose an item to consume for repair",
            "mod" => "Choose a modification to install permanently",
            "hotkey" => "Choose a hotkey",
            "drop" => "Choose how many to drop",
            _ => throw new InvalidOperationException("Unknown inventory action.")
        };
        if (_inventoryAction == "repair")
            foreach (var donor in _inventory.RepairMaterials(_records, _actionItem, _actionVariant, _references.FormLists, HasRepairJuryRigging))
            {
                var material = _inventory.Item(donor.Form)!;
                rows.Add((InventoryInstanceLabel(material, donor.Extra), () =>
                {
                    _inventory.Repair(_records, _actionItem, _actionVariant, donor.Form, donor.Variant,
                        (_actorValue ?? throw new InvalidOperationException("Repair has no player skill owner."))("Repair"), _actionRevision, _references.FormLists, HasRepairJuryRigging);
                    EndInventoryAction();
                }, false));
            }
        else if (_inventoryAction == "mod")
            foreach (var modification in FalloutWeaponModifications.Read(_records, _actionItem))
            {
                var installed = (selected.WeaponMods & (1 << modification.Slot)) != 0;
                var available = _inventory.Item(modification.Item)?.Count ?? 0;
                rows.Add(($"{NameOf(modification.Item)}  {(installed ? "Installed" : available > 0 ? "Install" : "Not owned")}", () =>
                {
                    _inventory.InstallWeaponMod(_records, _actionItem, _actionVariant, modification.Slot, _actionRevision, _references.FormLists);
                    EndInventoryAction();
                }, installed));
            }
        else if (_inventoryAction == "drop")
        {
            foreach (var count in new[] { 1, selected.Count }.Distinct())
                rows.Add(($"Drop {count}", () =>
                {
                    (DropItem ?? throw new InvalidOperationException("Drop has no physical world owner."))
                        (_actionItem, _actionVariant, count, _actionRevision);
                    EndInventoryAction();
                }, false));
        }
        else
        {
            foreach (var slot in new byte[] { 1, 3, 4, 5, 6, 7, 8 })
            {
                var assigned = _inventory.Hotkey(slot);
                rows.Add(($"{slot}  {(assigned is null ? "Empty" : NameOf(assigned.Form))}", () =>
                {
                    _inventory.AssignHotkey(_actionItem, _actionVariant, slot, _actionRevision);
                    EndInventoryAction();
                }, selected.Hotkey == slot));
            }
            rows.Add(("Clear hotkey", () =>
            {
                _inventory.AssignHotkey(_actionItem, _actionVariant, null, _actionRevision);
                EndInventoryAction();
            }, false));
        }
        AddText(NameOf(_actionItem) + "\n" + title, new(470, 440), new(400, 140));
        ListRows(Tile("IM_InventoryList"), "IM_InventoryListTemplate", rows);
        InventoryActionButton("IM_CancelButton", "Cancel", () => { _inventoryAction = null; _offset = 0; Refresh(); }, forceVisible: true);
        if (rows.Count == 0) AddText("No eligible items", new(470, 590), new(400, 50));
        return true;
    }

    private void EndInventoryAction()
    {
        _inventoryAction = null; _selectedVariant = 0; _offset = 0; Refresh();
    }
}
