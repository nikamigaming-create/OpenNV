using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutActorOverrides> _actorOverrides = [];
    private readonly FalloutAbilityModifiers _perkAbilities = new(records);
    private static string RecordHash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();

    private FalloutPluginRecord ActorOverrideSource(FalloutFormKey target)
    {
        var record = records.GetEffective(target == records.RuntimeFormKey(0x14) ? records.RuntimeFormKey(7) : target);
        if (record.Signature is not ("NPC_" or "CREA" or "ACHR" or "ACRE"))
            throw new InvalidDataException("Actor override target is not an actor.");
        return record;
    }

    private FalloutFormKey ActorBase(FalloutFormKey reference) => reference == records.RuntimeFormKey(0x14)
        ? records.RuntimeFormKey(7) : Actor(reference).Base;

    private FalloutActorOverrides Overrides(FalloutFormKey target) => _actorOverrides.GetValueOrDefault(target) ??
        new(target, RecordHash(ActorOverrideSource(target)), [], []);

    private FalloutActorFormOverride FormOverride(FalloutFormKey form, string signature, int value)
    {
        var source = records.GetEffective(form);
        if (source.Signature != signature) throw new InvalidDataException($"Actor override requires {signature}.");
        return new(form, RecordHash(source), value);
    }

    internal IReadOnlyList<FalloutActorOverrides> CaptureActorOverrides() => _actorOverrides.Values.ToArray();

    internal void RestoreActorOverrides(IReadOnlyList<FalloutActorOverrides>? snapshots)
    {
        var admitted = new Dictionary<FalloutFormKey, FalloutActorOverrides>();
        foreach (var snapshot in snapshots ?? [])
        {
            if (snapshot is null || snapshot.Perks is null || snapshot.Factions is null ||
                !RecordHash(ActorOverrideSource(snapshot.Target)).Equals(snapshot.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                !admitted.TryAdd(snapshot.Target, snapshot)) throw new InvalidDataException("Saved actor overrides changed source or repeat a target.");
            foreach (var (items, signature) in new[] { (snapshot.Perks, "PERK"), (snapshot.Factions, "FACT") })
            {
                var seen = new HashSet<FalloutFormKey>();
                foreach (var item in items)
                    if (item is null || !seen.Add(item.Form) || item.Value < (signature == "FACT" ? -1 : 0) || item.Value > (signature == "FACT" ? 127 : 1) ||
                        !FormOverride(item.Form, signature, item.Value).Sha256.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Saved actor perk/faction override is invalid or differs from source.");
            }
            if (snapshot.CombatStyle is { } style &&
                (style.Value != 0 || !FormOverride(style.Form, "CSTY", 0).Sha256.Equals(style.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Saved combat style differs from source.");
        }
        _actorOverrides.Clear();
        foreach (var (key, value) in admitted) _actorOverrides.Add(key, value);
    }

    internal void ChangePerk(FalloutFormKey reference, FalloutFormKey perk, bool add)
    {
        var current = Overrides(reference);
        var entry = FormOverride(perk, "PERK", add ? 1 : 0);
        if (add) _ = _perkAbilities.Perk(perk);
        _actorOverrides[reference] = current with { Perks = current.Perks.Where(item => item.Form != perk).Append(entry).ToArray() };
    }

    internal IReadOnlyList<FalloutFormKey> AcquiredPerks(FalloutFormKey reference)
    {
        var result = new Dictionary<FalloutFormKey, int>();
        var source = records.GetEffective(ActorBase(reference));
        foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "PRKR"))
        {
            var data = field.Data.Span;
            if (data.Length != 8 || data[4] > 1) throw new NotSupportedException("Actor perk rank extent/rank is unbound.");
            result[source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data))] = data[4];
        }
        foreach (var item in _actorOverrides.GetValueOrDefault(reference)?.Perks ?? []) result[item.Form] = item.Value;
        return result.Where(item => item.Value > 0).Select(item => item.Key).ToArray();
    }

    internal IEnumerable<FalloutPerkEntry> PerkEntries(FalloutFormKey reference) =>
        AcquiredPerks(reference).SelectMany(perk => _perkAbilities.Perk(perk).Entries);

    internal IEnumerable<FalloutAbilityModifier> PerkModifiers(FalloutFormKey reference) =>
        AcquiredPerks(reference).SelectMany(perk => _perkAbilities.Perk(perk).Spells).Distinct().SelectMany(_perkAbilities.Spell);

    internal void ChangeFaction(FalloutFormKey reference, FalloutFormKey faction, int rank, bool changeBase)
    {
        if (rank is < -1 or > 127) throw new InvalidDataException("Faction rank is outside its signed-byte range.");
        var entry = FormOverride(faction, "FACT", rank);
        var current = Overrides(reference);
        var baseKey = ActorBase(reference);
        var baseState = changeBase ? Overrides(baseKey) : null;
        _actorOverrides[reference] = current with { Factions = current.Factions.Where(item => item.Form != faction).Append(entry).ToArray() };
        if (baseState is not null)
            _actorOverrides[baseKey] = baseState with { Factions = baseState.Factions.Where(item => item.Form != faction).Append(entry).ToArray() };
    }

    internal IReadOnlyDictionary<FalloutFormKey, sbyte> ActorFactions(FalloutFormKey reference)
    {
        var actor = ActorBase(reference);
        var selection = reference == records.RuntimeFormKey(0x14) ? null : Actor(reference).Templates;
        var result = new Dictionary<FalloutFormKey, sbyte>(FalloutAiPackages.ReadFactions(records, actor, selection));
        foreach (var target in new[] { actor, reference })
            foreach (var item in _actorOverrides.GetValueOrDefault(target)?.Factions ?? []) result[item.Form] = checked((sbyte)item.Value);
        return result;
    }

    internal uint ActorRelation(FalloutFormKey actor, FalloutFormKey target) =>
        FalloutActorThreat.Relation(records, ActorFactions(actor), ActorFactions(target));

    internal void SetActorFlag(FalloutFormKey reference, bool value, bool friendlyHits)
    {
        var current = Overrides(reference);
        _actorOverrides[reference] = friendlyHits ? current with { IgnoreFriendlyHits = value } : current with { IgnoreCrime = value };
    }

    internal bool IgnoresCrime(FalloutFormKey reference) => _actorOverrides.GetValueOrDefault(reference)?.IgnoreCrime == true;
    internal bool IgnoresFriendlyHits(FalloutFormKey reference) => _actorOverrides.GetValueOrDefault(reference)?.IgnoreFriendlyHits == true;

    internal void SetCombatStyle(FalloutFormKey reference, FalloutFormKey style)
    {
        var current = Overrides(reference);
        _ = FalloutCombatStyle.Read(records.GetEffective(style));
        _actorOverrides[reference] = current with { CombatStyle = FormOverride(style, "CSTY", 0) };
    }

    internal FalloutCombatStyle? CombatStyle(FalloutFormKey reference)
    {
        if (_actorOverrides.GetValueOrDefault(reference)?.CombatStyle is { } changed)
            return FalloutCombatStyle.Read(records.GetEffective(changed.Form));
        var instance = Actor(reference);
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(instance.Base), 16, instance.Templates);
        var links = source.ReadSubrecords().Where(field => field.Signature == "ZNAM").ToArray();
        if (links.Length == 0) return null;
        if (links.Length != 1 || links[0].Data.Length != 4) throw new InvalidDataException("Actor combat-style link is invalid.");
        return source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span)) is { } form
            ? FalloutCombatStyle.Read(records.GetEffective(form)) : null;
    }
}
