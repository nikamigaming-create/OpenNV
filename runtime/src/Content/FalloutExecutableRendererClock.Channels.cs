namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static FalloutRendererClockSource RequireRendererChannels(byte[] code, Image image, int writer,
        uint scaledTimer, FalloutRendererClockSource source)
    {
        var cell = Word(code, writer + 2);
        uint? callback = null;
        for (var at = 0; at <= code.Length - 10; ++at)
        {
            if (!Match(code, at, [0xc7, 5]) || Word(code, at + 2) != cell || Word(code, at + 6) == 0) continue;
            if (callback is not null) throw new InvalidDataException("Renderer indexed clock callback installation is ambiguous.");
            callback = Word(code, at + 6);
        }
        var start = SourceOffset(code, image.CodeBase, callback ?? throw new NotSupportedException("Renderer clock callback is not installed."));
        if (!Match(code, start, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x83, 0xf8, 3, 0x74, 0x4b, 0x83, 0xf8, 2, 0x75, 0x38]) ||
            !Match(code, start + 0x15, [0x84, 0xc0, 0x75, 0x5f, 0xa1]) ||
            !Match(code, start + 0x1e, [0x66, 0x0f, 0x6e, 0xc0, 0xf3, 0x0f, 0xe6, 0xc0, 0xc1, 0xe8, 0x1f, 0xf2, 0x0f, 0x58, 4, 0xc5]) ||
            !Match(code, start + 0x32, [0x66, 0x0f, 0x5a, 0xc0, 0xf3, 0x0f, 0x5e, 5]) ||
            !Match(code, start + 0x48, [0x83, 0xf8, 1, 0x75, 0x11]) ||
            !Match(code, start + 0x52, [0x84, 0xc0, 0x75, 0x22, 0xd9, 5]) ||
            !Match(code, start + 0x5e, [0x85, 0xc0, 0x75, 0x16, 0xb9]) ||
            !Match(code, start + 0x72, [0xdc, 0xc9, 0xde, 0xc9, 0x5d, 0xc3, 0xd9, 0xee, 0x5d, 0xc3]) ||
            Word(code, start + 0x58) != scaledTimer || Word(code, start + 0x1a) != scaledTimer + 4 ||
            Target(code, image.CodeBase, start + 0x10) != Target(code, image.CodeBase, start + 0x4d))
            throw new NotSupportedException("Renderer indexed callback changed its independent timer/calendar/pause arms.");
        var corrections = image.Read(Word(code, start + 0x2e), 16);
        if (BitConverter.ToDouble(corrections, 0) != 0 || BitConverter.ToDouble(corrections, 8) != 4294967296d)
            throw new NotSupportedException("Renderer cumulative clock changed its actual unsigned millisecond conversion.");
        var menu = SourceOffset(code, image.CodeBase, Target(code, image.CodeBase, start + 0x10));
        if (!Match(code, menu, [0xa1]) || !Match(code, menu + 5, [0x85, 0xc0, 0x74, 0x0d, 0x80, 0x38, 0, 0x74, 8,
            0x83, 0x78, 0x0c, 1, 0x0f, 0x95, 0xc0, 0xc3, 0x32, 0xc0, 0xc3]))
            throw new NotSupportedException("Renderer clock no longer queries its actual source interface predicate.");
        return source with
        {
            MillisecondsPerSecond = BitConverter.UInt32BitsToSingle(Word(image.Read(Word(code, start + 0x3a), 4), 0)),
            HourFactor = ReadSinglePhaseClock(code, image.CodeBase, Word(code, writer + 37),
                address => BitConverter.UInt32BitsToSingle(Word(image.Read(address, 4), 0)), address => image.IsWritableExtent(address, 4)),
        };
    }

    private static void RequireClockSceneComplexity(byte[] code, Image image, uint field)
    {
        var handlers = image.NamedScriptCommandExecutors(code, "SetSceneIsComplex");
        if (handlers.Count != 1) throw new NotSupportedException("Renderer clock has no unique scene-complexity command owner.");
        var at = SourceOffset(code, image.CodeBase, handlers[0]);
        if (!Match(code, at + 0x2d, [0x83, 0x7d, 0xfc, 0, 0x0f, 0x95, 0xc0, 0xa2]) ||
            Word(code, at + 0x35) != field || !Match(code, at + 0x3e, [0xb0, 1, 0x8b, 0xe5, 0x5d, 0xc3]))
            throw new NotSupportedException("Renderer clock scene-complexity argument lost its original signed-integer/byte writer.");
    }

    private sealed partial class Image
    {
        internal IReadOnlyList<uint> NamedScriptCommandExecutors(byte[] code, string selected)
        {
            var result = new List<uint>();
            foreach (var section in headers.SectionHeaders.Where(section =>
                (section.SectionCharacteristics & System.Reflection.PortableExecutable.SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - 40; at += 4)
                {
                    var row = data.Slice(at, 40);
                    if (U32(row, 8) is < 0x1000 or >= 0x2000 || Literal(U32(row, 0)) != selected) continue;
                    var executor = U32(row, 24);
                    if (executor < CodeBase || (long)executor - CodeBase >= code.Length)
                        throw new InvalidDataException("Named script command escaped its actual selected code extent.");
                    result.Add(executor);
                }
            }
            return result;
        }
    }
}
