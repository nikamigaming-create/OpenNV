using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // This source arm keeps the Float64 phase arithmetic and the explicit
    // Float32 trig-result stores distinct from the optimized source arm.
    private static FalloutDoubleVisionPhase BindStoredDoubleVisionPhase(byte[] code, Image image,
        FalloutDoubleVisionPhase phase)
    {
        var rows = new List<int>();
        for (var at = 0; at <= code.Length - 38; at++)
            if (Match(code, at, [0x83, 0xec, 8, 0xd9, 5]) &&
                Match(code, at + 9, [0x8b, 0x41, 0x1c, 0xdc, 0x35]) &&
                Match(code, at + 18, [0x56, 0x8b, 0x70, 0x0c, 0x83, 0xec, 0x10, 0x8b, 0xce, 0xdc, 0x0d])) rows.Add(at);
        if (rows.Count != 1) throw new InvalidDataException("Stored phase has no unique source parameter owner.");
        var start = rows[0];
        if (start > code.Length - 193 ||
            !Match(code, start + 74, [0x6a, 0, 0xe8]) ||
            !Match(code, start + 102, [0xd9, 0x5c, 0x24, 0x10, 0xd9, 0x44, 0x24, 0x10, 0x51, 0xd8, 0x0d]) ||
            !Match(code, start + 117, [0xd9, 0x5c, 0x24, 0x14, 0xd9, 0x44, 0x24, 0x14, 0xd9, 0x1c, 0x24,
                0xd9, 0x44, 0x24, 0x10, 0xe8]) ||
            !Match(code, start + 137, [0xd9, 0x5c, 0x24, 0x14, 0xd9, 0x44, 0x24, 0x14, 0x51, 0xd8, 0x0d]) ||
            !Match(code, start + 152, [0x8b, 0xce, 0xd9, 0x5c, 0x24, 0x18, 0xd9, 0x44, 0x24, 0x18,
                0xd9, 0x1c, 0x24, 0x6a, 1, 0xe8]) ||
            !Match(code, start + 172, [0xd9, 5]) || !Match(code, start + 178, [0xd9, 0x1d]) ||
            !Match(code, start + 184, [0xb0, 1, 0x5e, 0x83, 0xc4, 8, 0xc2, 4, 0]) ||
            Word(code, start + 113) != Word(code, start + 148) ||
            Target(code, image.CodeBase, start + 76) != Target(code, image.CodeBase, start + 167))
            throw new NotSupportedException("Stored phase changed its actual Float32 trigonometric parameter flow.");
        var vector = SourceOffset(code, image.CodeBase, Target(code, image.CodeBase, start + 76));
        if (!Match(code, vector, [0x8b, 0x44, 0x24, 4, 0xd9, 0x44, 0x24, 8, 0x8b, 0x49, 0x44,
            0x03, 0xc0, 0x03, 0xc0, 0xd9, 0x1c, 0x81, 0xd9, 0x44, 0x24, 0x0c, 0xd9, 0x5c, 0x81, 4,
            0xd9, 0x44, 0x24, 0x10, 0xd9, 0x5c, 0x81, 8, 0xd9, 0x44, 0x24, 0x14,
            0xd9, 0x5c, 0x81, 0x0c, 0xc2, 0x14, 0]))
            throw new NotSupportedException("Stored phase vector changed its actual four Float32 parameter stores.");
        RequireStoredPhaseTrig(code, image.CodeBase, Target(code, image.CodeBase, start + 97), 0xff);
        RequireStoredPhaseTrig(code, image.CodeBase, Target(code, image.CodeBase, start + 132), 0xfe);
        var clock = Word(code, start + 5);
        uint? callbackCell = null;
        for (var at = 0; at <= code.Length - 81; at++)
        {
            if (!Match(code, at, [0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0xc7, 0x45, 0xfc, 0, 0, 0, 0, 0xeb, 9,
                0x8b, 0x45, 0xfc, 0x83, 0xc0, 1, 0x89, 0x45, 0xfc, 0x83, 0x7d, 0xfc, 4, 0x7d, 0x2f, 0x83, 0x3d]) ||
                !Match(code, at + 36, [0, 0x74, 0x12, 0x8b, 0x4d, 0xfc, 0x51, 0xff, 0x15]) ||
                !Match(code, at + 49, [0x83, 0xc4, 4, 0xd9, 0x5d, 0xf8, 0xeb, 5, 0xd9, 0xee, 0xd9, 0x5d, 0xf8,
                    0x8b, 0x55, 0xfc, 0xd9, 0x45, 0xf8, 0xd9, 0x1c, 0x95]) ||
                Word(code, at + 71) != clock || !Match(code, at + 75, [0xeb, 0xc2, 0x8b, 0xe5, 0x5d, 0xc3])) continue;
            var cell = Word(code, at + 32);
            if (cell != Word(code, at + 45) || !image.IsWritableExtent(cell, 4) || callbackCell is not null)
                throw new InvalidDataException("Stored phase changed its exact four-slot source clock writer.");
            callbackCell = cell;
        }
        if (callbackCell is not { } callbackField) throw new NotSupportedException("Stored phase source clock writer is absent.");
        uint? setter = null;
        for (var at = 0; at <= code.Length - 13; at++)
            if (Match(code, at, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0xa3]) &&
                Word(code, at + 7) == callbackField && Match(code, at + 11, [0x5d, 0xc3]))
            {
                if (setter is not null) throw new InvalidDataException("Stored phase callback constructor is ambiguous.");
                setter = checked(image.CodeBase + (uint)at);
            }
        if (setter is not { } callbackSetter) throw new NotSupportedException("Stored phase callback constructor is absent.");
        uint? callback = null;
        for (var at = 0; at <= code.Length - 13; at++)
            if (code[at] == 0x68 && code[at + 5] == 0xe8 &&
                (long)image.CodeBase + at + 10 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 6, 4)) == callbackSetter &&
                Match(code, at + 10, [0x83, 0xc4, 4]))
            {
                if (callback is not null) throw new InvalidDataException("Stored phase callback installation is ambiguous.");
                callback = Word(code, at + 1);
            }
        if (callback is not { } installed) throw new NotSupportedException("Stored phase has no actual installed source clock callback.");
        var index = SourceOffset(code, image.CodeBase, installed);
        if (!Match(code, index, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x10, 0x83, 0x7d, 8, 3, 0x75, 0x0f]) ||
            !Match(code, index + 126, [0x83, 0x7d, 8, 0, 0x75, 0x1e, 0xb9]) ||
            !Match(code, index + 142, [0xdc, 0x0d]) || !Match(code, index + 148, [0xdc, 0x0d]) ||
            !Match(code, index + 154, [0xd9, 0x5d, 0xf0, 0xd9, 0x45, 0xf0, 0xeb, 2, 0xd9, 0xee, 0x8b, 0xe5, 0x5d, 0xc3]) ||
            Word(code, index + 144) != Word(code, index + 150))
            throw new NotSupportedException("Stored phase has no actual index-zero GameHour callback arm.");
        var hour = SourceOffset(code, image.CodeBase, Target(code, image.CodeBase, index + 137));
        if (!Match(code, hour, [0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x89, 0x4d, 0xfc, 0x8b, 0x45, 0xfc,
            0x83, 0x78, 0x0c, 0, 0x74, 0x10, 0x8b, 0x4d, 0xfc, 0x8b, 0x49, 0x0c, 0xe8]) ||
            !Match(code, hour + 29, [0xd9, 0x5d, 0xf8, 0xeb, 9, 0xd9, 5]) ||
            !Match(code, hour + 43, [0xd9, 0x45, 0xf8, 0x8b, 0xe5, 0x5d, 0xc3]))
            throw new NotSupportedException("Stored phase GameHour receiver/payload is unbound.");
        var global = SourceOffset(code, image.CodeBase, Target(code, image.CodeBase, hour + 24));
        if (!Match(code, global, [0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc, 0x8b, 0x45, 0xfc,
            0xd9, 0x40, 0x24, 0x8b, 0xe5, 0x5d, 0xc3]))
            throw new NotSupportedException("Stored phase GameHour lost its actual Float32 global payload.");
        var factor = BitConverter.ToDouble(image.Read(Word(code, index + 144), 8));
        if (!double.IsFinite(factor) || factor is < 1 or > 4096 || factor != Math.Truncate(factor) || factor * factor != phase.SecondsPerHour)
            throw new NotSupportedException("Stored phase requires another actual extended source-hour arithmetic owner.");
        var semantic = Encoding.UTF8.GetBytes("source-stored-phase;Float64-divide-multiply-to-Float32;Float32-trig-before-amplitude;")
            .Concat(BitConverter.GetBytes(phase.SecondsPerHour)).Concat(BitConverter.GetBytes(phase.RadiansPerTurn))
            .Concat(BitConverter.GetBytes(factor)).ToArray();
        return phase with
        {
            SourceHourFactor = (float)factor,
            StoredSingleTrigonometricResult = true,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(semantic)).ToLowerInvariant()
        };
    }

    private static void RequireStoredPhaseTrig(byte[] code, uint codeBase, uint address, byte operation)
    {
        var at = SourceOffset(code, codeBase, address);
        if (!Match(code, at, [0x83, 0x3d]) || !Match(code, at + 6, [0, 0x74, 0x32]) ||
            !Match(code, at + 48, [0x8d, 0x64, 0x24, 8, 0x75, 5, 0xe9]) ||
            !Match(code, at + 59, [0x83, 0xec, 0x0c, 0xdd, 0x14, 0x24, 0xe8]) ||
            code.Length - at < 80 || code[at + 70] != 0xe8 ||
            !Match(code, at + 75, [0x83, 0xc4, 0x0c, 0xc3]))
            throw new NotSupportedException("Stored phase trig transport changed its actual x87 consumer.");
        var primitive = SourceOffset(code, codeBase, Target(code, codeBase, at + 70));
        if (!Match(code, primitive, [0x52, 0x9b, 0xd9, 0x3c, 0x24, 0x74]) ||
            !Match(code, primitive + 7, [0x66, 0x81, 0x3c, 0x24, 0x7f, 2, 0x74, 6, 0xd9, 0x2d]) ||
            !Match(code, primitive + 21, [0xd9, operation, 0x9b, 0xdf, 0xe0, 0x9e]))
            throw new NotSupportedException("Stored phase changed its actual sine/cosine ISA operation.");
    }
}
