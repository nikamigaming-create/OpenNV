using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorThreat(FalloutFormKey Owner, byte Aggression, byte Confidence,
    byte Assistance, bool RadiusBehavior, int Radius)
{
    internal static FalloutActorThreat Read(FalloutPluginStack records, FalloutFormKey actor)
    {
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor), 16);
        var data = owner.ReadSubrecords().Single(field => field.Signature == "AIDT").Data;
        return Decode(owner.FormKey, data.Span);
    }

    internal static FalloutActorThreat Decode(FalloutFormKey owner, ReadOnlySpan<byte> data)
    {
        if (data.Length != 20) throw new NotSupportedException("Actor AIDT extent is unbound.");
        var result = new FalloutActorThreat(owner, data[0], data[1], data[14], (data[15] & 1) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(data[16..]));
        if (result.Aggression > 3 || result.Confidence > 4 || result.Assistance > 2 || (data[15] & ~1) != 0 || result.Radius < 0)
            throw new InvalidDataException("Actor threat parameters are invalid.");
        return result;
    }

    // FACT group combat reactions: neutral, enemy, ally, friend. Aggression
    // and faction membership come from independent template groups.
    internal bool Initiates(uint relation) => relation > 3 ? throw new ArgumentOutOfRangeException(nameof(relation)) :
        Confidence != 0 && (Aggression == 3 || Aggression == 2 && relation < 2 || Aggression == 1 && relation == 1);

    internal static uint Relation(FalloutPluginStack records, FalloutFormKey actor, FalloutFormKey target)
    {
        var from = FalloutAiPackages.ReadFactions(records, actor).Where(pair => pair.Value >= 0).Select(pair => pair.Key).ToArray();
        var to = FalloutAiPackages.ReadFactions(records, target).Where(pair => pair.Value >= 0).Select(pair => pair.Key).ToHashSet();
        var reactions = new HashSet<uint>();
        if (from.Any(to.Contains)) reactions.Add(2);
        foreach (var faction in from)
        {
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
        if (reactions.Contains(1) && reactions.Any(value => value >= 2))
            throw new NotSupportedException("Conflicting enemy and friendly factions require their disposition priority owner.");
        return reactions.Count == 0 ? 0 : reactions.Max();
    }
}
