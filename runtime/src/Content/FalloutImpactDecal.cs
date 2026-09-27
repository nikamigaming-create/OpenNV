namespace OpenNV.Runtime.Content;

internal sealed record FalloutImpactDecal(float MinimumWidth, float MaximumWidth, float MinimumHeight,
    float MaximumHeight, float Depth, float Shininess, float ParallaxScale, byte ParallaxPasses, byte Flags,
    byte Red, byte Green, byte Blue, string Diffuse, string? Normal)
{
    // DODT defines three low flag bits. Owned thrown/melee impacts also retain
    // nonzero reserved bits; preserve the byte without treating them as flags.
    internal byte KnownFlags => (byte)(Flags & 7);
    internal byte ReservedFlags => (byte)(Flags & 0xf8);

    internal static FalloutImpactDecal Read(FalloutPluginRecord impact, FalloutPluginRecord textures)
    {
        if (textures.Signature != "TXST") throw new InvalidDataException("Impact decal texture link is not TXST.");
        var data = impact.ReadSubrecords().SingleOrDefault(field => field.Signature == "DODT").Data;
        if (data.IsEmpty) data = textures.ReadSubrecords().SingleOrDefault(field => field.Signature == "DODT").Data;
        if (data.Length != 36) throw new NotSupportedException("Impact decal DODT extent is unbound.");
        var result = Decode(data.Span,
            FalloutNpcAppearanceResolver.PathField(textures, "TX00", "textures", true)!,
            FalloutNpcAppearanceResolver.PathField(textures, "TX01", "textures", false));
        return result;
    }

    internal static FalloutImpactDecal Decode(ReadOnlySpan<byte> data, string diffuse, string? normal)
    {
        if (data.Length != 36) throw new InvalidDataException("Decal data must contain 36 bytes.");
        var result = new FalloutImpactDecal(FalloutProjectile.Number(data, 0), FalloutProjectile.Number(data, 4),
            FalloutProjectile.Number(data, 8), FalloutProjectile.Number(data, 12), FalloutProjectile.Number(data, 16),
            FalloutProjectile.Number(data, 20), FalloutProjectile.Number(data, 24), data[28], data[29], data[32], data[33], data[34], diffuse, normal);
        if (result.MinimumWidth <= 0 || result.MaximumWidth < result.MinimumWidth || result.MinimumHeight <= 0 ||
            result.MaximumHeight < result.MinimumHeight || result.Depth <= 0 || result.Shininess < 0)
            throw new InvalidDataException("Decal dimensions or shininess are invalid.");
        return result;
    }
}
