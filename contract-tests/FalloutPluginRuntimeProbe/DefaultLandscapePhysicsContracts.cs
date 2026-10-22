using OpenNV.Runtime.Content;

internal static class DefaultLandscapePhysicsContracts
{
    internal static void Run()
    {
        var defaults = FalloutLandscapeTransportResolver.ReadDefaultTexture(Settings(
            "Rock/Example.DDS", "\\textures\\landscape\\EXAMPLE_n.dds"));
        Require(defaults.Physics == new FalloutLandscapePhysics(2, 30, 30),
            "Default LAND did not retain its initialized physical declaration.");
        Require(defaults.LandscapeTexture is null && defaults.TextureSet is null &&
            defaults.LandscapeTextureEditorId.Length == 0 && defaults.TextureSetEditorId.Length == 0,
            "Default LAND fabricated a source LTEX or TXST identity.");
        Require(defaults.DiffusePath == "textures\\landscape\\rock\\example.dds" &&
            defaults.NormalPath == "textures\\landscape\\example_n.dds",
            "Default LAND changed its existing installation visual-path normalization.");
        var otherVisuals = FalloutLandscapeTransportResolver.ReadDefaultTexture(Settings(
            "textures/landscape/unrelated.dds", "landscape/also_unrelated_n.dds"));
        Require(otherVisuals.DiffusePath != defaults.DiffusePath && otherVisuals.Physics == defaults.Physics,
            "Default LAND physics depended on its visual texture filename.");

        var source = Source(defaults, new(5, 7, 9));
        var resolver = new FalloutLandscapeImpactMaterial(source);
        Require(resolver.At(0, 0) == 2 && resolver.At(3712, 768) == 2,
            "Unpainted or strongest-default LAND impact lost its physical owner.");
        Require(resolver.At(3840, 768) == 5,
            "Default LAND physics overrode a winning authored LTEX layer.");
        Require(FalloutImpact.MaterialIndex(resolver.At(3712, 768)) == 1,
            "Raw default Havok material was confused with an impact-data-set family index.");
        var before = resolver.At(3712, 768);
        var otherMaterial = new FalloutLandscapeImpactMaterial(Source(otherVisuals, new(9, 11, 13))).At(3840, 768);
        Require(otherMaterial == 9 && resolver.At(3712, 768) == before && source.Textures[Key(10)].Physics == new FalloutLandscapePhysics(5, 7, 9),
            "Independent LAND resolution mutated a sibling transport or authored physical declaration.");

        var missingRealPhysics = new FalloutLandscapeImpactMaterial(Source(defaults, null));
        Require(missingRealPhysics.At(3712, 768) == 2,
            "An inactive missing LTEX declaration masked a valid default winner.");
        Reject(() => missingRealPhysics.At(3840, 768),
            "A winning real LTEX with absent physics silently inherited default LAND physics.");
        foreach (var missing in new[] { (Diffuse: (string?)null, Normal: (string?)"normal.dds"),
            (Diffuse: (string?)"diffuse.dds", Normal: (string?)null),
            (Diffuse: (string?)"", Normal: (string?)"normal.dds"),
            (Diffuse: (string?)"diffuse.dds", Normal: (string?)"") })
            Reject(() => FalloutLandscapeTransportResolver.ReadDefaultTexture(Settings(missing.Diffuse, missing.Normal)),
                "Default LAND physical support hid a missing or empty required visual INI value.");
        Reject(() => FalloutLandscapePhysics.Read(new byte[] { 2, 30 }), "Truncated LTEX physics was admitted.");
        Reject(() => FalloutLandscapePhysics.Read(new byte[] { 2, 30, 30, 0 }), "Trailing LTEX physics bytes were admitted.");
        Reject(() => FalloutLandscapePhysics.Read(new byte[] { 32, 30, 30 }), "An unsupported LTEX material was admitted.");
        Console.WriteLine("OPENNV_DEFAULT_LAND_PHYSICS_PASS actualDefaultReader=true initializedPhysicalBytes=true visualPathsIndependent=true defaultAndPaintedWinners=true realMissingPhysicsRefused=true malformedPhysicsAndMissingVisualsRefused=true sourceImmutable=true parserAndRetailParity=unverified");
    }

    private static FalloutFormKey Key(uint id) => new("Synthetic.esm", id);

    private static FalloutInstallationSettings Settings(string? diffuse, string? normal)
    {
        var rows = new List<FalloutInstallationSetting>();
        if (diffuse is not null) rows.Add(new("Landscape", "SDefaultLandDiffuseTexture", diffuse));
        if (normal is not null) rows.Add(new("Landscape", "SDefaultLandNormalTexture", normal));
        return FalloutInstallationSettings.ReadLayers([], rows);
    }

    private static FalloutLandscapeTransport Source(FalloutLandscapeTexture defaults, FalloutLandscapePhysics? paintedPhysics)
    {
        var bases = Enumerable.Range(0, 4).Select(quadrant =>
            new FalloutLandscapeLayer(Key(0), (byte)quadrant, 0, 0, true, [])).ToArray();
        FalloutLandscapeLayer[] alphas =
        [
            new(Key(10), 1, 0, 0, false, [new(115, 0, .25f), new(116, 0, 1)]),
            new(Key(11), 1, 1, 0, false, [new(115, 0, .15f)]),
        ];
        var textures = new Dictionary<FalloutFormKey, FalloutLandscapeTexture>
        {
            [Key(0)] = defaults,
            [Key(10)] = new(Key(10), "Painted", Key(20), "PaintedSet", "textures\\landscape\\painted.dds", null, paintedPhysics),
            [Key(11)] = new(Key(11), "OtherPainted", Key(21), "OtherPaintedSet", "textures\\landscape\\other.dds", null, new(9, 17, 19)),
        };
        return new(Key(1), Key(2), (0, 0), Key(3), Key(4), 1,
            new float[33 * 33], new float[33 * 33 * 3], new byte[33 * 33 * 3], bases, alphas, textures);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException(message);
    }
}
