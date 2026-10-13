namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerIdleClassPredicate(byte[] code, Image image, int idle)
    {
        var players = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (players.Length != 1 || !Match(code, idle + 0x149, [0x8b, 7, 0x8b, 0xcf, 0x8b, 0x80, 0x34, 2, 0, 0, 0xff, 0xd0,
            0x84, 0xc0, 0x75, 0x78]))
            throw new NotSupportedException("High idle lost its actual independent Actor status virtual.");
        var predicate = SourceOffset(code, image.CodeBase, Word(image.Read(players[0] + 0x234, 4), 0));
        if (!Match(code, predicate, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x51, 0x60, 0x85, 0xd2, 0x74, 0x49,
            0x8b, 0x52, 0x2c, 0x85, 0xd2, 0x74, 0x42]))
            throw new NotSupportedException("Actor status virtual changed its independent process/cache selection.");
        var threshold = Word(code, predicate + 0x2c);
        if (Word(image.Read(threshold, 4), 0) != 0 ||
            !Match(code, predicate + 0x12, [0x8b, 0x42, 0x40, 0x81, 0xc1, 0xa0, 0, 0, 0, 0xc1, 0xe8, 5, 0xf6, 0xd0, 0xa8, 1, 0x75, 0x13]) ||
            !Match(code, predicate + 0x24, [0xf3, 0x0f, 0x10, 0x42, 0x18, 0x0f, 0x2f, 5]) ||
            !Match(code, predicate + 0x30, [0x0f, 0x97, 0xc0, 0x8b, 0xe5, 0x5d, 0xc3]) ||
            !Match(code, predicate + 0x37, [0x8b, 1, 0x8b, 0x40, 0x18, 0xff, 0xd0]) ||
            !Match(code, predicate + 0x54, [0x8b, 0x81, 0xa0, 0, 0, 0, 0x81, 0xc1, 0xa0, 0, 0, 0, 0x8b, 0x40, 0x18, 0xff, 0xd0]) ||
            Word(code, predicate + 0x49) != threshold || Word(code, predicate + 0x70) != threshold)
            throw new NotSupportedException("Idle status predicate changed its cache-valid bit, current getter or ordered Float32 threshold.");
        var valueTables = image.SourceClassTables(".?AVPlayerCharacter@@", 7, 0xa0).ToArray();
        if (valueTables.Length != 1) throw new NotSupportedException("Player status predicate has no actual secondary getter interface.");
        var getter = SourceOffset(code, image.CodeBase, Word(image.Read(valueTables[0] + 0x18, 4), 0));
        if (!Match(code, getter, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0x8b, 0x46, 0xfc, 0x8d, 0x4e, 0xfc,
                0x6a, 0x2f, 0x8b, 0x40, 0x0c, 0xff, 0xd0]))
            throw new NotSupportedException("Idle status getter no longer requests the actual current actor-value slot.");
        var baseTables = image.SourceClassTables(".?AVBaseProcess@@", 1).ToArray();
        if (baseTables.Length != 1) throw new NotSupportedException("Actor status cache has no source process constructor.");
        var constructors = 0;
        for (var at = 0; at <= code.Length - 18; ++at)
            if (Match(code, at, [0xc7, 6]) && Word(code, at + 2) == baseTables[0] && code[at + 6] == 0xe8 &&
                Match(code, at + 0xb, [0xc7, 0x46, 0x2c, 0, 0, 0, 0])) ++constructors;
        if (constructors != 1) throw new NotSupportedException("Actor status cache lost its independent source null constructor.");
    }
}
