using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Every template group uses the same resolver. A leveled list which has one
// possible outcome at every legal player level is an alias, not a random roll.
// Choices which depend on level, globals, chance or multiple candidates still
// require the reference's persistent encounter-selection owner.
internal static class FalloutActorTemplateOwner
{
    internal static FalloutPluginRecord Resolve(FalloutPluginStack stack, FalloutPluginRecord actor, ushort group)
    {
        var signature = actor.Signature;
        if (signature is not ("NPC_" or "CREA")) throw new InvalidDataException("Template owner is not an actor.");
        var visited = new HashSet<FalloutFormKey>();
        while (true)
        {
            if (actor.Signature != signature || !visited.Add(actor.FormKey))
                throw new InvalidDataException("Actor template has a cycle or changes actor type.");
            var acbs = Field(actor, "ACBS", 24);
            if ((BinaryPrimitives.ReadUInt16LittleEndian(acbs.Span[22..]) & group) == 0) return actor;
            actor = stack.GetEffective(actor.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(Field(actor, "TPLT", 4).Span)));
            while (actor.Signature == (signature == "NPC_" ? "LVLN" : "LVLC"))
            {
                if (!visited.Add(actor.FormKey)) throw new InvalidDataException("Leveled actor template contains a cycle.");
                actor = stack.GetEffective(InvariantChoice(actor));
            }
        }
    }

    private static FalloutFormKey InvariantChoice(FalloutPluginRecord list)
    {
        var chance = Field(list, "LVLD", 1).Span[0];
        var flags = Field(list, "LVLF", 1).Span[0];
        if (chance > 100 || (flags & ~3) != 0) throw new InvalidDataException("Leveled actor flags or chance are invalid.");
        var fields = list.ReadSubrecords().ToArray();
        var chanceGlobal = fields.SingleOrDefault(field => field.Signature == "LVLG").Data;
        if (!chanceGlobal.IsEmpty && (chanceGlobal.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(chanceGlobal.Span) != 0))
            throw new NotSupportedException($"Actor template {list.FormKey} requires its chance-global selection owner.");
        if (chance != 0) throw new NotSupportedException($"Actor template {list.FormKey} requires persistent chance-none selection.");
        var entries = fields.Where(field => field.Signature == "LVLO").Select(field =>
        {
            if (field.Data.Length != 12) throw new InvalidDataException("Leveled actor entry extent is invalid.");
            var data = field.Data.Span;
            return (Level: BinaryPrimitives.ReadUInt16LittleEndian(data),
                Form: list.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[4..])));
        }).ToArray();
        if (entries.Length == 0 || entries.Any(entry => entry.Level > 1))
            throw new NotSupportedException($"Actor template {list.FormKey} requires its persistent encounter-level selection owner.");
        if (fields.Any(field => field.Signature == "COED"))
            throw new NotSupportedException($"Actor template {list.FormKey} requires its entry extra-data owner.");
        var eligible = (flags & 1) != 0 ? entries : entries.Where(entry => entry.Level == entries.Max(item => item.Level)).ToArray();
        var choices = eligible.Select(entry => entry.Form).Distinct().ToArray();
        return choices.Length == 1 ? choices[0] : throw new NotSupportedException(
            $"Actor template {list.FormKey} requires a persistent leveled choice among {choices.Length} candidates.");
    }

    private static ReadOnlyMemory<byte> Field(FalloutPluginRecord owner, string signature, int length)
    {
        var fields = owner.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        return fields.Length == 1 && fields[0].Data.Length == length ? fields[0].Data :
            throw new InvalidDataException($"Actor template {owner.FormKey} has invalid {signature} data.");
    }
}
