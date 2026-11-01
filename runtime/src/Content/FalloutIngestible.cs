using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIngestibleEffect(int Index, FalloutFormKey Form, string Hash,
    uint Archetype, int ActorValue, uint Flags, float Magnitude, uint Duration,
    uint Area, uint Range, bool AdditionalPresentation, IReadOnlyList<FalloutCondition> Conditions, int Resistance = -1, FalloutFormKey? Script = null)
{
    internal uint EffectiveDuration => (Flags & 0x80) == 0 ? Duration : 0;
}
internal sealed record FalloutIngestible(FalloutFormKey Form, string Hash, byte Flags,
    FalloutFormKey? Script, FalloutFormKey? Withdrawal, float AddictionChance,
    FalloutFormKey? Sound, IReadOnlyList<FalloutIngestibleEffect> Effects)
{
    internal static FalloutIngestible Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature is not ("ALCH" or "INGR")) throw new InvalidDataException("Consumed item is not an ingestible or ingredient.");
        var fields = record.ReadSubrecords().ToArray();
        var header = fields.Single(field => field.Signature == "ENIT").Data.Span;
        if (header.Length != (record.Signature == "INGR" ? 8 : 20))
            throw new NotSupportedException("Ingestible ENIT has an unsupported declaration extent.");
        if (record.Signature == "INGR" && (header[4] & ~3) != 0)
            throw new NotSupportedException("Ingredient flags require their additional source behavior owner.");
        var chance = record.Signature == "INGR" ? 0 : BinaryPrimitives.ReadSingleLittleEndian(header[12..]);
        if (!float.IsFinite(chance) || chance < 0) throw new InvalidDataException("Ingestible addiction chance is invalid.");
        var script = fields.SingleOrDefault(field => field.Signature == "SCRI").Data;
        if (script.Length is not (0 or 4)) throw new InvalidDataException("Ingestible script link is invalid.");
        return new(form, HashOf(record), header[4], script.IsEmpty ? null :
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(script.Span)),
            record.Signature == "INGR" ? null : record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(header[8..])), chance,
            record.Signature == "INGR" ? null : record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(header[16..])), ReadEffects(records, record, fields));
    }

    // Actor effects and consumables share EFID/EFIT/MGEF declarations, but
    // actor effects do not carry ALCH skill scaling or consume an inventory item.
    internal static FalloutIngestible ReadActorEffect(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature != "SPEL") throw new InvalidDataException("Actor effect is not SPEL.");
        var fields = record.ReadSubrecords().ToArray();
        var declarations = fields.Where(field => field.Signature == "SPIT").ToArray();
        if (declarations.Length != 1 || declarations[0].Data.Length != 16)
            throw new InvalidDataException("Actor effect needs one 16-byte SPIT declaration.");
        var header = declarations[0].Data.Span;
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0 || header[12] != 0)
            throw new NotSupportedException("Actor effect type or flags require their spell lifecycle owner.");
        var effects = ReadEffects(records, record, fields);
        if (effects.Count == 0) throw new InvalidDataException("Actor effect has no source effects.");
        return new(form, HashOf(record), 0, null, null, 0, null, effects);
    }

    private static IReadOnlyList<FalloutIngestibleEffect> ReadEffects(FalloutPluginStack records,
        FalloutPluginRecord record, FalloutPluginSubrecord[] fields)
    {
        var effects = new List<FalloutIngestibleEffect>();
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].Signature != "EFID") continue;
            if (fields[index].Data.Length != 4) throw new InvalidDataException("Ingestible EFID extent is invalid.");
            var effect = records.GetEffective(record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[index].Data.Span)));
            if (effect.Signature != "MGEF") throw new InvalidDataException("Ingestible effect is not MGEF.");
            var end = index + 1;
            while (end < fields.Length && fields[end].Signature != "EFID") end++;
            var group = fields[(index + 1)..end];
            var data = group.Single(field => field.Signature == "EFIT").Data.Span;
            var definitionData = effect.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            var definition = definitionData.Span;
            if (data.Length != 20 || definition.Length != 72)
                throw new NotSupportedException("Ingestible effect requires EFIT(20)/MGEF DATA(72).");
            if (group.Any(field => field.Signature is not ("EFIT" or "CTDA")))
                throw new NotSupportedException("Ingestible effect has an unbound script/data extension.");
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(definition);
            var archetype = BinaryPrimitives.ReadUInt32LittleEndian(definition[64..]);
            var script = archetype == 1 ? effect.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(definition[8..])) : null;
            var presentation = archetype != 1 && BinaryPrimitives.ReadUInt32LittleEndian(definition[8..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(definition[20..]) != 0 ||
                new[] { 24, 32, 36, 40, 44, 48, 52 }.Any(offset =>
                    BinaryPrimitives.ReadUInt32LittleEndian(definitionData.Span[offset..]) != 0) ||
                effect.ReadSubrecords().Any(field => field.Signature == "MODL" && field.Data.Span.IndexOfAnyExcept((byte)0) >= 0);
            effects.Add(new(effects.Count, effect.FormKey, HashOf(effect), BinaryPrimitives.ReadUInt32LittleEndian(definition[64..]),
                BinaryPrimitives.ReadInt32LittleEndian((flags & 0x180000) != 0 ? data[16..] : definition[68..]), flags,
                BinaryPrimitives.ReadUInt32LittleEndian(data), BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[4..]), BinaryPrimitives.ReadUInt32LittleEndian(data[12..]), presentation,
                group.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(record, field.Data.Span)).ToArray(),
                BinaryPrimitives.ReadInt32LittleEndian(definition[16..]), script));
            index = end - 1;
        }
        return effects;
    }

    private static string HashOf(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
