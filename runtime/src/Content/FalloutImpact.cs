using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutImpact(FalloutFormKey Form, string? Model, float Duration, uint Orientation,
    float AngleThreshold, float PlacementRadius, uint Flags, FalloutFormKey? TextureSet, FalloutFormKey[] Sounds)
{
    internal static FalloutImpact? Resolve(FalloutPluginStack records, FalloutFormKey set, int material)
    {
        if (material is < 0 or > 11) throw new ArgumentOutOfRangeException(nameof(material));
        var source = records.GetEffective(set);
        if (source.Signature != "IPDS") throw new InvalidDataException("Weapon impact dataset is not IPDS.");
        var data = source.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
        if (data.Length != 48) throw new NotSupportedException("Impact dataset does not contain the twelve source materials.");
        var id = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(material * 4)..]);
        if (id == 0) return null;
        return Read(records, source.Plugin.AdjustFormId(id));
    }

    private static FalloutImpact Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var source = records.GetEffective(form);
        if (source.Signature != "IPCT") throw new InvalidDataException("Material impact is not IPCT.");
        var fields = source.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 24) throw new NotSupportedException("Impact DATA extent is unbound.");
        FalloutFormKey? Form(string field)
        {
            var bytes = fields.SingleOrDefault(value => value.Signature == field).Data;
            if (bytes.IsEmpty) return null;
            if (bytes.Length != 4) throw new InvalidDataException("Impact FormID extent is invalid.");
            return source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span));
        }
        var result = new FalloutImpact(form, FalloutNpcAppearanceResolver.PathField(source, "MODL", "meshes", false, fields),
            FalloutProjectile.Number(data, 0), BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            FalloutProjectile.Number(data, 8), FalloutProjectile.Number(data, 12), BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
            Form("DNAM"), new[] { Form("SNAM"), Form("NAM1") }.Where(value => value.HasValue).Select(value => value!.Value).ToArray());
        if (result.Duration < 0 || result.Orientation > 2 || result.PlacementRadius < 0 || (result.Flags & ~1u) != 0)
            throw new NotSupportedException("Impact orientation, duration, placement or flags are unbound.");
        return result;
    }

    internal static int MaterialIndex(uint havok)
    {
        if (havok >= 128) throw new NotSupportedException($"Havok impact material {havok} has no material-family binding.");
        return (havok % 32) switch
        {
            0 or 10 or 19 => 0,
            1 => 7,
            2 or 18 => 1,
            3 or 24 => 3,
            4 => 2,
            5 or 11 or 13 or 14 or 15 or 21 or 26 or 27 => 4,
            6 or 7 => 6,
            8 => 8,
            9 or 12 => 5,
            16 or 17 or 20 or 22 or 23 or 25 or 28 or 29 => 9,
            _ => throw new NotSupportedException($"Havok impact material {havok} has no measured impact family.")
        };
    }
}
