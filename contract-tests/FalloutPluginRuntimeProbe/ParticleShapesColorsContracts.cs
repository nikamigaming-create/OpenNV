using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class ParticleShapesColorsContracts
{
    internal static void Run()
    {
        foreach (var interpolation in new[] { 1U, 2U, 3U, 5U })
        {
            var bytes = NativeNifInstanceAudit.ParticleShapesColorsFixture(interpolation);
            var before = SHA256.HashData(bytes);
            var file = FalloutNifFile.Read(bytes);
            for (var block = 0; block < file.Blocks.Count; block++) _ = file.ReadObject(block);
            var sphere = file.ReadObject(16) as FalloutNifParticleSphericalCollider;
            var colors = file.ReadObject(17) as FalloutNifParticleColorKeys;
            var data = file.ReadObject(19) as FalloutNifColorData;
            Require(sphere is { Radius: 1, Bounce: .5f, SpawnOnCollide: false, Manager: 11, Next: -1, Object: 18 } &&
                colors is { Order: 3000, Target: 0, Data: 19, Active: true } &&
                data is { Keys.Length: 3 } && data.Interpolation == interpolation &&
                file.ReadObject(12) is FalloutNifParticleCollider { Next: 16 } &&
                file.ReadObject(20) is FalloutNifColorData { Interpolation: null, Keys.Length: 0 },
                "Typed particle/color declarations changed original links, keys or empty extent.");
            var sampler = new FalloutNifColorAnimation(file, colors!.Data);
            Require(sampler.Sample(-1) == new FalloutNifVector4(1, .2f, .1f, .25f) &&
                sampler.Sample(.5f) == new FalloutNifVector4(.2f, 1, .5f, .75f) &&
                sampler.Sample(2) == new FalloutNifVector4(.4f, .3f, 1, .5f),
                "Color curve changed endpoint or exact-key identity.");
            var value = sampler.Sample(interpolation == 1 || interpolation == 5 ? .25f : .125f);
            var expected = interpolation switch
            {
                1 => new FalloutNifVector4(.6f, .6f, .3f, .5f),
                2 => new FalloutNifVector4(.875f, .325f, .1625f, .328125f),
                3 => new FalloutNifVector4(.7765625f, .43515625f, .19765625f, .392578125f),
                _ => new FalloutNifVector4(1, .2f, .1f, .25f),
            };
            Require(Near(value, expected), "Source RGBA curve differs from independent linear/Hermite/TBC/constant values.");
            Reject(() => sampler.Sample(float.NaN));
            Reject(() => new FalloutNifColorAnimation(file, 20));
            Reject(() => new FalloutNifColorAnimation(file, 1));
            Require(before.AsSpan().SequenceEqual(SHA256.HashData(bytes)), "Particle source reader/sampler mutated authored bytes.");
        }
        foreach (var mode in new[] { "empty-color", "data-null", "data-type" })
        {
            var file = FalloutNifFile.Read(NativeNifInstanceAudit.ParticleShapesColorsFixture(mode: mode));
            var source = (FalloutNifParticleColorKeys)file.ReadObject(17);
            Reject(() => new FalloutNifColorAnimation(file, source.Data));
            Require(file.ReadObject(20) is FalloutNifColorData { Keys.Length: 0 }, "Invalid selected owner hid an unrelated empty declaration.");
        }
        foreach (var kind in new[] { "truncated", "trailing", "nonfinite", "out-of-range", "unknown-interpolation", "unordered", "repeated-time" })
        {
            var bytes = NativeNifInstanceAudit.ParticleShapesColorsFixture();
            var source = FalloutNifFile.Read(bytes);
            var blocks = source.Blocks.Select(block => (block.TypeName, Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToArray();
            var target = kind == "out-of-range" ? 16 : 19;
            var body = blocks[target].Bytes.ToArray();
            if (kind == "truncated") body = body[..^1];
            if (kind == "trailing") body = body.Concat(new byte[] { 0 }).ToArray();
            if (kind == "nonfinite") BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(12), float.NaN);
            if (kind == "out-of-range") BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(10), blocks.Length);
            if (kind == "unknown-interpolation") BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 4);
            if (kind == "unordered") BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(48), -.5f);
            if (kind == "repeated-time") BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(48), .5f);
            blocks[target] = (blocks[target].TypeName, body);
            var fixture = NativeNifInstanceAudit.WriteParticleShapeColorNif(blocks, source.Strings.ToArray());
            var before = SHA256.HashData(fixture);
            var malformed = FalloutNifFile.Read(fixture);
            if (kind == "repeated-time")
            {
                Require(((FalloutNifColorData)malformed.ReadObject(19)).Keys.Length == 3,
                    "A repeated timestamp was dropped from typed source declarations.");
                Reject(() => new FalloutNifColorAnimation(malformed, 19));
            }
            else Reject(() => malformed.ReadObject(target));
            Require(malformed.ReadObject(20) is FalloutNifColorData { Keys.Length: 0 } &&
                before.AsSpan().SequenceEqual(SHA256.HashData(fixture)), "Failed particle declaration hid a sibling or mutated bytes.");
        }
        ExerciseSphere();
        Console.WriteLine("OPENNV_PARTICLE_SHAPES_COLORS_SOURCE_PASS actualFullReader=true completeExtents=true mixedColliderLinks=true RGBAKeys=true allKeyTypes=true endpoints=true sourceUnchanged=true malformedAndUnownedRefused=true analyticSweep=true nativeParity=unverified");
    }

    private static void ExerciseSphere()
    {
        var entry = FalloutNifParticleSphere.Sweep(new(0, 0, 3), new(0, 0, -4), 1, 1);
        Require(entry is { Fraction: .5f, Point.Z: 1, Normal.Z: 1 } &&
            FalloutNifParticleSphere.Sweep(new(2, 0, 3), new(0, 0, -4), 1, 1) is null &&
            FalloutNifParticleSphere.Sweep(new(0, 0, 3), new(0, 0, 4), 1, 1) is null &&
            FalloutNifParticleSphere.Sweep(new(0, 0, 3), Vector3.Zero, 1, 1) is null,
            "Source sphere sweep changed earliest entry, miss or moving-away behavior.");
        var boundary = FalloutNifParticleSphere.Sweep(new(0, 0, 1), new(0, 0, -2), .5f, 1);
        var continuation = FalloutNifParticleSphere.Sweep(new(0, 0, 1), new(0, 0, 1), .5f, 1);
        Require(boundary is { Fraction: 0 } && continuation is null &&
            FalloutNifParticleSphere.Sweep(new(1, 0, 3), new(0, 0, -4), 1, 1) is null &&
            FalloutNifParticleSphere.Sweep(Vector3.Zero, new(0, 0, 2), 1, 1) is null,
            "Sphere front-contact, tangent or documented initial-overlap boundary changed.");
        foreach (var radius in new[] { -1f, 0, float.NaN, float.PositiveInfinity })
            Reject(() => FalloutNifParticleSphere.Sweep(new(0, 0, 3), new(0, 0, -4), 1, radius));
        Reject(() => FalloutNifParticleSphere.Sweep(new(float.NaN, 0, 3), new(0, 0, -4), 1, 1));
        Reject(() => FalloutNifParticleSphere.Sweep(new(0, 0, 3), new(0, 0, -4), -1, 1));
    }

    private static bool Near(FalloutNifVector4 value, FalloutNifVector4 expected) =>
        MathF.Abs(value.X - expected.X) < .00001f && MathF.Abs(value.Y - expected.Y) < .00001f &&
        MathF.Abs(value.Z - expected.Z) < .00001f && MathF.Abs(value.W - expected.W) < .00001f;
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Malformed/unowned particle source was admitted.");
    }
}
