using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutImageSpace(
    FalloutFormKey Form, ushort FormVersion, string DnamSha256, float[] RawTraits,
    float? SkinDimmer, Vector4 Cinematic, Vector4 Tint, byte[] ReservedData)
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

    internal static FalloutImageSpace? ForWorld(FalloutPluginStack stack, FalloutFormKey world)
    {
        var seen = new HashSet<FalloutFormKey>();
        while (seen.Add(world))
        {
            var record = stack.GetEffective(world);
            if (record.Signature != "WRLD") throw new InvalidDataException("Image-space owner is not a WRLD.");
            var fields = record.ReadSubrecords().ToArray();
            FalloutFormKey? Link(string signature)
            {
                var rows = fields.Where(field => field.Signature == signature).ToArray();
                if (rows.Length == 0) return null;
                if (rows.Length != 1 || rows[0].Data.Length != 4)
                    throw new InvalidDataException($"WRLD {world} has an invalid {signature} link.");
                return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(rows[0].Data.Span));
            }
            // Unlike the other inheritance flags, an explicit INAM wins even
            // when Use Image Space Data is set on this world's parent link.
            if (Link("INAM") is { } image) return Read(stack.GetEffective(image));
            var flags = fields.Where(field => field.Signature == "PNAM").ToArray();
            if (flags.Length > 1 || flags.Length == 1 && flags[0].Data.Length != 2)
                throw new InvalidDataException($"WRLD {world} has invalid parent flags.");
            if (flags.Length == 0 || (BinaryPrimitives.ReadUInt16LittleEndian(flags[0].Data.Span) & 32) == 0 ||
                Link("WNAM") is not { } parent) return null;
            world = parent;
        }
        throw new InvalidDataException("World image-space inheritance contains a cycle.");
    }

    internal static FalloutImageSpace Read(FalloutPluginRecord record)
    {
        if (record.Signature != "IMGS") throw new InvalidDataException("XCIM target is not IMGS.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "DNAM").ToArray();
        if (fields.Length != 1) throw new InvalidDataException($"IMGS {record.FormKey} needs one DNAM.");
        return Decode(record.FormKey, record.FormVersion, fields[0].Data.Span);
    }

    // The runtime selects the Skin Dimmer layout by DNAM extent, not form
    // version. In particular, 148-byte v11/v13 records still have 32 traits.
    // Editor bookkeeping after the traits is not a cinematic enable mask.
    internal static FalloutImageSpace Decode(FalloutFormKey form, ushort version, ReadOnlySpan<byte> data)
    {
        if (data.Length is not (132 or 148 or 152))
            throw new InvalidDataException($"IMGS {form} v{version} DNAM has unsupported extent {data.Length}.");
        var hasSkinDimmer = data.Length == 152;
        var traitCount = hasSkinDimmer ? 33 : 32;
        var traits = new float[traitCount];
        for (var index = 0; index < traits.Length; index++)
        {
            traits[index] = BinaryPrimitives.ReadSingleLittleEndian(data[(index * 4)..]);
            if (!float.IsFinite(traits[index])) throw new InvalidDataException($"IMGS {form} has a non-finite trait {index}.");
        }
        var cinematicStart = hasSkinDimmer ? 25 : 24;
        var cinematic = new Vector4(traits[cinematicStart], traits[cinematicStart + 1], traits[cinematicStart + 2], traits[cinematicStart + 3]);
        var tint = new Vector4(traits[cinematicStart + 4], traits[cinematicStart + 5], traits[cinematicStart + 6], traits[cinematicStart + 7]);
        return new(form, version, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), traits,
            hasSkinDimmer ? traits[14] : null, cinematic, tint, data[(traitCount * 4)..].ToArray());
    }
}
