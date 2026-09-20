using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFollowPackage(FalloutFormKey Form, FalloutFormKey Target, int Distance, uint Flags)
{
    internal static FalloutFollowPackage Read(FalloutPluginRecord record)
    {
        if (record.Signature != "PACK") throw new InvalidDataException("Follow source is not PACK.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Required(string name, int size)
        {
            var found = fields.Where(field => field.Signature == name).ToArray();
            return found.Length == 1 && found[0].Data.Length == size ? found[0].Data :
                throw new InvalidDataException($"Follow package {record.FormKey} has invalid {name}.");
        }
        var data = Required("PKDT", 12).Span;
        if (data[4] != 1) throw new InvalidDataException("Package procedure is not Follow.");
        if (fields.Any(field => field.Signature is "PLDT" or "PLD2"))
            throw new NotSupportedException("Follow start/end locations require their travel/completion owner.");
        var target = Required("PTDT", 16).Span;
        if (BinaryPrimitives.ReadInt32LittleEndian(target) != 0)
            throw new NotSupportedException("Follow target requires its non-reference selection owner.");
        var key = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(target[4..]));
        var distance = BinaryPrimitives.ReadInt32LittleEndian(target[8..]);
        if (distance <= 0) throw new InvalidDataException("Follow distance must be positive.");
        return new(record.FormKey, key, distance, BinaryPrimitives.ReadUInt32LittleEndian(data));
    }
}
