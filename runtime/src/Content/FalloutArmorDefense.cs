using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutArmorDefense(float Threshold, float Resistance)
{
    internal static FalloutArmorDefense Read(FalloutPluginRecord record)
    {
        if (record.Signature != "ARMO") throw new InvalidDataException("Armor defense requires an ARMO record.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
        if (data.Length is not (4 or 12)) throw new NotSupportedException("Armor defense layout is unbound.");
        var resistance = BinaryPrimitives.ReadInt16LittleEndian(data) / 100f;
        var threshold = data.Length == 12 ? BinaryPrimitives.ReadSingleLittleEndian(data[4..]) : 0;
        if (!float.IsFinite(threshold) || threshold < 0 || resistance < 0)
            throw new InvalidDataException("Armor defense is invalid.");
        return new(threshold, resistance);
    }
}
