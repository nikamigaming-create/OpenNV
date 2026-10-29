using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutInventoryInstance(FalloutFormKey Form, int Variant, FalloutItemVariant Extra);

internal sealed partial class FalloutPlayerInventory
{
    internal IReadOnlyList<FalloutInventoryInstance> Instances(FalloutFormKey form)
    {
        var item = Item(form) ?? throw new InvalidOperationException("Selected item is no longer present.");
        return (item.Variants ?? [new FalloutItemVariant(item.Count)]).Select((extra, index) => new FalloutInventoryInstance(form, index, extra)).ToArray();
    }

    internal void AssignHotkey(FalloutFormKey form, int variant, byte? slot, long revision)
    {
        RequireRevision(revision);
        var item = Item(form) ?? throw new InvalidOperationException("Hotkey item is absent.");
        if (item.RecordType is not ("WEAP" or "ARMO" or "ALCH" or "INGR"))
            throw new InvalidOperationException("This item cannot be equipped or used from a hotkey.");
        if (slot is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(slot));
        _ = Instances(form)[variant];
        ApplyRemovalTransaction(null, (staged, _) =>
        {
            foreach (var entry in staged.Items)
            {
                var extras = (entry.Variants ?? [new FalloutItemVariant(entry.Count)]).Select((extra, index) =>
                    entry.FormKey == form && index == variant ? extra with { Hotkey = slot } :
                    slot is not null && extra.Hotkey == slot ? extra with { Hotkey = null } : extra).ToArray();
                staged.Publish([entry with { Variants = extras }]);
            }
            if (slot is not null && staged.Instances(form)[variant].Extra.Count > 1)
            {
                var selected = staged.Instances(form)[variant].Extra;
                staged.ReplaceInstances(form, [(variant, 1)], [selected with { Count = 1, Hotkey = slot }]);
            }
        });
    }

    internal FalloutInventoryInstance? Hotkey(byte slot) => Items.SelectMany(item => Instances(item.FormKey))
        .SingleOrDefault(instance => instance.Extra.Hotkey == slot);

    internal byte WeaponModMask(FalloutFormKey weapon) => SelectedWeapon(weapon).Extra.WeaponMods;

    internal FalloutInventoryInstance SelectedWeapon(FalloutFormKey weapon) => Instances(weapon)[0];

    internal FalloutInventoryInstance EnsureWeaponInstance(FalloutFormKey weapon)
    {
        var selected = SelectedWeapon(weapon);
        if (Item(weapon)!.RecordType != "WEAP") throw new InvalidOperationException("Weapon identity requires WEAP.");
        if (selected.Extra.Instance is not null) return selected;
        SelectInstance(weapon, selected.Variant, Revision);
        return SelectedWeapon(weapon);
    }

    internal void SetWeaponMagazine(FalloutFormKey weapon, Guid instance, FalloutItemMagazine magazine) =>
        Publish([WithWeaponMagazine(Item(weapon) ?? throw new InvalidOperationException("Magazine weapon is absent."), instance, magazine)]);

    internal static FalloutCampaignItem WithWeaponMagazine(FalloutCampaignItem item, Guid instance, FalloutItemMagazine magazine)
    {
        if (instance == Guid.Empty || magazine.Loaded < 0 || magazine.Ammunition.ObjectId == 0 || item.RecordType != "WEAP")
            throw new InvalidDataException("Weapon magazine identity or contents are invalid.");
        var variants = item.Variants?.ToArray() ?? throw new InvalidDataException("Weapon magazine has no individual inventory owner.");
        var index = Array.FindIndex(variants, extra => extra.Instance == instance);
        if (index < 0 || variants[index].Count != 1) throw new InvalidOperationException("Weapon magazine instance is absent or ambiguous.");
        variants[index] = variants[index] with { Magazine = magazine };
        return item with { Variants = variants };
    }

    internal int LoadedInventoryRounds(FalloutFormKey ammunition, Guid? excluding = null) => Items
        .SelectMany(item => item.Variants ?? []).Where(extra => extra.Instance != excluding && extra.Magazine?.Ammunition == ammunition)
        .Sum(extra => extra.Magazine!.Loaded);

    internal void SelectInstance(FalloutFormKey form, int variant, long revision)
    {
        RequireRevision(revision);
        var item = Item(form) ?? throw new InvalidOperationException("Selected item is absent.");
        var instances = Instances(form);
        var selected = instances[variant].Extra;
        var extras = instances.Where(value => value.Variant != variant).Select(value => value.Extra).ToList();
        if (item.RecordType == "WEAP")
        {
            if (selected.Count > 1) extras.Insert(0, selected with { Count = selected.Count - 1, Hotkey = null, Instance = null, Magazine = null });
            selected = selected with { Count = 1, Instance = selected.Instance ?? Guid.NewGuid() };
        }
        extras.Insert(0, selected);
        Publish([item with { Variants = extras }]);
    }

    internal IReadOnlyList<FalloutInventoryInstance> RepairMaterials(FalloutPluginStack records, FalloutFormKey target, int variant)
    {
        var item = Item(target) ?? throw new InvalidOperationException("Repair target is absent.");
        if (item.RecordType is not ("WEAP" or "ARMO")) return [];
        var selected = Instances(target)[variant];
        if ((selected.Extra.Condition ?? 1) >= 1) return [];
        var allowed = new HashSet<FalloutFormKey> { target };
        var record = records.GetEffective(target);
        var repair = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "REPL").Data;
        if (!repair.IsEmpty)
        {
            if (repair.Length != 4) throw new InvalidDataException("Repair list FormID extent is invalid.");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(repair.Span);
            if (raw != 0) ReadList(record.Plugin.AdjustFormId(raw), []);
        }
        return Items.Where(entry => allowed.Contains(entry.FormKey) && !entry.UnequipLocked &&
                !records.QuestObjects.IsQuestObject(entry.FormKey) &&
                (entry.FormKey == target || !_equipped.Contains(entry.RuntimeFormId)))
            .SelectMany(entry => Instances(entry.FormKey)).Where(instance =>
                instance.Form != target || instance.Variant != variant || instance.Extra.Count > 1).ToArray();

        void ReadList(FalloutFormKey form, HashSet<FalloutFormKey> active)
        {
            if (!active.Add(form)) throw new InvalidDataException("Repair list contains a cycle.");
            var list = records.GetEffective(form);
            if (list.Signature != "FLST") { allowed.Add(form); return; }
            foreach (var field in list.ReadSubrecords().Where(field => field.Signature == "LNAM"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Repair list entry extent is invalid.");
                ReadList(list.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)), new(active));
            }
        }
    }

    internal void Repair(FalloutPluginStack records, FalloutFormKey target, int variant,
        FalloutFormKey material, int materialVariant, float skill, long revision)
    {
        RequireRevision(revision);
        if (!float.IsFinite(skill)) throw new InvalidDataException("Repair skill is invalid.");
        if (!RepairMaterials(records, target, variant).Any(value => value.Form == material && value.Variant == materialVariant))
            throw new InvalidOperationException("Selected repair material is no longer eligible.");
        var selected = Instances(target)[variant].Extra;
        var donor = Instances(material)[materialVariant].Extra;
        // The source formula uses condition on a 0..10 scale. NV retains its
        // fractional result, unlike FO3's final integer-percent rounding.
        var minimum = records.NumericSettings.Float("fRepairMin");
        var maximum = records.NumericSettings.Float("fRepairMax");
        var scavenge = records.NumericSettings.Float("fRepairScavengeMult");
        var a = (selected.Condition ?? 1) * 10; var b = (donor.Condition ?? 1) * 10;
        var condition = Math.Clamp((Math.Max(a, b) + minimum + (maximum - minimum) * Math.Clamp(skill, 0, 100) / 100 + Math.Min(a, b) * scavenge) / 10, .05f, 1);
        if (!float.IsFinite(condition) || condition <= (selected.Condition ?? 1))
            throw new InvalidOperationException("This repair would not improve the selected item.");
        ApplyRemovalTransaction(null, (staged, _) =>
        {
            if (target == material)
                staged.ReplaceInstances(target, [(variant, 1), (materialVariant, 1)], [selected with { Count = 1, Condition = condition,
                    Instance = Item(target)!.RecordType == "WEAP" ? selected.Instance ?? Guid.NewGuid() : null }]);
            else
            {
                staged.ReplaceInstances(material, [(materialVariant, 1)], []);
                staged.ReplaceInstances(target, [(variant, 1)], [selected with { Count = 1, Condition = condition,
                    Instance = Item(target)!.RecordType == "WEAP" ? selected.Instance ?? Guid.NewGuid() : null }]);
            }
        });
    }

    internal void InstallWeaponMod(FalloutPluginStack records, FalloutFormKey weapon, int variant,
        int slot, long revision)
    {
        RequireRevision(revision);
        var selected = Instances(weapon)[variant].Extra;
        var modification = FalloutWeaponModifications.Read(records, weapon).Single(value => value.Slot == slot);
        FalloutWeaponModifications.RequireRuntimeEffect(modification);
        if ((selected.WeaponMods & (1 << slot)) != 0) throw new InvalidOperationException("This modification is already installed.");
        var mask = (byte)(selected.WeaponMods | (1 << slot));
        var capacity = FalloutWeaponPresentation.Read(records, weapon, weaponMods: mask).ClipSize;
        var magazine = selected.Magazine is { } previous ? previous with { Loaded = Math.Min(previous.Loaded, capacity) } : null;
        var material = Item(modification.Item) ?? throw new InvalidOperationException("Required weapon modification is absent.");
        if (records.QuestObjects.IsQuestObject(material.FormKey) || material.UnequipLocked)
            throw new InvalidOperationException("The modification cannot be consumed.");
        ApplyRemovalTransaction(null, (staged, _) =>
        {
            staged.RemoveCore(material.FormKey, 1, true);
            staged.ReplaceInstances(weapon, [(variant, 1)], [selected with { Count = 1, Instance = selected.Instance ?? Guid.NewGuid(), WeaponMods = mask, Magazine = magazine }]);
        });
    }

    internal void DropInstance(FalloutPluginStack records, FalloutFormKey form, int variant, int count,
        long revision, FalloutInventoryChangeLease worldPublication)
    {
        RequireRevision(revision);
        var item = Item(form) ?? throw new InvalidOperationException("Dropped item is absent.");
        if (!FalloutInventoryAccess.CanTransfer(records, records.GetEffective(form), true) || item.UnequipLocked)
            throw new InvalidOperationException("This item cannot be dropped.");
        if (count <= 0 || count > Instances(form)[variant].Extra.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var dropped = Instances(form)[variant].Extra;
        ApplyRemovalTransaction(null, (staged, _) =>
        {
            staged.ReplaceInstances(form, [(variant, count)], []);
            if (dropped.Magazine is { Loaded: > 0 } magazine)
            {
                if ((staged.Item(magazine.Ammunition)?.Count ?? 0) < magazine.Loaded)
                    throw new InvalidOperationException("Dropped magazine exceeds carried ammunition.");
                staged.RemoveCore(magazine.Ammunition, magazine.Loaded, true);
            }
            if (variant == 0) staged._equipped.Remove(item.RuntimeFormId);
        }, worldPublication);
    }

    private void RequireRevision(long revision)
    {
        RequireNoChangeInProgress();
        if (Revision != revision) throw new InvalidOperationException("Inventory changed; select the item again.");
    }

    private void ReplaceInstances(FalloutFormKey form, IReadOnlyList<(int Index, int Count)> removals,
        IReadOnlyList<FalloutItemVariant> additions)
    {
        var item = Item(form) ?? throw new InvalidOperationException("Inventory instance disappeared.");
        var extras = (item.Variants ?? [new FalloutItemVariant(item.Count)]).ToArray();
        var worn = item.RecordType == "WEAP" && _equipped.Contains(item.RuntimeFormId) ? extras[0].Instance : null;
        foreach (var removal in removals)
        {
            if (removal.Index < 0 || removal.Index >= extras.Length || extras[removal.Index].Count < removal.Count)
                throw new InvalidOperationException("Selected inventory instance is no longer available.");
            extras[removal.Index] = extras[removal.Index] with { Count = extras[removal.Index].Count - removal.Count };
        }
        var assigned = additions.Where(extra => extra.Hotkey.HasValue).Select(extra => extra.Hotkey).ToHashSet();
        var nextItems = new List<FalloutItemVariant>();
        var insertion = removals.Count == 0 ? 0 : removals[0].Index;
        for (var index = 0; index < extras.Length; index++)
        {
            if (index == insertion) nextItems.AddRange(additions);
            if (extras[index].Count <= 0) continue;
            nextItems.Add(assigned.Contains(extras[index].Hotkey) ? extras[index] with { Hotkey = null } : extras[index]);
        }
        var next = nextItems.ToArray();
        if (worn is not null && next.All(extra => extra.Instance != worn)) _equipped.Remove(item.RuntimeFormId);
        var count = next.Sum(extra => extra.Count);
        if (count == 0) { _items.Remove(form); _equipped.Remove(item.RuntimeFormId); ++Revision; }
        else Publish([item with { Count = count, Variants = next }]);
    }
}
