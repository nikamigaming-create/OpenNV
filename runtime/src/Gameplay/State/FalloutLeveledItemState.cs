using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutLeveledItemAddition(FalloutFormKey Form, ushort Level, ushort Count, float Condition);
internal sealed record FalloutLeveledItemChanges(FalloutFormKey List, string Winner, string Sha256,
    IReadOnlyList<FalloutLeveledItemAddition> Added);
internal sealed record FalloutLeveledItemsSnapshot(IReadOnlyList<FalloutLeveledItemChanges> Lists);

// A world owns scripted entries; winning LVLI declarations never change. Keep
// command order (including duplicates) so equal-level insertion survives saves.
internal sealed class FalloutLeveledItemState(FalloutPluginStack records) : IFalloutLeveledItemChanges
{
    private readonly Dictionary<FalloutFormKey, List<FalloutLeveledItemAddition>> _added = [];
    internal long Revision { get; private set; }

    public IReadOnlyList<FalloutLeveledItemAddition> Additions(FalloutFormKey list) =>
        _added.TryGetValue(list, out var entries) ? entries.ToArray() : [];

    internal void Add(FalloutFormKey list, FalloutFormKey form, int level, int count, float health)
    {
        var maximumHealth = FalloutNativeTagSkillResolver.UsesFallout3Skills(records) ? 1f : 100f;
        if (!float.IsFinite(health) || health < 0 || health > maximumHealth ||
            level < 0 || level > ushort.MaxValue || count < 1 || count > ushort.MaxValue)
            throw new InvalidDataException("Scripted leveled entry has invalid level, count or health.");
        var addition = new FalloutLeveledItemAddition(records.GetEffective(form).FormKey, (ushort)level, (ushort)count, health / maximumHealth);
        list = Validate(list, addition).FormKey;
        var revision = checked(Revision + 1);
        if (!_added.TryGetValue(list, out var entries)) _added.Add(list, entries = []);
        entries.Add(addition);
        Revision = revision;
    }

    private FalloutPluginRecord Validate(FalloutFormKey list, FalloutLeveledItemAddition entry)
    {
        var source = records.GetEffective(list);
        if (source.Signature != "LVLI") throw new InvalidDataException("AddItemToLeveledList requires LVLI.");
        if (records.GetEffective(entry.Form).Signature is not
            ("LVLI" or "ALCH" or "AMMO" or "ARMO" or "BOOK" or "CCRD" or "CHIP" or "CMNY" or "IMOD" or "INGR" or "KEYM" or "LIGH" or "MISC" or "NOTE" or "WEAP"))
            throw new InvalidDataException("Scripted leveled entry is not an inventory base or leveled item.");
        if (entry.Count == 0 ||
            !float.IsFinite(entry.Condition) || entry.Condition is < 0 or > 1)
            throw new InvalidDataException("Saved scripted leveled entry is invalid.");
        return source;
    }

    private FalloutLeveledItemChanges Capture(FalloutFormKey list, IReadOnlyList<FalloutLeveledItemAddition> entries)
    {
        var source = records.GetEffective(list);
        return new(list, source.Plugin.Name, Convert.ToHexString(SHA256.HashData(source.ReadData())), entries.ToArray());
    }

    internal FalloutLeveledItemsSnapshot Capture() => new(_added.OrderBy(pair => pair.Key.OwnerPlugin, StringComparer.OrdinalIgnoreCase)
        .ThenBy(pair => pair.Key.ObjectId).Select(pair => Capture(pair.Key, pair.Value)).ToArray());

    internal void Restore(FalloutLeveledItemsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Lists);
        var prepared = new Dictionary<FalloutFormKey, List<FalloutLeveledItemAddition>>();
        foreach (var saved in snapshot.Lists)
        {
            ArgumentNullException.ThrowIfNull(saved.Added);
            foreach (var entry in saved.Added) Validate(saved.List, entry);
            var source = Capture(saved.List, saved.Added);
            if (records.GetEffective(saved.List).Signature != "LVLI" ||
                !source.Winner.Equals(saved.Winner, StringComparison.OrdinalIgnoreCase) || source.Sha256 != saved.Sha256 ||
                !prepared.TryAdd(saved.List, saved.Added.ToList()))
                throw new InvalidDataException("Saved scripted leveled-list source changed or is duplicated.");
        }
        _added.Clear();
        foreach (var pair in prepared) _added.Add(pair.Key, pair.Value);
        Revision = checked(Revision + 1);
    }
}

internal interface IFalloutLeveledItemChanges
{
    IReadOnlyList<FalloutLeveledItemAddition> Additions(FalloutFormKey list);
}
