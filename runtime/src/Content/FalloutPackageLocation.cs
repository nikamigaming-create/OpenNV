using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// A decoded location is a declaration, not an executable search policy.
// Preserve the raw word for location kinds whose interpretation is unknown.
internal sealed record FalloutPackageLocation(int Type, uint RawLocation, FalloutFormKey? Form, int Radius)
{
    internal static FalloutPackageLocation? Read(FalloutPluginRecord record, string signature = "PLDT")
    {
        if (signature is not ("PLDT" or "PLD2")) throw new ArgumentException("Invalid package location signature.", nameof(signature));
        var locations = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (locations.Length == 0) return null;
        if (locations.Length != 1 || locations[0].Data.Length != 12)
            throw new InvalidDataException($"Package requires at most one complete {signature} location declaration.");
        var data = locations[0].Data.Span;
        var type = BinaryPrimitives.ReadInt32LittleEndian(data);
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var radius = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        if (radius < 0) throw new InvalidDataException("Package location radius is negative.");
        var form = type is 0 or 1 or 4 ? record.Plugin.AdjustOptionalFormId(raw) : null;
        if (type == 0 && form is null) throw new InvalidDataException("Near-reference package location has no reference.");
        return new(type, raw, form, radius);
    }
}
