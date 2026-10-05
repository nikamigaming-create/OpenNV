using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutPackageData(uint Flags, byte Procedure, ushort BehaviorFlags,
    ushort? SpecificFlags)
{
    internal int SourceExtent => SpecificFlags is null ? 8 : 12;

    internal static FalloutPackageData Read(FalloutPluginRecord record)
    {
        if (record.Signature != "PACK") throw new InvalidDataException("Package data source is not PACK.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "PKDT").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length is not (8 or 12))
            throw new InvalidDataException($"PACK {record.FormKey} requires one 8- or 12-byte PKDT.");
        var data = fields[0].Data.Span;
        // The legacy form ends after behavior flags. Its type-specific flags
        // are absent; byte 5 and the final two bytes of the longer form are unused.
        return new(BinaryPrimitives.ReadUInt32LittleEndian(data), data[4],
            BinaryPrimitives.ReadUInt16LittleEndian(data[6..]),
            data.Length == 12 ? BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) : null);
    }
}
