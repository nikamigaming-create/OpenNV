using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // This transport has separate Float32 stores for the sampled source clock,
    // the quotient, the phase and each trigonometric result. It is selected by
    // actual operands and their consumers, independently of installation names.
    internal static FalloutDoubleVisionPhase? ReadSingleDoubleVisionPhase(ReadOnlySpan<byte> input,
        uint codeBase, Func<uint, float> scalar, Func<uint, bool> writable)
    {
        var code = input.ToArray();
        FalloutDoubleVisionPhase? result = null;
        for (var at = 0; at <= code.Length - 25; at++)
        {
            if (!Match(code, at, [0x51, 0xf3, 0x0f, 0x10, 0x05]) ||
                !Match(code, at + 9, [0xf3, 0x0f, 0x5e, 0x05]) ||
                !Match(code, at + 17, [0x8b, 0x41, 0x1c, 0x56, 0x83, 0xec, 0x10, 0x8b])) continue;
            if (at > code.Length - 208 || !Match(code, at + 24, [0x8b, 0x70, 0x0c, 0x8b, 0xce]) ||
                !Match(code, at + 29, [0xf3, 0x0f, 0x10, 0x0d]) ||
                !Match(code, at + 37, [0xf3, 0x0f, 0x59, 0x05]) ||
                !Match(code, at + 45, [0xf3, 0x0f, 0x11, 0x4c, 0x24, 0x0c,
                    0xf3, 0x0f, 0x11, 0x44, 0x24, 0x14]) ||
                !Match(code, at + 57, [0xf3, 0x0f, 0x10, 0x05]) ||
                !Match(code, at + 65, [0xf3, 0x0f, 0x59, 0xc1, 0xf3, 0x0f, 0x11, 0x44, 0x24, 8]) ||
                !Match(code, at + 75, [0xc7, 0x44, 0x24, 4, 0, 0, 0, 0, 0xc7, 4, 0x24, 0, 0, 0, 0,
                    0x6a, 0, 0xe8]) ||
                !Match(code, at + 97, [0xf3, 0x0f, 0x10, 0x4c, 0x24, 4, 0x83, 0xec, 8, 0x0f, 0x5a, 0xc1,
                    0xc7, 0x44, 0x24, 4, 0, 0, 0x80, 0x3f, 0xc7, 4, 0x24, 0, 0, 0x80, 0x3f, 0xe8]) ||
                !Match(code, at + 129, [0x0f, 0x57, 0xc9, 0xf2, 0x0f, 0x5a, 0xc8, 0x51,
                    0xf3, 0x0f, 0x59, 0x0d]) ||
                !Match(code, at + 145, [0xf3, 0x0f, 0x11, 0x0c, 0x24,
                    0xf3, 0x0f, 0x10, 0x4c, 0x24, 0x10, 0x0f, 0x5a, 0xc1, 0xe8]) ||
                !Match(code, at + 164, [0xf2, 0x0f, 0x5a, 0xc0, 0x51, 0x8b, 0xce,
                    0xf3, 0x0f, 0x59, 0x05]) ||
                !Match(code, at + 179, [0xf3, 0x0f, 0x11, 4, 0x24, 0x6a, 1, 0xe8]) ||
                !Match(code, at + 191, [0xc7, 5]) || Word(code, at + 197) != 0x40000000 ||
                !Match(code, at + 201, [0xb0, 1, 0x5e, 0x59, 0xc2, 4, 0]))
                throw new NotSupportedException("Owned single-precision double-vision parameter flow is unbound.");
            if (Target(code, codeBase, at + 92) != Target(code, codeBase, at + 186) ||
                Target(code, codeBase, at + 124) == Target(code, codeBase, at + 159) ||
                Word(code, at + 141) != Word(code, at + 175) ||
                !writable(Word(code, at + 5)) || !writable(Word(code, at + 193)))
                throw new NotSupportedException("Owned single-precision double-vision receiver or phase consumer drifted.");
            RequirePhaseVectorSetter(code, codeBase, Target(code, codeBase, at + 92));
            RequirePhaseTrigonometricConsumer(code, codeBase, Target(code, codeBase, at + 124), 0xff);
            RequirePhaseTrigonometricConsumer(code, codeBase, Target(code, codeBase, at + 159), 0xfe);
            var clock = Word(code, at + 5);
            var divisor = scalar(Word(code, at + 13));
            var turn = scalar(Word(code, at + 41));
            var factor = ReadSinglePhaseClock(code, codeBase, clock, scalar, writable);
            if (!float.IsFinite(divisor) || divisor <= 0 || !float.IsFinite(turn) || turn <= 0 ||
                (double)factor * factor != divisor)
                throw new InvalidDataException("Owned single-precision phase changed its source clock scale or finite operands.");
            var semantics = Encoding.UTF8.GetBytes("source-double-vision/Float32-stored-clock;Float32-divide-multiply;Float64-trig-to-Float32-before-amplitude;")
                .Concat(BitConverter.GetBytes(divisor)).Concat(BitConverter.GetBytes(turn)).Concat(BitConverter.GetBytes(factor)).ToArray();
            if (result is not null) throw new InvalidDataException("Owned single-precision double-vision phase declaration is ambiguous.");
            result = new(divisor, turn, Convert.ToHexString(SHA256.HashData(semantics)).ToLowerInvariant())
            { SinglePrecisionArithmetic = true, SourceHourFactor = factor };
        }
        return result;
    }

    private static void RequirePhaseVectorSetter(byte[] code, uint codeBase, uint address)
    {
        var at = SourceOffset(code, codeBase, address);
        if (!Match(code, at, [0x8b, 0x44, 0x24, 4, 0xf3, 0x0f, 0x10, 0x44, 0x24, 8,
            0x8d, 0x14, 0x85, 0, 0, 0, 0, 0x8b, 0x41, 0x44, 0xf3, 0x0f, 0x11, 4, 0x90,
            0xf3, 0x0f, 0x10, 0x44, 0x24, 0x0c, 0xf3, 0x0f, 0x11, 0x44, 0x90, 4,
            0xf3, 0x0f, 0x10, 0x44, 0x24, 0x10, 0xf3, 0x0f, 0x11, 0x44, 0x90, 8,
            0xf3, 0x0f, 0x10, 0x44, 0x24, 0x14, 0xf3, 0x0f, 0x11, 0x44, 0x90, 0x0c, 0xc2, 0x14, 0]))
            throw new NotSupportedException("Owned double-vision vector setter no longer stores its four actual Float32 components.");
    }

    private static void RequirePhaseTrigonometricConsumer(byte[] code, uint codeBase, uint address, byte operation)
    {
        var at = SourceOffset(code, codeBase, address);
        if (at > code.Length - 49 || !Match(code, at, [0x83, 0xec, 8, 0x0f, 0xae, 0x5c, 0x24, 4, 0x8b, 0x44, 0x24, 4,
            0x25, 0x80, 0x7f, 0, 0, 0x3d, 0x80, 0x1f, 0, 0, 0x75, 0x0f, 0xd9, 0x3c, 0x24,
            0x66, 0x8b, 4, 0x24, 0x66, 0x83, 0xe0, 0x7f, 0x66, 0x83, 0xf8, 0x7f,
            0x8d, 0x64, 0x24, 8, 0x0f, 0x85]))
            throw new NotSupportedException("Owned double-vision trigonometric control/ABI declaration is unbound.");
        var fallback = checked(at + 49 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 45, 4)));
        if (!Match(code, fallback, [0x83, 0xec, 8, 0x66, 0x0f, 0xd6, 4, 0x24, 0xe8]) ||
            !Match(code, fallback + 13, [0xdd, 0x1c, 0x24, 0xf3, 0x0f, 0x7e, 4, 0x24, 0x83, 0xc4, 8, 0xc3]))
            throw new NotSupportedException("Owned double-vision trigonometric fallback did not consume/return its actual Float64 argument.");
        var primitive = SourceOffset(code, codeBase, Target(code, codeBase, fallback + 8));
        if (!Match(code, primitive, [0x8d, 0x54, 0x24, 4, 0xe8]) ||
            !Match(code, primitive + 9, [0x52, 0x9b, 0xd9, 0x3c, 0x24, 0x74]) ||
            !Match(code, primitive + 16, [0x66, 0x81, 0x3c, 0x24, 0x7f, 2, 0x74, 6, 0xd9, 0x2d]) ||
            !Match(code, primitive + 30, [0xd9, operation, 0x9b, 0xdf, 0xe0, 0x9e]))
            throw new NotSupportedException("Owned double-vision source sine/cosine consumer changed its actual ISA operation.");
        var argument = SourceOffset(code, codeBase, Target(code, codeBase, primitive + 4));
        if (!Match(code, argument, [0x8b, 0x42, 4, 0x25, 0, 0, 0xf0, 0x7f, 0x3d, 0, 0, 0xf0, 0x7f,
            0x74, 3, 0xdd, 2, 0xc3]))
            throw new NotSupportedException("Owned double-vision trigonometric helper changed its actual finite Float64 argument load.");
    }

    private static float ReadSinglePhaseClock(byte[] code, uint codeBase, uint clock,
        Func<uint, float> scalar, Func<uint, bool> writable)
    {
        uint? callbackCell = null;
        for (var at = 0; at <= code.Length - 50; at++)
        {
            if (!Match(code, at, [0x83, 0x3d]) || Word(code, at + 2) == 0 || code[at + 6] != 0 ||
                !Match(code, at + 7, [0x74, 0x14, 0x56, 0xff, 0x15]) ||
                !Match(code, at + 16, [0xd9, 0x5d, 0xf8, 0xf3, 0x0f, 0x10, 0x45, 0xf8, 0x83, 0xc4, 4,
                    0xeb, 3, 0x0f, 0x57, 0xc0, 0xf3, 0x0f, 0x11, 4, 0xb5]) ||
                Word(code, at + 37) != clock ||
                !Match(code, at + 41, [0x0f, 0x57, 0xc9, 0x46, 0x83, 0xfe, 4, 0x7c, 0xce])) continue;
            var cell = Word(code, at + 2);
            if (cell != Word(code, at + 12) || !writable(cell) || callbackCell is not null)
                throw new NotSupportedException("Owned phase clock does not have one exact indexed callback writer.");
            callbackCell = cell;
        }
        if (callbackCell is null) throw new NotSupportedException("Owned phase clock callback writer is unbound.");
        uint? callback = null;
        for (var at = 0; at <= code.Length - 10; at++)
        {
            if (!Match(code, at, [0xc7, 5]) || Word(code, at + 2) != callbackCell) continue;
            var value = Word(code, at + 6);
            if (value == 0) continue; // Actual loader initialization precedes installation.
            if (callback is not null) throw new NotSupportedException("Owned phase clock callback installation is ambiguous.");
            callback = value;
        }
        if (callback is not { } callbackAddress || callbackAddress < codeBase || (long)callbackAddress - codeBase > code.Length - 124)
            throw new NotSupportedException("Owned phase clock has no actual source callback constructor.");
        var start = SourceOffset(code, codeBase, callbackAddress);
        // Preserve every index predicate. The phase consumes index zero; the
        // paused/cumulative/delta arms remain separate source clock consumers.
        if (!Match(code, start, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x83, 0xf8, 3, 0x74, 0x4b,
            0x83, 0xf8, 2, 0x75, 0x38]) ||
            !Match(code, start + 72, [0x83, 0xf8, 1, 0x75, 0x11]) ||
            !Match(code, start + 94, [0x85, 0xc0, 0x75, 0x16, 0xb9]) ||
            code[start + 103] != 0xe8 || !Match(code, start + 108, [0xd9, 5]) ||
            !Match(code, start + 114, [0xdc, 0xc9, 0xde, 0xc9, 0x5d, 0xc3, 0xd9, 0xee, 0x5d, 0xc3]))
            throw new NotSupportedException("Owned phase clock indexed source-hour branch is unbound.");
        var getter = Target(code, codeBase, start + 103);
        if (getter < codeBase || getter - codeBase > code.Length - 42)
            throw new NotSupportedException("Owned phase clock hour getter escaped its selected source.");
        var hour = checked((int)(getter - codeBase));
        if (!Match(code, hour, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x41, 0x0c, 0x85, 0xc0, 0x74, 0x11,
            0xf3, 0x0f, 0x10, 0x40, 0x24, 0xf3, 0x0f, 0x11, 0x45, 0xfc, 0xd9, 0x45, 0xfc,
            0x8b, 0xe5, 0x5d, 0xc3]) ||
            !Match(code, hour + 28, [0xc7, 0x45, 0xfc]) ||
            !Match(code, hour + 35, [0xd9, 0x45, 0xfc, 0x8b, 0xe5, 0x5d, 0xc3]))
            throw new NotSupportedException("Owned phase clock has no actual Float32 game-hour payload getter.");
        var factor = scalar(Word(code, start + 110));
        // Integer factors of this extent have an exact binary product in the
        // host's Float64 intermediate, before the original Float32 cache store.
        if (!float.IsFinite(factor) || factor < 1 || factor > 4096 || factor != MathF.Truncate(factor))
            throw new NotSupportedException("Owned phase clock factor requires another extended arithmetic owner.");
        return factor;
    }

    private static bool Match(byte[] code, int at, ReadOnlySpan<byte> bytes) =>
        at >= 0 && at <= code.Length - bytes.Length && code.AsSpan(at, bytes.Length).SequenceEqual(bytes);
    private static uint Word(byte[] code, int at) => BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(at, 4));
    private static int SourceOffset(byte[] code, uint codeBase, uint address)
    {
        var offset = (long)address - codeBase;
        if (offset < 0 || offset >= code.Length)
            throw new NotSupportedException("Owned phase callee escaped its actual selected code extent.");
        return checked((int)offset);
    }
    private static uint Target(byte[] code, uint codeBase, int at)
    {
        if (at < 0 || at > code.Length - 5 || code[at] != 0xe8)
            throw new NotSupportedException("Owned phase declaration changed its direct source callee.");
        var target = (long)codeBase + at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1, 4));
        if (target < codeBase || target >= (long)codeBase + code.Length)
            throw new NotSupportedException("Owned phase declaration targets another executable extent.");
        return checked((uint)target);
    }

    private static FalloutDoubleVisionPhase BindLoadedPhaseClock(byte[] code, Image image, FalloutDoubleVisionPhase phase)
    {
        uint? cell = null;
        for (var at = 0; at <= code.Length - 25; at++)
        {
            var selected = phase.SinglePrecisionArithmetic ?
                Match(code, at, [0x51, 0xf3, 0x0f, 0x10, 5]) && Match(code, at + 9, [0xf3, 0x0f, 0x5e, 5]) :
                Match(code, at, [0x83, 0xec, 8, 0xd9, 5]) && Match(code, at + 9, [0x8b, 0x41, 0x1c, 0xdc, 0x35]);
            if (!selected) continue;
            if (cell is not null) throw new InvalidDataException("Loaded image phase clock declaration is ambiguous.");
            cell = Word(code, at + 5);
        }
        if (cell is not { } address || !image.IsWritableExtent(address, 4))
            throw new InvalidDataException("Image phase has no actual loaded writable clock cell.");
        if (image.IsFileExtent(address, 1) && !image.IsFileExtent(address, 4))
            throw new NotSupportedException("Image phase clock straddles file-backed and virtual-zero source extents.");
        // PE's writable virtual zero-fill owns a cell beyond file-backed data.
        // It is not an assumed x87 mode, a sampled game hour or a save default.
        var initial = image.IsFileExtent(address, 4) ? Word(image.Read(address, 4), 0) : 0u;
        return phase with { LoadedClockBits = initial };
    }
}
