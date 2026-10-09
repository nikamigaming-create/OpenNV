using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutActorOverrides> _actorOverrides = [];
    private readonly FalloutAbilityModifiers _perkAbilities = new(records);
    internal Func<IReadOnlyList<FalloutFormKey>>? PlayerTraitSelection { get; set; }
    internal Action<FalloutPerkQuestStage>? ExecutePerkQuestStage { get; set; }
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

    internal IReadOnlyList<FalloutActorOverrides> CaptureActorOverrides() => _actorOverrides.Values.Select(CloneFace).ToArray();

    private static FalloutActorOverrides CloneFace(FalloutActorOverrides source) => source.FaceGeometry is not { } face ? source :
        source with { FaceGeometry = face with { SymmetricGeometry = face.SymmetricGeometry.ToArray(), AsymmetricGeometry = face.AsymmetricGeometry.ToArray() } };

    internal void RestoreActorOverrides(IReadOnlyList<FalloutActorOverrides>? snapshots)
    {
        var admitted = new Dictionary<FalloutFormKey, FalloutActorOverrides>();
        foreach (var snapshot in snapshots ?? [])
        {
            if (snapshot is null || snapshot.Perks is null || snapshot.Factions is null ||
                !RecordHash(ActorOverrideSource(snapshot.Target)).Equals(snapshot.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                !admitted.TryAdd(snapshot.Target, snapshot)) throw new InvalidDataException("Saved actor overrides changed source or repeat a target.");
            if (snapshot.Alerted is not null) RequireAlertActor(snapshot.Target);
            if (snapshot.TeammatePerks is { Count: > 0 } && snapshot.Target != records.RuntimeFormKey(0x14))
                throw new InvalidDataException("Shared teammate perks must be retained on the player.");
            foreach (var (items, signature) in new[] { (snapshot.Perks, "PERK"), (snapshot.Factions, "FACT"),
                (snapshot.TeammatePerks ?? [], "PERK") })
            {
                var seen = new HashSet<FalloutFormKey>();
                foreach (var item in items)
                    if (item is null || !seen.Add(item.Form) || item.Value < (signature == "FACT" ? -1 : 0) ||
                        item.Value > (signature == "FACT" ? 127 : FalloutPerkDeclaration.Read(records.GetEffective(item.Form)).Ranks) ||
                        !FormOverride(item.Form, signature, item.Value).Sha256.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Saved actor perk/faction override is invalid or differs from source.");
            }
            if (snapshot.CombatStyle is { } style &&
                (style.Value != 0 || !FormOverride(style.Form, "CSTY", 0).Sha256.Equals(style.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Saved combat style differs from source.");
            if (snapshot.Race is { } race &&
                (ActorOverrideSource(snapshot.Target).Signature != "NPC_" || race.Value != 0 ||
                !FormOverride(race.Form, "RACE", 0).Sha256.Equals(race.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Saved actor race differs from its winning NPC/RACE source.");
            if (snapshot.Height is { } height)
            {
                if (ActorOverrideSource(snapshot.Target).Signature != "NPC_") throw new InvalidDataException("Saved height is not an NPC base override.");
                _ = FalloutNpcHeight.Require(height);
            }
            if (snapshot.Hair is { } hair && (ActorOverrideSource(snapshot.Target).Signature != "NPC_" ||
                (hair.Form is null ? hair.Sha256 is not null :
                    !FormOverride(hair.Form.Value, "HAIR", 0).Sha256.Equals(hair.Sha256, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Saved actor hair differs from its winning source.");
            if (snapshot.FaceGeometry is { } face) ValidateFaceGeometry(snapshot, face);
            admitted[snapshot.Target] = CloneFace(snapshot);
        }
        var changedAppearance = _actorOverrides.Where(item => item.Value.Race is not null || item.Value.FaceGeometry is not null || item.Value.Height is not null || item.Value.Hair is not null).Select(item => item.Key)
            .Concat(admitted.Where(item => item.Value.Race is not null || item.Value.FaceGeometry is not null || item.Value.Height is not null || item.Value.Hair is not null).Select(item => item.Key)).Distinct().ToArray();
        _actorOverrides.Clear();
        _alertRevisions.Clear();
        foreach (var (key, value) in admitted) _actorOverrides.Add(key, value);
        foreach (var key in changedAppearance) _appearanceRevisions[key] = ++_appearanceRevision;
    }

    internal void ChangePerk(FalloutFormKey reference, FalloutFormKey perk, bool add, bool forTeammates = false)
    {
        var maximum = FalloutPerkDeclaration.Read(records.GetEffective(perk)).Ranks;
        if (maximum == 0) throw new InvalidDataException($"Perk {perk} has no declared acquired rank.");
        SetPerkRank(reference, perk,
            add ? Math.Min(checked(PerkRank(reference, perk, forTeammates) + 1), maximum) : 0, forTeammates);
    }

    internal void SetPerkRank(FalloutFormKey reference, FalloutFormKey perk, int rank, bool forTeammates = false)
    {
        var maximum = FalloutPerkDeclaration.Read(records.GetEffective(perk)).Ranks;
        if (rank < 0 || rank > maximum) throw new InvalidDataException($"Perk {perk} acquired rank is outside its winning declaration.");
        if (PerkRank(reference, perk, forTeammates) == rank) return;
        var current = Overrides(reference);
        var entry = FormOverride(perk, "PERK", rank);
        var stages = rank == 0 ? [] : _perkAbilities.Perk(perk, rank).QuestStages;
        if (stages.Length != 0 && ExecutePerkQuestStage is null)
            throw new NotSupportedException($"Perk {perk} requires its actual shared quest-stage executor.");
        if (forTeammates && reference == records.RuntimeFormKey(0x14))
            _actorOverrides[reference] = current with
            { TeammatePerks = (current.TeammatePerks ?? []).Where(item => item.Form != perk).Append(entry).ToArray() };
        else
            _actorOverrides[reference] = current with { Perks = current.Perks.Where(item => item.Form != perk).Append(entry).ToArray() };
        foreach (var stage in stages.OrderByDescending(stage => stage.Priority)) ExecutePerkQuestStage!(stage);
    }

    internal int PerkRank(FalloutFormKey reference, FalloutFormKey perk, bool forTeammates = false)
    {
        _ = FalloutPerkDeclaration.Read(records.GetEffective(perk));
        return AcquiredPerkRanks(reference, forTeammates).GetValueOrDefault(perk);
    }

    internal IReadOnlyDictionary<FalloutFormKey, int> AcquiredPerkRanks(FalloutFormKey reference, bool forTeammates = false)
    {
        var player = records.RuntimeFormKey(0x14);
        var shared = _actorOverrides.GetValueOrDefault(player)?.TeammatePerks ?? [];
        if (forTeammates && reference == player)
            return shared.Where(item => item.Value > 0).ToDictionary(item => item.Form, item => item.Value);
        var result = new Dictionary<FalloutFormKey, int>();
        var source = records.GetEffective(ActorBase(reference));
        foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "PRKR"))
        {
            var data = field.Data.Span;
            if (data.Length != 8) throw new NotSupportedException("Actor perk rank extent is unbound.");
            var perk = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
            var maximum = FalloutPerkDeclaration.Read(records.GetEffective(perk)).Ranks;
            if (data[4] > maximum || !result.TryAdd(perk, data[4]))
                throw new InvalidDataException("Actor source perk rank is invalid or repeats a PERK.");
        }
        if (reference == records.RuntimeFormKey(0x14) && PlayerTraitSelection is { } traits)
            foreach (var trait in traits())
            {
                var declaration = FalloutPerkDeclaration.Read(records.GetEffective(trait));
                if (!declaration.Trait || declaration.Ranks == 0)
                    throw new InvalidDataException("Player trait selection differs from its winning PERK.");
                result[trait] = Math.Max(result.GetValueOrDefault(trait), 1);
            }
        foreach (var item in _actorOverrides.GetValueOrDefault(reference)?.Perks ?? []) result[item.Form] = item.Value;
        if (reference != player && Actor(reference).PlayerTeammate)
            foreach (var item in shared.Where(item => item.Value > 0)) result[item.Form] = item.Value;
        return result.Where(item => item.Value > 0).ToDictionary(item => item.Key, item => item.Value);
    }

    internal IReadOnlyList<FalloutFormKey> AcquiredPerks(FalloutFormKey reference, bool forTeammates = false) =>
        AcquiredPerkRanks(reference, forTeammates).Keys.ToArray();

    internal IEnumerable<FalloutPerkEntry> PerkEntries(FalloutFormKey reference) =>
        AcquiredPerkRanks(reference).SelectMany(perk => _perkAbilities.Perk(perk.Key, perk.Value).Entries)
            .OrderByDescending(entry => entry.Priority);

    internal IEnumerable<FalloutAbilityModifier> PerkModifiers(FalloutFormKey reference) =>
        AcquiredPerkRanks(reference).SelectMany(perk => _perkAbilities.Perk(perk.Key, perk.Value).Spells).Distinct().SelectMany(_perkAbilities.Spell);

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
        FalloutActorThreat.Relation(records, ActorFactions(actor), ActorFactions(target), FactionCombatReaction);

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
