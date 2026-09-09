using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMapMarker(FalloutFormKey Reference, string Name, byte Flags, byte Type,
    FalloutFormKey? Reputation)
{
    internal static FalloutMapMarker Read(FalloutPluginRecord reference)
    {
        if (reference.Signature != "REFR") throw new InvalidDataException("Map marker is not a placed reference.");
        var fields = reference.ReadSubrecords().ToArray();
        byte[] Required(string signature, int? extent = null)
        {
            var matches = fields.Where(field => field.Signature == signature).ToArray();
            return matches.Length == 1 && (extent is null || matches[0].Data.Length == extent)
                ? matches[0].Data.ToArray() : throw new InvalidDataException($"Map marker {reference.FormKey} has invalid {signature}.");
        }
        _ = Required("XMRK", 0);
        var flags = Required("FNAM", 1)[0];
        if ((flags & ~7) != 0) throw new NotSupportedException($"Map marker {reference.FormKey} has unsupported flags.");
        var type = Required("TNAM", 2)[0]; // The second byte is unused source padding.
        var reputations = fields.Where(field => field.Signature == "WMI1").ToArray();
        if (reputations.Length > 1 || reputations.Length == 1 && reputations[0].Data.Length != 4)
            throw new InvalidDataException($"Map marker {reference.FormKey} has invalid reputation data.");
        return new(reference.FormKey, FalloutDialogueTopic.Text(Required("FULL")), flags, type,
            reputations.Length == 0 ? null : reference.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(reputations[0].Data.Span)));
    }
}

internal sealed record FalloutMapMarkerState(bool Visible, bool CanTravel);
