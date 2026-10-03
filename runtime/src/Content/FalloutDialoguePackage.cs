using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDialoguePackage(FalloutFormKey Form, FalloutFormKey Target, int ActivationDistance,
    float FieldOfView, FalloutFormKey? Topic, uint Flags, uint Type, uint TrailingData)
{
    internal uint PackageFlags { get; init; }
    internal FalloutDialogueTriggerLocation? TriggerLocation { get; init; }
    internal bool Running => (PackageFlags & 0x2000) != 0;
    internal bool WeaponDrawn => (PackageFlags & 0x800000) != 0;
    internal bool ControlsTargetMovement => (Flags & 0x100) == 0;

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
        var packageData = Required("PKDT", 12).Span;
        if (packageData[4] != 15) throw new InvalidDataException("Package procedure is not Dialogue.");
        _ = FalloutScriptPackage.Read(record);
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
        var topic = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
        if (type == 1 && topic is null) throw new InvalidDataException("SayTo dialogue package has no source topic.");
        return new(record.FormKey, record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(target[4..])), distance,
            fov, topic, flags, type, BinaryPrimitives.ReadUInt32LittleEndian(data[20..]))
        { PackageFlags = BinaryPrimitives.ReadUInt32LittleEndian(packageData), TriggerLocation = FalloutDialogueTriggerLocation.Read(record) };
    }
}

internal sealed record FalloutDialogueTriggerLocation(int Type, FalloutFormKey? Reference, int Radius)
{
    internal static FalloutDialogueTriggerLocation? Read(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == "PLD2").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Any(field => field.Data.Length != 12 || !field.Data.Span.SequenceEqual(fields[0].Data.Span)))
            throw new InvalidDataException("Dialogue trigger declarations differ or have an invalid extent.");
        var data = fields[0].Data.Span;
        var type = BinaryPrimitives.ReadInt32LittleEndian(data);
        var radius = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        if (type is not (0 or 2 or 3) || radius < 0)
            throw new NotSupportedException("Dialogue trigger location requires its location selection owner.");
        var reference = type == 0 ? record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[4..])) ??
            throw new InvalidDataException("Dialogue trigger has no source reference.") : (FalloutFormKey?)null;
        return new(type, reference, radius);
    }
}
