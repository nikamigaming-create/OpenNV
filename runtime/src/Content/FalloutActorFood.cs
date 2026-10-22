using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutActorFood
{
    // Food classification inspects declarations without executing their effects.
    internal static bool IsFood(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature is not ("ALCH" or "INGR")) return false;
        var fields = record.ReadSubrecords().ToArray();
        var headers = fields.Where(field => field.Signature == "ENIT").ToArray();
        var extent = record.Signature == "ALCH" ? 20 : 8;
        if (headers.Length != 1 || headers[0].Data.Length != extent)
            throw new InvalidDataException("Actor food has an invalid ENIT declaration.");
        if ((headers[0].Data.Span[4] & 2) == 0) return false;
        if (record.Signature == "INGR") return true;

        // An empty effect list is not poison. Otherwise an ingestible is poison
        // only when every effect is hostile or detrimental; conditions do not
        // execute during this classification.
        var effects = fields.Where(field => field.Signature == "EFID").ToArray();
        if (effects.Length == 0) return true;
        foreach (var effect in effects)
        {
            if (effect.Data.Length != 4) throw new InvalidDataException("Actor food EFID is invalid.");
            var definition = records.GetEffective(record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(effect.Data.Span)));
            if (definition.Signature != "MGEF") throw new InvalidDataException("Actor food effect is not MGEF.");
            var data = definition.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (data.Length != 72) throw new InvalidDataException("Actor food effect DATA is invalid.");
            if ((BinaryPrimitives.ReadUInt32LittleEndian(data.Span) & 0x400001) == 0) return true;
        }
        return false;
    }
}
