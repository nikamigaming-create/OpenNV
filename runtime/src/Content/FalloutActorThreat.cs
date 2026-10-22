using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorThreat(FalloutFormKey Owner, byte Aggression, byte Confidence,
    byte Assistance, bool RadiusBehavior, int Radius)
{
    internal byte Energy { get; init; }

    internal static FalloutActorThreat Read(FalloutPluginStack records, FalloutFormKey actor, FalloutActorTemplateSelection? selection = null)
    {
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor), 16, selection);
        var data = owner.ReadSubrecords().Single(field => field.Signature == "AIDT").Data;
        return Decode(owner.FormKey, data.Span);
    }

    internal static FalloutActorThreat Decode(FalloutFormKey owner, ReadOnlySpan<byte> data)
    {
        if (data.Length != 20) throw new NotSupportedException("Actor AIDT extent is unbound.");
        var result = new FalloutActorThreat(owner, data[0], data[1], data[14], (data[15] & 1) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(data[16..]))
        { Energy = data[2] };
        if (result.Aggression > 3 || result.Confidence > 4 || result.Assistance > 2 || (data[15] & ~1) != 0 || result.Radius < 0)
            throw new InvalidDataException("Actor threat parameters are invalid.");
        return result;
    }

    // FACT group combat reactions: neutral, enemy, ally, friend. Aggression
    // and faction membership come from independent template groups.
    internal bool Hostile(uint relation) => relation > 3 ? throw new ArgumentOutOfRangeException(nameof(relation)) :
        Aggression == 3 || Aggression == 2 && relation < 2 || Aggression == 1 && relation == 1;

    internal bool Initiates(uint relation) => relation > 3 ? throw new ArgumentOutOfRangeException(nameof(relation)) :
        Confidence != 0 && Hostile(relation);

    internal bool Warns(uint relation) => relation > 3 ? throw new ArgumentOutOfRangeException(nameof(relation)) :
        Aggression != 0 && Confidence != 0 && RadiusBehavior && Radius > 0 && relation < 2;

    internal static uint Relation(FalloutPluginStack records, FalloutFormKey actor, FalloutFormKey target,
        FalloutActorTemplateSelection? selection = null, FalloutActorTemplateSelection? targetSelection = null)
    {
        return Relation(records, FalloutAiPackages.ReadFactions(records, actor, selection), FalloutAiPackages.ReadFactions(records, target, targetSelection));
    }

    internal static uint Relation(FalloutPluginStack records, IReadOnlyDictionary<FalloutFormKey, sbyte> actor,
        IReadOnlyDictionary<FalloutFormKey, sbyte> target,
        Func<FalloutFormKey, FalloutFormKey, uint>? query = null)
    {
        var from = actor.Where(pair => pair.Value >= 0).Select(pair => pair.Key).ToArray();
        var to = target.Where(pair => pair.Value >= 0).Select(pair => pair.Key).ToHashSet();
        var reactions = new HashSet<uint>();
        foreach (var faction in from)
        {
            if (query is not null)
            {
                foreach (var other in to)
                {
                    var reaction = query(faction, other);
                    if (reaction > 3) throw new InvalidDataException("Faction combat relation is invalid.");
                    if (reaction != 0) reactions.Add(reaction);
                }
                continue;
            }
            var source = records.GetEffective(faction);
            foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "XNAM"))
            {
                if (field.Data.Length != 12) throw new NotSupportedException("Faction combat relation extent is unbound.");
                var data = field.Data.Span;
                var other = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
                var reaction = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                if (reaction > 3) throw new InvalidDataException("Faction combat relation is invalid.");
                if (to.Contains(other) && reaction != 0) reactions.Add(reaction);
            }
        }
        // Combat reactions have their own priority: ally, friend, enemy,
        // neutral. Keep validating every admitted pair before selecting it.
        return reactions.Contains(2) ? 2u : reactions.Contains(3) ? 3u : reactions.Contains(1) ? 1u : 0u;
    }
}
