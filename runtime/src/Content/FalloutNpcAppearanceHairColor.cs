using System.Numerics;

namespace OpenNV.Runtime.Content;

/// <summary>NPC HCLR RGB and the HAIR record's fixed-colour policy.</summary>
internal static class FalloutNpcAppearanceHairColor
{
    internal const uint ShaderFlag = 1U << 18;

    internal static Vector3 Resolve(FalloutPluginStack stack, FalloutNpcAppearance appearance,
        FalloutNpcAppearancePart part)
    {
        var record = stack.GetEffective(part.Source);
        if (record.Signature == "HAIR")
        {
            var fields = record.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
            if (fields.Length != 1 || fields[0].Data.Length != 1)
                throw new InvalidDataException($"HAIR {record.FormKey} requires one byte of DATA flags.");
            if ((fields[0].Data.Span[0] & 8) != 0)
                return Vector3.One;
        }
        return Decode(appearance.HairColorBytes);
    }

    internal static Vector3 Decode(ReadOnlySpan<byte> hclr)
    {
        if (hclr.Length != 4)
            throw new InvalidDataException("NPC HCLR requires RGB and its unused fourth byte.");
        return new Vector3(hclr[0] / 255.0f, hclr[1] / 255.0f, hclr[2] / 255.0f);
    }
}
