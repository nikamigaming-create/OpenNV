using System.Buffers.Binary;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

/// <summary>Compiler-emitted image-space filter tables read from the selected owned executable.</summary>
internal sealed record FalloutImageSpaceKernels(Vector4[][] Blur, Vector4[] Prefilter, string SourceSha256)
{
    internal static FalloutImageSpaceKernels Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(bytes, false));
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32)
            throw new NotSupportedException("Image-space filter declarations require the owned Win32 layout.");
        var tables = new List<(Vector4[][] Blur, Vector4[] Prefilter, byte[] Bytes)>();
        foreach (var section in pe.PEHeaders.SectionHeaders.Where(section =>
            (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0 && section.SizeOfRawData > 0))
        {
            var found = Decode(bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData));
            if (found is not null) tables.Add(found.Value);
        }
        if (tables.Count != 1) throw new NotSupportedException("Owned image-space filter declaration is missing or ambiguous.");
        var table = tables[0];
        return new(table.Blur, table.Prefilter, Convert.ToHexString(SHA256.HashData(table.Bytes)).ToLowerInvariant());
    }

    internal static (Vector4[][] Blur, Vector4[] Prefilter, byte[] Bytes)? Decode(ReadOnlySpan<byte> data)
    {
        // The admitted declarations are seven centered, padded 15-entry kernels.
        // Kernel extents and coordinates identify the table; weights remain source
        // Float32 data, including the compiler's rounded normalization.
        const int radiusCount = 7, tapCount = 15, stride = 16;
        var extent = radiusCount * tapCount * stride;
        Vector4[][]? result = null;
        var position = -1;
        for (var at = 0; at <= data.Length - extent; at += 4)
        {
            if (F(data, at) != -7 || F(data, at + 4) != -7) continue;
            var kernels = new Vector4[radiusCount][];
            var valid = true;
            for (var radius = 1; radius <= radiusCount && valid; radius++)
            {
                var row = kernels[radius - 1] = new Vector4[tapCount];
                var sum = 0f;
                for (var tap = 0; tap < tapCount; tap++)
                {
                    var start = at + ((radius - 1) * tapCount + tap) * stride;
                    var value = row[tap] = new(F(data, start), F(data, start + 4), F(data, start + 8), F(data, start + 12));
                    var offset = tap - radiusCount;
                    if (value.X != offset || value.Y != offset || value.W != 0 || !float.IsFinite(value.Z) ||
                        value.Z < 0 || (Math.Abs(offset) > radius && value.Z != 0)) { valid = false; break; }
                    sum += value.Z;
                }
                if (MathF.Abs(sum - 1) > 0.00001f) valid = false;
            }
            if (!valid) continue;
            if (result is not null) throw new InvalidDataException("Multiple owned blur-kernel declarations match.");
            result = kernels; position = at;
        }
        if (result is null) return null;
        // The four-tap prefilter is a separate declaration. Its center ordering
        // and weights are checked; retaining the actual bytes avoids a fitted
        // replacement filter when the renderer selects it before/after blur.
        Vector2[] coordinates = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        Vector4[]? prefilter = null;
        var prefilterPosition = -1;
        for (var at = 0; at <= data.Length - 64; at += 4)
        {
            if (F(data, at) != -1 || F(data, at + 4) != -1) continue;
            var row = new Vector4[4]; var sum = 0f; var valid = true;
            for (var tap = 0; tap < row.Length; tap++)
            {
                var start = at + tap * stride;
                var value = row[tap] = new(F(data, start), F(data, start + 4), F(data, start + 8), F(data, start + 12));
                if (value.X != coordinates[tap].X || value.Y != coordinates[tap].Y || value.W != 0 ||
                    !float.IsFinite(value.Z) || value.Z <= 0) { valid = false; break; }
                sum += value.Z;
            }
            if (!valid || MathF.Abs(sum - 1) > 0.00001f) continue;
            if (prefilter is not null) throw new InvalidDataException("Multiple owned prefilter declarations match.");
            prefilter = row; prefilterPosition = at;
        }
        if (prefilter is null) throw new NotSupportedException("Owned image-space prefilter declaration is absent.");
        return (result, prefilter, data.Slice(position, extent).ToArray().Concat(data.Slice(prefilterPosition, 64).ToArray()).ToArray());
    }

    internal Vector4[] AtRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0 || radius > Blur.Length)
            throw new NotSupportedException($"Image-space blur radius {radius:R} has no admitted kernel.");
        var upper = Math.Clamp((int)MathF.Ceiling(radius), 1, Blur.Length);
        var lower = Math.Max(upper - 1, 1);
        var fraction = radius - (upper - 1);
        if (fraction == 0) fraction = 1;
        return Blur[upper - 1].Select((value, index) => new Vector4(value.X, value.Y,
            Blur[lower - 1][index].Z + (value.Z - Blur[lower - 1][index].Z) * fraction, value.W)).ToArray();
    }

    private static float F(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
}
