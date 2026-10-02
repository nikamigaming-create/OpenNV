using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal static int RaceAgeSteps(double value) => double.IsFinite(value) && Math.Truncate(value) is >= int.MinValue and <= int.MaxValue
        ? (int)Math.Truncate(value) : throw new InvalidDataException("Race age steps are outside their signed-integer domain.");

    internal bool AgeRace(FalloutFormKey target, int steps)
    {
        if (steps == 0) return false;
        var baseKey = target == records.RuntimeFormKey(0x14) ? records.RuntimeFormKey(7) : Get(target).Base;
        if (records.GetEffective(baseKey).Signature != "NPC_") return false;
        var race = ActorRace(target);
        var visited = new Dictionary<FalloutFormKey, long> { [race] = 0 };
        long traversed = 0;
        var remaining = Math.Abs((long)steps);
        var link = steps < 0 ? "YNAM" : "ONAM";
        while (remaining-- > 0)
        {
            if (RaceLink(RequireRace(race), link) is not { } next) break;
            _ = RequireRace(next);
            race = next;
            traversed++;
            if (visited.TryGetValue(race, out var first))
            {
                remaining %= traversed - first;
                visited.Clear();
            }
            visited[race] = traversed;
        }
        return ChangeActorRace(target, race);
    }

    private bool ActorFemale(FalloutFormKey reference, FalloutPluginRecord traits)
    {
        if (reference == records.RuntimeFormKey(0x14) && _playerAppearance?.Invoke().Female is { } female) return female;
        var data = traits.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
        if (data.Length != 24) throw new InvalidDataException("NPC traits extent is invalid.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(data) & 1) != 0;
    }

    internal float ActorHeight(FalloutFormKey reference)
    {
        var (source, traits) = NpcAppearanceSource(reference);
        if ((_actorOverrides.GetValueOrDefault(source.FormKey)?.Height ?? _actorOverrides.GetValueOrDefault(traits.FormKey)?.Height) is { } height)
            return height;
        var fields = traits.ReadSubrecords().Where(field => field.Signature == "NAM6").ToArray();
        if (fields.Length > 1) throw new InvalidDataException("NPC height is duplicated.");
        return FalloutNpcHeight.Normalize(fields.Length == 0 ? [] : fields[0].Data.Span,
            FalloutNpcHeight.Race(RequireRace(ActorRace(reference)), ActorFemale(reference, traits)));
    }

    private bool ChangeActorRace(FalloutFormKey target, FalloutFormKey race)
    {
        var (source, traits) = NpcAppearanceSource(target);
        var before = ActorRace(target);
        if (before == race) return false;
        var female = ActorFemale(target, traits);
        var previousHeight = FalloutNpcHeight.Race(RequireRace(before), female);
        var nextHeight = FalloutNpcHeight.Race(RequireRace(race), female);
        var height = ActorHeight(target);
        var entry = FormOverride(race, "RACE", 0);
        // A custom height is retained. A later transition may follow race
        // height again if the stored value equals that transition's old race.
        var current = Overrides(source.FormKey);
        var player = target == records.RuntimeFormKey(0x14);
        var model = FalloutActorTemplateOwner.Resolve(records, source, 64, player ? null : Actor(target).Templates);
        var chosenHair = player ? _playerAppearance!().Hair : current.Hair is { } savedHair ? savedHair.Form : RaceLink(model, "HNAM");
        var hair = FalloutNpcRaceHair.Select(records, RequireRace(race), female, chosenHair);
        _actorOverrides[source.FormKey] = current with
        {
            Race = entry,
            Height = height == previousHeight ? nextHeight : height,
            Hair = new FalloutActorHairOverride(hair, hair is { } form ? RecordHash(records.GetEffective(form)) : null),
        };
        _appearanceRevisions[source.FormKey] = ++_appearanceRevision;
        return true;
    }

    internal void InvalidateActorAppearance(FalloutFormKey reference)
    {
        _ = NpcAppearanceSource(reference);
        _appearanceRevisions[reference] = ++_appearanceRevision;
    }
}
