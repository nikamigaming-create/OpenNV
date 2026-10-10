using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRoomMembership(FalloutFormKey Reference, FalloutFormKey? Room, string SourceSha256)
{
    internal static FalloutRoomMembership Read(FalloutPluginStack records, FalloutFormKey reference)
    {
        var record = records.GetEffective(reference);
        if (record.Signature is not ("REFR" or "ACHR" or "ACRE"))
            throw new InvalidDataException("Room member is not an original placed reference.");
        var rows = record.ReadSubrecords().Where(field => field.Signature == "XMBR").ToArray();
        if (rows.Length > 1 || rows.Length == 1 && rows[0].Data.Length != sizeof(uint))
            throw new InvalidDataException("Source XMBR is duplicated or has an invalid extent.");
        var room = rows.Length == 0 ? null : record.Plugin.AdjustOptionalFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(rows[0].Data.Span));
        if (rows.Length == 1 && room is null)
            throw new InvalidDataException("Present source XMBR has no original room target.");
        if (room is { } target && records.GetEffective(target).Signature != "REFR")
            throw new InvalidDataException("Source XMBR target is not a winning placed room reference.");
        return new(reference, room, Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant());
    }
}
