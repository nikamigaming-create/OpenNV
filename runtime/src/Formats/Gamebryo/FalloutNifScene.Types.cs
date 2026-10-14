namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct FalloutNifSceneKey(int Asset, int Block);
internal readonly record struct FalloutNifSceneSphere(FalloutNifVector3 Center, float Radius);
internal sealed record FalloutNifSceneBound(Guid Identity, uint X, uint Y, uint Z, uint Radius)
{
    internal FalloutNifSceneSphere Sphere => new(new(Value(X), Value(Y), Value(Z)), Value(Radius));
    internal static FalloutNifSceneBound From(FalloutNifSceneSphere value, Guid? identity = null) =>
        new(identity ?? Guid.NewGuid(), Bits(value.Center.X), Bits(value.Center.Y), Bits(value.Center.Z), Bits(value.Radius));
    internal static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    internal static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
}
internal sealed record FalloutNifSceneTransform(uint X, uint Y, uint Z, IReadOnlyList<uint> Rotation, uint Scale)
{
    internal static FalloutNifSceneTransform From(FalloutNifTransform transform) => new(Bits(transform.Translation.X),
        Bits(transform.Translation.Y), Bits(transform.Translation.Z), Array.AsReadOnly(transform.RotationRowMajor.Select(Bits).ToArray()), Bits(transform.Scale));
    internal FalloutNifTransform Read()
    {
        if (Rotation is not { Count: 9 }) throw new InvalidDataException("Cold logical scene rotation has a wrong source extent.");
        var result = new FalloutNifTransform(new(Value(X), Value(Y), Value(Z)), Rotation.Select(Value).ToArray(), Value(Scale));
        FalloutNifSceneMath.Validate(result); return result;
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
}
internal sealed record FalloutNifScenePropertySnapshot(FalloutNifSceneKey Key, string Type, int? ShaderKind,
    uint? ShaderAlphaBits, long ShaderStores, uint? MaterialAlphaBits, long MaterialStores);
internal sealed record FalloutNifSceneObjectSnapshot(FalloutNifSceneKey Key, string Type, string Name,
    FalloutNifSceneKey? Parent, IReadOnlyList<FalloutNifSceneKey?> Children, uint Flags,
    FalloutNifSceneTransform Local, FalloutNifSceneTransform World, FalloutNifSceneBound? Bound,
    FalloutNifSceneKey? Shader, long PoseStores, long BoundStores);
internal sealed record FalloutActorLogicalSceneAsset(int Slot, string Path, string Sha256, IReadOnlyList<int> Roots);
internal enum FalloutNifSceneAlphaStep { Root, Child, ShaderStore, NodeRecursion, Returned, Failed }
internal sealed record FalloutNifSceneAlphaCall(Guid Invocation, uint AlphaBits, long Entered, long Changed,
    FalloutNifSceneAlphaStep Step, FalloutNifSceneAlphaStep? FailedAt, FalloutNifSceneKey? Current,
    IReadOnlyList<FalloutNifSceneKey> Stores, string? Failure);
internal sealed record FalloutActorLogicalSceneSnapshot(string Schema, Guid Factory, Guid Lifetime,
    long Sequence, IReadOnlyList<FalloutActorLogicalSceneAsset> Assets, FalloutNifSceneKey Root,
    IReadOnlyList<FalloutNifSceneObjectSnapshot> Objects, IReadOnlyList<FalloutNifScenePropertySnapshot> Properties,
    FalloutNifSceneAlphaCall? AlphaCall, string? Failure, bool Retired,
    Guid? RestoredLifetime, string BoundLane, string AttachmentLane);

// A living property is shared by the exact source references to its block. Fade
// alpha and material-animation alpha are independent cells, even on one geometry.
internal sealed class FalloutNifSceneProperty
{
    internal FalloutNifSceneProperty(FalloutNifSceneKey key, FalloutNifObject source)
    {
        Key = key; Source = source;
        ShaderKind = source.Block.TypeName switch { "BSShaderPPLightingProperty" => 7, "BSShaderNoLightingProperty" => 20, "BSShaderProperty" => -1, _ => null };
        ShaderAlphaBits = ShaderKind is null ? null : 0x3f800000;
        MaterialAlphaBits = source is FalloutNifMaterialProperty material ? BitConverter.SingleToUInt32Bits(material.Alpha) : null;
    }
    internal FalloutNifSceneKey Key { get; }
    internal FalloutNifObject Source { get; }
    internal int? ShaderKind { get; }
    internal uint? ShaderAlphaBits { get; private set; }
    internal uint? MaterialAlphaBits { get; private set; }
    internal long ShaderStores { get; private set; }
    internal long MaterialStores { get; private set; }
    internal void StoreShaderAlpha(uint bits)
    {
        if (ShaderKind is null) throw new NotSupportedException("source-scene-slot-three-runtime-shader-layout-unowned");
        if (ShaderKind == -1) throw new InvalidOperationException("The source invalid shader must be skipped before a store.");
        ShaderAlphaBits = bits; ShaderStores = checked(ShaderStores + 1);
    }
    internal void StoreMaterialAlpha(uint bits)
    {
        if (MaterialAlphaBits is null) throw new NotSupportedException("source-current-material-alpha-property-unowned");
        MaterialAlphaBits = bits; MaterialStores = checked(MaterialStores + 1);
    }
    internal FalloutNifScenePropertySnapshot Capture() => new(Key, Source.Block.TypeName, ShaderKind, ShaderAlphaBits,
        ShaderStores, MaterialAlphaBits, MaterialStores);
    internal void Restore(FalloutNifScenePropertySnapshot saved)
    {
        if (saved.Key != Key || saved.Type != Source.Block.TypeName || saved.ShaderKind != ShaderKind ||
            (saved.ShaderAlphaBits is null) != (ShaderAlphaBits is null) || (saved.MaterialAlphaBits is null) != (MaterialAlphaBits is null) ||
            saved.ShaderStores < 0 || saved.MaterialStores < 0 || saved.ShaderStores == 0 && saved.ShaderAlphaBits != ShaderAlphaBits ||
            saved.MaterialStores == 0 && saved.MaterialAlphaBits != MaterialAlphaBits || ShaderKind == -1 && saved.ShaderStores != 0)
            throw new InvalidDataException("Cold scene property changed its source class, independent alpha cells or stores.");
        ShaderAlphaBits = saved.ShaderAlphaBits; MaterialAlphaBits = saved.MaterialAlphaBits;
        ShaderStores = saved.ShaderStores; MaterialStores = saved.MaterialStores;
    }
}

internal sealed class FalloutNifSceneObject
{
    internal FalloutNifSceneObject(FalloutNifSceneKey key, FalloutNifObject source, FalloutNifTransform local, uint flags)
    {
        Key = key; Source = source; Local = FalloutNifSceneMath.Copy(local); World = FalloutNifSceneMath.Identity; Flags = flags;
    }
    internal FalloutNifSceneKey Key { get; }
    internal FalloutNifObject Source { get; }
    internal bool IsNode => Source is FalloutNifNode;
    internal bool IsGeometry => Source is FalloutNifGeometry;
    internal string Name => Source switch { FalloutNifNode node => node.Name, FalloutNifGeometry geometry => geometry.Name, _ => "" };
    internal FalloutNifSceneObject? Parent { get; set; }
    internal List<FalloutNifSceneObject?> Children { get; } = [];
    internal uint Flags { get; set; }
    internal FalloutNifTransform Local { get; set; }
    internal FalloutNifTransform World { get; set; }
    internal FalloutNifSceneBound? Bound { get; set; }
    internal FalloutNifSceneProperty? Shader { get; set; }
    internal long PoseStores { get; set; }
    internal long BoundStores { get; set; }
    internal FalloutNifSceneObjectSnapshot Capture() => new(Key, Source.Block.TypeName, Name, Parent?.Key,
        Array.AsReadOnly(Children.Select(child => (FalloutNifSceneKey?)child?.Key).ToArray()), Flags,
        FalloutNifSceneTransform.From(Local), FalloutNifSceneTransform.From(World), Bound, Shader?.Key, PoseStores, BoundStores);
}
