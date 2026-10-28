using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Preserve every target field independently of the package procedure that
// consumes it. Unknown target kinds and categories are declarations, not
// permission to execute them; individual owners must admit their semantics.
internal sealed record FalloutPackageTarget(int Type, uint RawTarget, FalloutFormKey? Form,
    int CountOrDistance, uint AuxiliaryBits)
{
    internal static FalloutPackageTarget Read(FalloutPluginRecord record, string signature = "PTDT")
    {
        if (signature is not ("PTDT" or "PTD2")) throw new ArgumentException("Invalid package target signature.", nameof(signature));
        var targets = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (targets.Length != 1 || targets[0].Data.Length != 16)
            throw new InvalidDataException($"Package requires one complete {signature} target declaration.");
        var data = targets[0].Data.Span;
        var type = BinaryPrimitives.ReadInt32LittleEndian(data);
        var target = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var form = type is 0 or 1 ? record.Plugin.AdjustOptionalFormId(target) : null;
        return new(type, target, form, BinaryPrimitives.ReadInt32LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]));
    }
}
