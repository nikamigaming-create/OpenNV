using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutPackageData(uint Flags, byte Procedure, ushort BehaviorFlags,
    ushort? SpecificFlags)
{
    internal int SourceExtent => SpecificFlags is null ? 8 : 12;

    // Runtime package kinds which reserve the listener for their own response.
    // Get-up and search-for-attacker are not members of this source table;
    // their actual physical/combat state is checked independently.
    internal bool BlocksAmbientConversation => Procedure is >= 18 and <= 25 or
        >= 27 and <= 29 or >= 31 and <= 36;

    // Public condition110 codes describe the actual current process package,
    // not its editor PACK discriminator. Known absence is the native result0;
    // unresolved ownership must be refused by the caller before reaching here.
    internal static int CurrentAiPackageCode(byte? sourceType) => sourceType switch
    {
        null or 0 => 0,
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        5 => 13,
        6 => 14,
        7 => 15,
        8 => 16,
        9 => 17,
        10 => 18,
        11 => 19,
        12 => 36,
        13 => 37,
        14 => 35,
        15 => 34,
        16 => 33,
        18 => 5,
        19 => 20,
        20 => 7,
        21 => 8,
        22 => 10,
        23 => 11,
        24 => 9,
        25 => 12,
        26 => 21,
        27 => 24,
        28 => 6,
        29 => 28,
        30 => 29,
        31 => 32,
        _ => -1,
    };

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
