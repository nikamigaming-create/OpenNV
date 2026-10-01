using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Formats.FaceGen;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFaceGeometryControls(FalloutCtlFile Model, string Sha256)
{
    internal static FalloutFaceGeometryControls Read(ReadOnlyMemory<byte> bytes) =>
        new(FalloutCtlFile.Read(bytes), Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant());

    internal static FalloutFaceGeometryControls Read(RuntimeLiveContentSource source) =>
        source.TryRead("facegen/si.ctl", null, out var bytes, out _) ? Read(bytes) :
            throw new FileNotFoundException("Face matching has no owned facegen/si.ctl.");
}

internal static class FalloutFaceGeometryMatch
{
    // Matching adds a scaled source-minus-preset displacement to the current
    // target coefficients. It preserves the target's statistical geometry age,
    // then stores geometry relative to the target race's male default. Texture
    // coefficients stay with the target; this is not target/source interpolation.
    internal static FalloutNpcFaceGen Apply(FalloutNpcFaceGen target, FalloutNpcFaceGen source,
        FalloutNpcFaceGen preset, FalloutNpcFaceGen targetRaceMale, FalloutCtlFile controls, int percentage)
    {
        var symmetric = Displace(target.SymmetricGeometry, source.SymmetricGeometry, preset.SymmetricGeometry, percentage);
        var asymmetric = Displace(target.AsymmetricGeometry, source.AsymmetricGeometry, preset.AsymmetricGeometry, percentage);
        if (controls.BasisCounts[0] * 4L != symmetric.Length || controls.BasisCounts[1] * 4L != asymmetric.Length)
            throw new InvalidDataException("Face geometry differs from the owned CTL basis dimensions.");
        FalloutCtlAffineAxis[] axes = [controls.AffineAxes[0][0][0], controls.AffineAxes[0][1][0]];
        var zero = new byte[symmetric.Length];
        var age = FalloutFaceGenControls.Attribute(target.SymmetricGeometry, zero, axes[0]);
        symmetric = FalloutFaceGenControls.SetAttribute(symmetric, zero, axes, 0, age);
        return target with
        {
            SymmetricGeometry = Subtract(symmetric, targetRaceMale.SymmetricGeometry),
            AsymmetricGeometry = Subtract(asymmetric, targetRaceMale.AsymmetricGeometry),
            SymmetricTexture = target.SymmetricTexture.ToArray(),
        };
    }

    private static byte[] Displace(byte[] target, byte[] source, byte[] preset, int percentage)
    {
        if (target.Length == 0 || target.Length % 4 != 0 || source.Length != target.Length || preset.Length != target.Length)
            throw new InvalidDataException("Face matching has incompatible geometry extents.");
        var result = new byte[target.Length]; var scale = (float)(percentage / 100.0);
        for (var index = 0; index < target.Length; index += 4)
        {
            var delta = Read(source, index) - Read(preset, index);
            var scaled = delta * scale;
            Write(result, index, Read(target, index) + scaled);
        }
        return result;
    }

    private static byte[] Subtract(byte[] face, byte[] race)
    {
        if (race.Length != face.Length) throw new InvalidDataException("Target race geometry has incompatible extents.");
        var result = new byte[face.Length];
        for (var index = 0; index < face.Length; index += 4) Write(result, index, Read(face, index) - Read(race, index));
        return result;
    }
    private static float Read(byte[] data, int index)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(index));
        return float.IsFinite(value) ? value : throw new InvalidDataException("Face matching has a non-finite coefficient.");
    }
    private static void Write(byte[] data, int index, float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("Face matching produced a non-finite coefficient.");
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(index), value);
    }
}
