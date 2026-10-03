using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal enum FalloutRadioRange : uint { Radius, Everywhere, WorldspaceAndLinkedInteriors, LinkedInteriors, CurrentCell }

internal sealed record FalloutRadioStation(FalloutFormKey Reference, FalloutFormKey Base,
    FalloutRadioRange Range, float Radius, float StaticPercent, FalloutFormKey? PositionReference,
    bool PipBoy, bool Continuous)
{
    internal static FalloutRadioStation Read(FalloutPluginStack records, FalloutPluginRecord reference)
    {
        if (reference.Signature != "REFR") throw new InvalidDataException("Radio transmitter is not REFR.");
        var source = records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME"));
        if (source.Signature != "TACT" || (source.Flags & 0x20000) == 0)
            throw new InvalidDataException("Radio transmitter has no station TACT base.");
        var fields = reference.ReadSubrecords().Where(field => field.Signature == "XRDO").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 16)
            throw new NotSupportedException($"Radio transmitter {reference.FormKey} XRDO layout is unbound.");
        var data = fields[0].Data.Span;
        var range = (FalloutRadioRange)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var radius = BinaryPrimitives.ReadSingleLittleEndian(data);
        var percent = BinaryPrimitives.ReadSingleLittleEndian(data[8..]);
        if (!Enum.IsDefined(range) || !float.IsFinite(radius) || radius < 0 || !float.IsFinite(percent) || percent is < 0 or > 100)
            throw new InvalidDataException($"Radio transmitter {reference.FormKey} range/static data is invalid.");
        var position = reference.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[12..]));
        if (position is { } anchor && records.GetEffective(anchor).Signature is not ("REFR" or "ACHR" or "ACRE"))
            throw new InvalidDataException("Radio position anchor is not a placed reference.");
        return new(reference.FormKey, source.FormKey, range, radius, percent, position,
            (source.Flags & 0x10000000) == 0, (source.Flags & 0x40000000) != 0);
    }
}
