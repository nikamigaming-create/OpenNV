using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private Func<FalloutActorAppearanceState>? _playerAppearance;
    private readonly Dictionary<FalloutFormKey, long> _appearanceRevisions = [];
    private long _appearanceRevision;
    internal long AppearanceRevision => _appearanceRevision;

    internal void BindPlayerAppearance(Func<FalloutActorAppearanceState> read) => _playerAppearance = read;

    private (FalloutPluginRecord Base, FalloutPluginRecord Traits) NpcAppearanceSource(FalloutFormKey reference)
    {
        var source = records.GetEffective(ActorBase(reference));
        if (source.Signature != "NPC_") throw new InvalidDataException("Race changes require an NPC actor.");
        var selection = reference == records.RuntimeFormKey(0x14) ? null : Actor(reference).Templates;
        return (source, FalloutActorTemplateOwner.Resolve(records, source, 1, selection));
    }

    internal FalloutActorAppearanceState? ActorAppearanceOverride(FalloutFormKey reference)
    {
        var (source, traits) = NpcAppearanceSource(reference);
        var changed = _actorOverrides.GetValueOrDefault(source.FormKey)?.Race ?? _actorOverrides.GetValueOrDefault(traits.FormKey)?.Race;
        var face = _actorOverrides.GetValueOrDefault(source.FormKey)?.FaceGeometry;
        return changed is null && face is null ? null : new(null, changed?.Form, null, null, face is null ? null : ActorFace(reference));
    }

    internal long ActorAppearanceRevision(FalloutFormKey reference)
    {
        var (source, traits) = NpcAppearanceSource(reference);
        return Math.Max(_appearanceRevisions.GetValueOrDefault(source.FormKey), _appearanceRevisions.GetValueOrDefault(traits.FormKey));
    }

    internal FalloutFormKey ActorRace(FalloutFormKey reference)
    {
        if (ActorAppearanceOverride(reference)?.Race is { } changed) return changed;
        if (reference == records.RuntimeFormKey(0x14))
            return (_playerAppearance ?? throw new NotSupportedException("The current player appearance owner is absent."))().Race ??
                throw new InvalidDataException("Current player appearance has no race.");
        return RaceLink(NpcAppearanceSource(reference).Traits, "RNAM") ?? throw new InvalidDataException("NPC traits have no race.");
    }

    // MatchRace preserves the target's age tier within the source race family.
    // Changes belong to the NPC base and outlive every resident 3D instance.
    internal bool MatchRace(FalloutFormKey target, FalloutFormKey source)
    {
        var targetSource = NpcAppearanceSource(target).Base;
        _ = NpcAppearanceSource(source);
        var before = ActorRace(target); var from = ActorRace(source);
        _ = RequireRace(before); _ = RequireRace(from);
        if (before == from) return false;
        if (target == records.RuntimeFormKey(0x14))
            throw new NotSupportedException("Scripted player race changes require the shared character-identity transition owner.");
        var (_, age) = YoungestRace(before);
        var (race, _) = YoungestRace(from);
        var visited = new HashSet<FalloutFormKey> { race };
        for (var index = 0; index < age; index++)
        {
            if (RaceLink(RequireRace(race), "ONAM") is not { } older) break;
            _ = RequireRace(older);
            if (!visited.Add(older)) throw new InvalidDataException("Race older-family links contain a cycle.");
            race = older;
        }
        var entry = FormOverride(race, "RACE", 0);
        var current = Overrides(targetSource.FormKey);
        _actorOverrides[targetSource.FormKey] = current with { Race = entry };
        _appearanceRevisions[targetSource.FormKey] = ++_appearanceRevision;
        return true;
    }

    private (FalloutFormKey Race, int Age) YoungestRace(FalloutFormKey race)
    {
        var visited = new HashSet<FalloutFormKey>(); var age = 0;
        while (true)
        {
            if (!visited.Add(race)) throw new InvalidDataException("Race younger-family links contain a cycle.");
            if (RaceLink(RequireRace(race), "YNAM") is not { } younger) return (race, age);
            race = younger; age++;
        }
    }

    private FalloutPluginRecord RequireRace(FalloutFormKey race)
    {
        var record = records.GetEffective(race);
        return record.Signature == "RACE" ? record : throw new InvalidDataException("Actor race/family link is not RACE.");
    }

    private static FalloutFormKey? RaceLink(FalloutPluginRecord source, string signature)
    {
        var rows = source.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (rows.Length == 0) return null;
        if (rows.Length != 1 || rows[0].Data.Length != 4) throw new InvalidDataException($"Race graph {signature} link is malformed.");
        return source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(rows[0].Data.Span));
    }
}
