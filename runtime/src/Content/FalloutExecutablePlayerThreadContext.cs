namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerThreadContext(byte[] code, Image image, int hudCaller, FalloutMainScriptCallerSource main)
    {
        var source = FalloutPlayerThreadContextSource.Reviewed(main);
        var start = hudCaller + 0x32;
        if (!Match(code, start, [0x64, 0xa1, 0x2c, 0, 0, 0, 0x8b, 0x0d]) ||
            !Match(code, start + 0x0c, [0x89, 0x45, 0xe8, 0x8b, 0x34, 0x88, 0x8a, 0x86, 0xa4, 2, 0, 0, 0x84, 0xc0, 0x75, 0x0b, 0xe8]) ||
            !Match(code, start + 0x27, [0x8b, 0xb6, 0x80, 2, 0, 0, 0x89, 0x75, 0xec]) ||
            !Match(code, start + 0x36, [0x84, 0xc0, 0x75, 5, 0xe8]) ||
            !Match(code, start + 0x42, [0x8b, 0x15]) || Word(code, start + 8) != Word(code, start + 0x44) ||
            !Match(code, start + 0x48, [0x8b, 4, 0x91, 0xc7, 0x80, 0x80, 2, 0, 0]) || Word(code, start + 0x51) != source.PlayerKind)
            throw new NotSupportedException("Player working-context current-thread read/store order changed.");
        int Call(int at) => checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        var wrapper = Call(start + 0x1c);
        if (wrapper < 0 || wrapper > code.Length - 12 || wrapper != Call(start + 0x3a) ||
            !Match(code, wrapper, [0x6a, 0, 0x6a, 2, 0x6a, 0, 0xe8]) || code[wrapper + 11] != 0xc3)
            throw new NotSupportedException("Player source on-demand TLS initializer wrapper is unbound.");
        var init = Call(wrapper + 6);
        if (init < 0 || init > code.Length - 0x80 ||
            !Match(code, init + 0x25, [0x83, 0x7d, 0x0c, 2, 0x75, 0x42, 0x8b, 0x0d]) ||
            Word(code, init + 0x2d) != Word(code, start + 8) ||
            !Match(code, init + 0x3a, [0x80, 0xb8, 0xa4, 2, 0, 0, 1, 0x74, 0x2a, 0xc6, 0x80, 0xa4, 2, 0, 0, 1, 0xbe]) ||
            code[init + 0x4f] != 0xbb || Word(code, init + 0x4b) != Word(code, init + 0x50))
            throw new NotSupportedException("Player TLS initialization has an unowned callback, initialized-byte writer or nonempty initializer range.");
        var loaded = image.PlayerWorkingContextLoaderCells(Word(code, start + 8));
        if (loaded.Kind != source.InitialKind || loaded.Initialized != source.InitialInitialized ||
            loaded.Model != source.InitialModel || loaded.Reference != source.InitialReference || loaded.CollisionMode != source.InitialCollisionMode)
            throw new NotSupportedException("Player source TLS initial current-thread cells changed.");
        var exit = hudCaller + 0x3dc7;
        if (!Match(code, exit, [0x8b, 0x0d]) || Word(code, exit + 2) != Word(code, start + 8) ||
            !Match(code, exit + 6, [0x8b, 0x45, 0xe8, 0x8b, 0x3c, 0x88, 0x80, 0xbf, 0xa4, 2, 0, 0, 0, 0x75, 5, 0xe8]) ||
            Call(exit + 0x15) != wrapper ||
            !Match(code, exit + 0x1a, [0x89, 0xb7, 0x80, 2, 0, 0, 0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0,
                0x59, 0x5f, 0x5e, 0x8b, 0xe5, 0x5d, 0x8b, 0xe3, 0x5b, 0xc2, 4, 0]))
            throw new NotSupportedException("Player context epilogue changed its calling thread, initializer or prior-kind restore.");
        if (code[start + 0x62] != 0xe8)
            throw new NotSupportedException("Player source current input-manager getter is unbound.");
        RequirePlayerInputManager(code, image, Call(start + 0x62));
    }
    private sealed partial class Image
    {
        internal (uint Kind, byte Initialized, uint Model, uint Reference, uint CollisionMode) PlayerWorkingContextLoaderCells(uint selectedIndex)
        {
            var directory = headers.PEHeader!.ThreadLocalStorageTableDirectory;
            if (directory.Size != 24 || directory.RelativeVirtualAddress == 0)
                throw new NotSupportedException("Player current-thread source has no complete PE32 TLS declaration.");
            var tls = Read(checked(Base + (uint)directory.RelativeVirtualAddress), 24);
            var first = Word(tls, 0); var last = Word(tls, 4);
            if (last < first || Word(tls, 8) != selectedIndex || (ulong)last - first + Word(tls, 16) < 0x2a5)
                throw new InvalidDataException("Player source context is outside the declared loader TLS image/index.");
            var values = ReadLoaderData(checked(first + 0x248), 0x5d);
            return (Word(values, 0x38), values[0x5c], Word(values, 0), Word(values, 4), Word(values, 0x40));
        }
    }
}
