namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static (int Constructor, uint Slot) RequirePlayerInputManager(byte[] code, Image image, int getter)
    {
        if (getter < 0 || getter > code.Length - 6 || code[getter] != 0xa1 || code[getter + 5] != 0xc3)
            throw new NotSupportedException("Player current input-manager getter has an unowned effect or trailing child.");
        var slot = Word(code, getter + 1);
        if (Word(image.ReadLoaderData(slot, 4), 0) != 0)
            throw new NotSupportedException("Player input-manager source slot has no original null loader state.");
        var factories = new List<int>();
        for (var at = 0x16; at <= code.Length - 0x22; ++at)
            if (Match(code, at, [0x85, 0xc0, 0x74, 0x0c, 0xff, 0x75, 0x0c, 0x8b, 0xc8, 0xe8]) &&
                Match(code, at + 0x0e, [0xeb, 2, 0x33, 0xc0, 0x8b, 0xc8, 0xc6, 0x45, 0xfc, 0x10, 0xa3]) &&
                Word(code, at + 0x19) == slot && code[at + 0x1d] == 0xe8 &&
                Match(code, at - 0x16, [0x68, 4, 0x1c, 0, 0, 0xb9])) factories.Add(at);
        if (factories.Count != 1)
            throw new NotSupportedException("Player input-manager getter has no unique actual constructor-to-slot publisher.");
        var factory = factories[0];
        var constructor = checked(factory + 14 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(factory + 10)));
        if (constructor < 0 || constructor > code.Length - 0xab || code[constructor + 0x40] != 0x68 ||
            !Match(code, constructor + 0x45, [0x68, 0, 8, 0, 0, 0x50]) || code[constructor + 0x59] != 0xe8 ||
            code[constructor + 0xa1] != 0x68 || !Match(code, constructor + 0xa6, [0x50, 0xff, 0x51, 0x0c]))
            throw new NotSupportedException("Player source input-manager constructor lost its interface or keyboard creation calls.");
        // Public DirectInput ABI identities associate the object with its real
        // device factory. It is independent from the source script-loop Main.
        if (new Guid(image.Read(Word(code, constructor + 0x41), 16)) != new Guid("bf798030-483a-4da2-aa99-5d64ed369700") ||
            new Guid(image.Read(Word(code, constructor + 0xa2), 16)) != new Guid("6f1d2b61-d5a0-11cf-bfc7-444553540000"))
            throw new NotSupportedException("Player input-manager factory changed its original DirectInput interface or keyboard identity.");
        return (constructor, slot);
    }
}
