using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Formats.FaceGen;

internal static class FaceGenSynthetic
{
    internal static void Run()
    {
        var egm = new byte[64 + 3 * (4 + 2 * 6)];
        "FREGM002"u8.CopyTo(egm);
        U32(egm, 8, 2); U32(egm, 12, 2); U32(egm, 16, 1); U32(egm, 20, 99);
        egm[63] = 0xab;
        EgmMode(egm, 64, 0.5f, [2, -4, 6, 8, 0, -2]);
        EgmMode(egm, 80, -0.25f, [4, 8, -12, -4, 0, 4]);
        EgmMode(egm, 96, 0.125f, [-8, 16, 24, 0, -8, 8]);
        var geometry = FalloutEgmFile.Read(egm);
        var deltas = geometry.EvaluateDeltas([2, -0.5f], [3]);
        Require(deltas[0] == new Vector3(-0.5f, 3, 13.5f) && geometry.BasisVersion == 99 && geometry.SourceBytes.Span[63] == 0xab,
            "EGM signed values, full float scale, mode order and original bytes");
        var vertices = geometry.EvaluatePositions([Vector3.One, Vector3.Zero], [2, -0.5f], [3]);
        Require(vertices[0] == deltas[0] + Vector3.One, "EGM source position accumulation");
        Throws(() => geometry.EvaluatePositions([Vector3.Zero], [2, -0.5f], [3]));
        Throws(() => geometry.EvaluateDeltas([2], [3]));
        Throws(() => geometry.EvaluateDeltas([float.NaN, 0], [0]));
        Throws(() => FalloutEgmFile.Read(egm[..^1]));
        Throws(() => FalloutEgmFile.Read(egm.Append((byte)0).ToArray()));
        var badScale = egm.ToArray(); F32(badScale, 64, float.NaN); Throws(() => FalloutEgmFile.Read(badScale));

        var egt = new byte[64 + 3 * (4 + 6 * 3)];
        "FREGT003"u8.CopyTo(egt);
        U32(egt, 8, 2); U32(egt, 12, 3); U32(egt, 16, 2); U32(egt, 20, 1); U32(egt, 24, 81);
        EgtMode(egt, 64, 0.3125f, [1, 2, 3, 4, 5, 6, -1, -2, -3, -4, -5, -6, 10, 20, 30, 40, 50, 60]);
        EgtMode(egt, 86, -0.25f, Enumerable.Repeat((sbyte)127, 18).ToArray());
        EgtMode(egt, 108, 0.125f, Enumerable.Repeat((sbyte)1, 18).ToArray());
        var texture = FalloutEgtFile.Read(egt);
        var delta = texture.EvaluateDelta([2, 0], [2]);
        Require(delta.Width == 3 && delta.Height == 2 && texture.BasisVersion == 81, "EGT rows/columns and basis");
        Require(delta.Rgb.AsSpan(0, 3).SequenceEqual(new float[] { 0.875f, -0.375f, 6.5f }) &&
            delta.Rgb.AsSpan(15, 3).SequenceEqual(new float[] { 4, -3.5f, 37.75f }), "EGT signed planar RGB, scales and top-down layout");
        Require(texture.EvaluateDelta([0, 0], [0]).Rgb.All(value => value == 0), "EGT does not invent a mean image");
        Throws(() => texture.EvaluateDelta([0, float.PositiveInfinity], [0]));
        Throws(() => texture.EvaluateDelta([0, 0], []));
        Throws(() => FalloutEgtFile.Read(egt[..^1]));
        Throws(() => FalloutEgtFile.Read(egt.Append((byte)0).ToArray()));
        var badDimensions = egt.ToArray(); U32(badDimensions, 8, uint.MaxValue); Throws(() => FalloutEgtFile.Read(badDimensions));
        var badTextureScale = egt.ToArray(); F32(badTextureScale, 64, float.NegativeInfinity); Throws(() => FalloutEgtFile.Read(badTextureScale));
        Console.WriteLine("OPENNV_FACEGEN_FILES_OK egmSymmetricAsymmetric=true egtFloatScales=true signedSamples=true rowsColumns=true exactExtent=true nonFiniteRejected=true sourceBytesPreserved=true");
    }

    private static void EgmMode(byte[] bytes, int offset, float scale, short[] values)
    {
        F32(bytes, offset, scale);
        for (var index = 0; index < values.Length; ++index)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 4 + index * 2), values[index]);
    }
    private static void EgtMode(byte[] bytes, int offset, float scale, sbyte[] values)
    {
        F32(bytes, offset, scale);
        for (var index = 0; index < values.Length; ++index) bytes[offset + 4 + index] = unchecked((byte)values[index]);
    }
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void F32(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed FaceGen input was accepted.");
    }
}
