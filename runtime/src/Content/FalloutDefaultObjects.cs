using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal enum FalloutDefaultObject
{
    Stimpak = 0,
    RadX = 2,
    RadAway = 3,
    DoctorsBag = 21
}

internal static class FalloutDefaultObjects
{
    internal static FalloutFormKey? Read(FalloutPluginStack records, FalloutDefaultObject slot)
    {
        var source = records.EffectiveRecords("DOBJ").Single();
        var data = source.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length is not (84 or 136))
            throw new NotSupportedException("Default-object DATA has an unqualified slot layout.");
        var offset = checked((int)slot * sizeof(uint));
        if (offset >= data.Length) return null;
        return source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]));
    }
}
