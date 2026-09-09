using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>Winning projectile declaration. Range and speed retain source game units.</summary>
internal sealed record FalloutProjectile(FalloutFormKey Form, ushort Flags, ushort Type, float Gravity,
    float Speed, float Range, float TracerChance, string? Model, string? MuzzleFlash, float MuzzleSeconds,
    FalloutFormKey? MuzzleLight, FalloutFormKey? Explosion)
{
    internal bool Hitscan => (Flags & 1) != 0;

    internal static FalloutProjectile Read(FalloutPluginStack records, FalloutFormKey key)
    {
        var record = records.GetEffective(key);
        if (record.Signature != "PROJ") throw new InvalidDataException("Projectile reference is not PROJ.");
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length is not (64 or 68 or 80 or 84)) throw new NotSupportedException($"PROJ DATA extent {data.Length} is unbound.");
        var result = new FalloutProjectile(key, BinaryPrimitives.ReadUInt16LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
            Number(data, 4), Number(data, 8), Number(data, 12), Number(data, 24), Path("MODL"), Path("NAM1"), Number(data, 44),
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[20..])),
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[36..])));
        if (result.Range <= 0 || result.Speed < 0 || result.Gravity < 0 || result.TracerChance is < 0 or > 1 || result.MuzzleSeconds < 0)
            throw new InvalidDataException("Projectile motion/appearance values are invalid.");
        return result;

        string? Path(string signature)
        {
            var bytes = fields.SingleOrDefault(field => field.Signature == signature).Data;
            if (bytes.IsEmpty) return null;
            var path = FalloutPlugin.DecodeZeroTerminated(bytes.Span, "projectile model").Replace('\\', '/');
            if (path.Split('/').Any(part => part is ".." or ".") || path.Contains(':') || path.StartsWith('/'))
                throw new InvalidDataException("Projectile model leaves the owned namespace.");
            return path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : "meshes/" + path;
        }
    }

    internal static float Number(ReadOnlySpan<byte> data, int offset)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Projectile/weapon field is not finite.");
    }
}
