using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutImageSpace(
    FalloutFormKey Form, ushort FormVersion, string DnamSha256, float[] RawTraits,
    float? SkinDimmer, Vector4 Cinematic, Vector4 Tint, byte? CinematicFlags)
{
    internal float TargetLuminance => RawTraits[4];
    internal float BrightScale => RawTraits[6];
    internal float BrightClamp => RawTraits[7];
}

internal static class FalloutImageSpaceReader
{
    internal static FalloutImageSpace? ForCell(FalloutPluginStack stack, FalloutFormKey cell)
    {
        var record = stack.GetEffective(cell);
        if (record.Signature != "CELL") throw new InvalidDataException("Image-space owner is not a CELL.");
        var links = record.ReadSubrecords().Where(field => field.Signature == "XCIM").ToArray();
        if (links.Length == 0) return null;
        if (links.Length != 1 || links[0].Data.Length != 4)
            throw new InvalidDataException($"CELL {cell} has an invalid XCIM link.");
        var key = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span));
        return key is null ? null : Read(stack.GetEffective(key.Value));
    }

    internal static FalloutImageSpace Read(FalloutPluginRecord record)
    {
        if (record.Signature != "IMGS") throw new InvalidDataException("XCIM target is not IMGS.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "DNAM").ToArray();
        if (fields.Length != 1) throw new InvalidDataException($"IMGS {record.FormKey} needs one DNAM.");
        return Decode(record.FormKey, record.FormVersion, fields[0].Data.Span);
    }

    // The pre-v10 layout has no Skin Dimmer. Reading it as 33 consecutive
    // traits shifts every cinematic field and interprets reserved bytes as tint.
    internal static FalloutImageSpace Decode(FalloutFormKey form, ushort version, ReadOnlySpan<byte> data)
    {
        var hasSkinDimmer = version >= 10;
        var traitCount = hasSkinDimmer ? 33 : 32;
        var reservedBytes = hasSkinDimmer ? 16 : 4;
        var flagsBytes = version >= 13 ? 4 : 0;
        var expectedBytes = traitCount * 4 + reservedBytes + flagsBytes;
        if (data.Length != expectedBytes)
            throw new InvalidDataException($"IMGS {form} v{version} DNAM has {data.Length} bytes; expected {expectedBytes}.");
        var traits = new float[traitCount];
        for (var index = 0; index < traits.Length; index++)
        {
            traits[index] = BinaryPrimitives.ReadSingleLittleEndian(data[(index * 4)..]);
            if (!float.IsFinite(traits[index])) throw new InvalidDataException($"IMGS {form} has a non-finite trait {index}.");
        }
        var cinematicStart = hasSkinDimmer ? 25 : 24;
        var cinematic = new Vector4(traits[cinematicStart], traits[cinematicStart + 1], traits[cinematicStart + 2], traits[cinematicStart + 3]);
        var tint = new Vector4(traits[cinematicStart + 4], traits[cinematicStart + 5], traits[cinematicStart + 6], traits[cinematicStart + 7]);
        byte? flags = flagsBytes == 0 ? null : data[traitCount * 4 + reservedBytes];
        if (flags is { } enabled)
        {
            if ((enabled & ~15) != 0) throw new NotSupportedException($"IMGS {form} uses unknown cinematic flags 0x{enabled:x2}.");
            if ((enabled & 1) == 0) cinematic.X = 1;
            if ((enabled & 2) == 0) { cinematic.Y = 0; cinematic.Z = 1; }
            if ((enabled & 4) == 0) tint.W = 0;
            if ((enabled & 8) == 0) cinematic.W = 1;
        }
        return new(form, version, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), traits,
            hasSkinDimmer ? traits[14] : null, cinematic, tint, flags);
    }
}
