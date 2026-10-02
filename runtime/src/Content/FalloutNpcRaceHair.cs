using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutNpcRaceHair
{
    internal static FalloutFormKey? Select(FalloutPluginStack records, FalloutPluginRecord race, bool female, FalloutFormKey? chosen)
    {
        if (chosen is null) return null;
        var fields = race.ReadSubrecords().Where(field => field.Signature == "HNAM").ToArray();
        if (fields.Length > 1 || fields.Length == 1 && fields[0].Data.Length % 4 != 0)
            throw new InvalidDataException("Race hair list is malformed.");
        var allowed = fields.Length == 0 ? [] : Enumerable.Range(0, fields[0].Data.Length / 4)
            .Select(index => race.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span[(index * 4)..]))).ToArray();
        if (allowed.Contains(chosen.Value) && Eligible(chosen.Value)) return chosen;
        var defaults = race.ReadSubrecords().Where(field => field.Signature == "DNAM").ToArray();
        if (defaults.Length == 0) return null;
        if (defaults.Length != 1 || defaults[0].Data.Length != 8) throw new InvalidDataException("Race default hair is malformed.");
        var hair = race.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(defaults[0].Data.Span[(female ? 4 : 0)..]));
        if (hair is { } value && !Eligible(value)) throw new InvalidDataException("Race default hair excludes the actor's sex.");
        return hair;

        bool Eligible(FalloutFormKey hair)
        {
            var source = records.GetEffective(hair);
            var data = source.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
            if (source.Signature != "HAIR" || data.Length != 1 || data[0].Data.Length != 1)
                throw new InvalidDataException("Race hair requires HAIR with its source flags.");
            return (data[0].Data.Span[0] & (female ? 4 : 2)) == 0;
        }
    }
}
