namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class FalloutNifFile
{
    // NiPSysCollider's linked header followed by the spherical radius. The
    // same source owner supplies planar and spherical collision chains.
    private FalloutNifParticleSphericalCollider ReadParticleSphericalCollider(FalloutNifBlock block, ref NifCursor cursor) => new(block,
        cursor.ReadFiniteSingle("particle collider bounce"), cursor.ReadBoolean("particle collider spawn"),
        cursor.ReadBoolean("particle collider die"), ReadReference(ref cursor, "particle collision spawn modifier"),
        ReadReference(ref cursor, "particle collider manager"), ReadReference(ref cursor, "next particle collider"),
        ReadReference(ref cursor, "particle collider object"), cursor.ReadFiniteSingle("particle sphere radius"));

    private static FalloutNifColorData ReadColorData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var count = cursor.ReadCount32("color key count", MaximumTableEntries);
        if (count == 0) return new(block, null, []);
        var interpolation = cursor.ReadUInt32("color interpolation");
        ValidateKeyType(interpolation, "color data");
        var keys = new FalloutNifColorKey[count];
        for (var index = 0; index < keys.Length; index++)
        {
            var time = cursor.ReadFiniteSingle($"color key {index} time");
            var value = ReadVector4(ref cursor, $"color key {index} RGBA");
            FalloutNifVector4? forward = interpolation == QuadraticKeyType
                ? ReadVector4(ref cursor, $"color key {index} forward") : null;
            FalloutNifVector4? backward = interpolation == QuadraticKeyType
                ? ReadVector4(ref cursor, $"color key {index} backward") : null;
            FalloutNifVector3? tbc = interpolation == TbcKeyType
                ? ReadVector(ref cursor, $"color key {index} TBC") : null;
            keys[index] = new(time, value, forward, backward, tbc, interpolation);
        }
        RequireIncreasingTimes(keys.Select(key => key.Time), "color data");
        return new(block, interpolation, keys);
    }
}

internal abstract record FalloutNifParticleCollider(FalloutNifBlock Block, float Bounce, bool SpawnOnCollide,
    bool DieOnCollide, int Spawn, int Manager, int Next, int Object) : FalloutNifObject(Block);
internal sealed record FalloutNifParticleSphericalCollider(FalloutNifBlock Block, float Bounce, bool SpawnOnCollide,
    bool DieOnCollide, int Spawn, int Manager, int Next, int Object, float Radius)
    : FalloutNifParticleCollider(Block, Bounce, SpawnOnCollide, DieOnCollide, Spawn, Manager, Next, Object);
internal sealed record FalloutNifParticleColorKeys(FalloutNifParticleModifier Header, int Data)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifColorKey(float Time, FalloutNifVector4 Value, FalloutNifVector4? Forward,
    FalloutNifVector4? Backward, FalloutNifVector3? Tbc, uint Interpolation);
internal sealed record FalloutNifColorData(FalloutNifBlock Block, uint? Interpolation, FalloutNifColorKey[] Keys)
    : FalloutNifObject(Block);
