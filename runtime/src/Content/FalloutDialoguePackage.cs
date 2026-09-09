using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDialoguePackage(FalloutFormKey Form, FalloutFormKey Target, int ActivationDistance,
    float FieldOfView, FalloutFormKey? Topic, uint Flags, uint Type, uint TrailingData)
{
    internal static FalloutDialoguePackage Read(FalloutPluginRecord record)
    {
        if (record.Signature != "PACK") throw new InvalidDataException("Dialogue package is not PACK.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Required(string name, int size)
        {
            var values = fields.Where(field => field.Signature == name).ToArray();
            return values.Length == 1 && values[0].Data.Length == size ? values[0].Data :
                throw new InvalidDataException($"Dialogue package {name} extent is invalid.");
        }
        if (Required("PKDT", 12).Span[4] != 15) throw new InvalidDataException("Package procedure is not Dialogue.");
        var target = Required("PTDT", 16).Span;
        if (BinaryPrimitives.ReadInt32LittleEndian(target) != 0)
            throw new NotSupportedException("Dialogue target selection requires its non-reference owner.");
        var distance = BinaryPrimitives.ReadInt32LittleEndian(target[8..]);
        if (distance <= 0) throw new InvalidDataException("Dialogue activation distance is not positive.");
        var data = Required("PKDD", 24).Span;
        var fov = BinaryPrimitives.ReadSingleLittleEndian(data);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        if (!float.IsFinite(fov) || fov <= 0 || fov >= 180 || (flags & ~0x101u) != 0 || type > 1)
            throw new NotSupportedException("Dialogue package FOV, flags or type is unbound.");
        return new(record.FormKey, record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(target[4..])), distance,
            fov, record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[4..])), flags, type,
            BinaryPrimitives.ReadUInt32LittleEndian(data[20..]));
    }
}
