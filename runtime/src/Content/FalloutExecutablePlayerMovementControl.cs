namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerMovementControl(byte[] code, Image image, int caller)
    {
        var arguments = caller + 0x857;
        if (!Match(code, arguments, [0xf3, 0x0f, 0x10, 0x43, 8, 0x8d, 0x45, 0xec, 0x6a, 0, 0x50, 0x6a, 0,
                0x51, 0x8b, 0xcf, 0xf3, 0x0f, 0x11, 4, 0x24, 0xe8]) ||
            !Match(code, arguments + 0x1a, [0x0f, 0xb6, 0x4d, 0xd6, 0x84, 0xc0, 0xba, 1, 0, 0, 0, 0x0f, 0x45, 0xca]))
            throw new NotSupportedException("Player movement/control changed its actual scene, zero arguments, mutable word or Boolean merge.");
        var child = Call(arguments + 0x15);
        if (!Match(code, child, [0x55, 0x8b, 0xec, 0x83, 0xe4, 0xf8, 0x83, 0xec, 0x78, 0xa1]) ||
            !Match(code, child + 0xe, [0xc6, 0x44, 0x24, 5, 0, 0x56, 0x57, 0x8b, 0xf9, 0x89, 0x7c, 0x24, 0x30]) ||
            !Match(code, child + 0x1b, [0x83, 0xf8, 4, 0x0f, 0x87]) ||
            !Match(code, child + 0x24, [0xff, 0x24, 0x85]) ||
            Word(image.ReadLoaderData(Word(code, child + 0xa), 4), 0) != 0)
            throw new NotSupportedException("Player movement/control lost its independent source mode loader or dispatch.");
        var ordinary = SourceOffset(code, image.CodeBase, Word(image.Read(Word(code, child + 0x27), 20), 0));
        if (code[ordinary] != 0xe8 || !Match(code, ordinary + 5, [0x84, 0xc0, 0x75]) ||
            !Match(code, ordinary + 9, [0x8b, 0x0d]) || code[ordinary + 0xf] != 0xe8 ||
            !Match(code, ordinary + 0x14, [0x8b, 0xf0, 0xc7, 5]) ||
            Word(code, ordinary + 0x1c) != 0 ||
            !Match(code, ordinary + 0x20, [0x33, 0xc0, 0x89, 0x74, 0x24, 0x30, 0xc7, 5]) ||
            Word(code, ordinary + 0x2c) != 0 || code[ordinary + 0x34] != 0xe8)
            throw new NotSupportedException("Ordinary Player controls lost current input retrieval before independent cache clears and device selection.");
        foreach (var at in new[] { ordinary + 0x18, ordinary + 0x28 })
            if (Word(image.ReadLoaderData(Word(code, at), 4), 0) != 0)
                throw new NotSupportedException("Player control cache changed its independent loader-null cells.");
        var inhibited = Call(ordinary);
        foreach (var (offset, field, branch) in new[] { (0, 0x6e5, 0x1e), (9, 0x6e4, 0x15), (18, 0x6e6, 12), (27, 0x6e7, 3) })
            if (!Match(code, inhibited + offset, [0x80, 0xb9, (byte)field, (byte)(field >> 8), 0, 0, 0, 0x75, (byte)branch]))
                throw new NotSupportedException("Player inhibition predicate changed its four distinct ordered source bytes.");
        if (!Match(code, inhibited + 36, [0x32, 0xc0, 0xc3, 0xb0, 1, 0xc3]))
            throw new NotSupportedException("Player inhibition predicate acquired an unowned effect or return.");
        var inhibitedReturn = checked(ordinary + 9 + unchecked((sbyte)code[ordinary + 8]));
        if (!Match(code, inhibitedReturn, [0x8a, 0x44, 0x24, 0x0d, 0x5f, 0x5e, 0x8b, 0xe5, 0x5d, 0xc2, 0x10, 0]) ||
            checked(child + 0x24 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(child + 0x20))) != inhibitedReturn)
            throw new NotSupportedException("Inhibited Player controls lost the existing zero-local Boolean and complete argument-return epilogue.");
        var joystick = Call(ordinary + 0x34);
        if (!Match(code, joystick, [0x80, 0x3d]) ||
            !Match(code, joystick + 6, [0, 0x74, 0x0c, 0x80, 0x3d]) ||
            !Match(code, joystick + 0xf, [0, 0x75, 3, 0xb0, 1, 0xc3, 0x32, 0xc0, 0xc3]) ||
            image.ReadLoaderData(Word(code, joystick + 2), 1)[0] != 0 ||
            image.ReadLoaderData(Word(code, joystick + 0xb), 1)[0] != 0)
            throw new NotSupportedException("Player joystick predicate lost its two independent loaded bytes.");
        var mouse = ordinary + 0x313;
        if (!Match(code, mouse, [0x6a, 1, 0xe8]) ||
            !Match(code, mouse + 7, [0x8b, 0x4c, 0x24, 0x30, 0x8b, 0xf0, 0x6a, 2, 0xe8]) ||
            !Match(code, mouse + 20, [0x8b, 0x4c, 0x24, 0x30, 0x6a, 3, 0x89, 0x44, 0x24, 0x3c, 0xe8]) ||
            Call(mouse + 2) != Call(mouse + 15) || Call(mouse + 2) != Call(mouse + 30))
            throw new NotSupportedException("Player mouse controls lost their three ordered current cached-axis reads.");
        var getter = Call(mouse + 2);
        if (!Match(code, getter, [0x8b, 0x44, 0x24, 4, 0x83, 0xe8, 1, 0x74, 0x21,
                0x83, 0xe8, 1, 0x74, 0x13, 0x83, 0xe8, 1, 0x74, 5, 0x33, 0xc0, 0xc2, 4, 0]) ||
            !Match(code, getter + 0x18, [0x8b, 0x81, 0x2c, 0x1b, 0, 0, 0xc2, 4, 0]) ||
            !Match(code, getter + 0x21, [0x8b, 0x81, 0x28, 0x1b, 0, 0, 0xc2, 4, 0]) ||
            !Match(code, getter + 0x2a, [0x8b, 0x81, 0x24, 0x1b, 0, 0, 0xc2, 4, 0]))
            throw new NotSupportedException("Player mouse getter acquired polling, mutation or a different cached extent.");
        RequirePlayerControlConfiguration(code, image, ordinary, mouse);
        int Call(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Movement/control source child is no longer direct.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }

    private static void RequirePlayerMovementControlConstructor(byte[] code, int constructor)
    {
        foreach (var (offset, field) in new[] { (0x2df, 0x6e6), (0x7d7, 0x6e4), (0x809, 0x6e5) })
        {
            var prefix = new byte[] { 0xc6, 0x87, (byte)field, (byte)(field >> 8), 0, 0 };
            if (!Match(code, constructor + offset, prefix) || code[constructor + offset + 6] != 0)
                throw new NotSupportedException("Player movement/control omitted its independent inhibition-byte constructor store.");
        }
        if (!Match(code, constructor + 0x2da, [0xb9, 0x49, 0, 0, 0]) ||
            !Match(code, constructor + 0x30c, [0x83, 0xe9, 1, 0x75, 0xdd]) ||
            !Match(code, constructor + 0x38b, [0x89, 0x8f, 0xfc, 8, 0, 0, 0x88, 0x8f, 0xe7, 6, 0, 0]))
            throw new NotSupportedException("Player inhibition's fourth constructor byte lost its completed zero-count loop producer.");
        if (!Match(code, constructor + 0x65a, [0x66, 0xc7, 0x87, 0xdc, 5, 0, 0, 0, 0]) ||
            !Match(code, constructor + 0x681, [0xc7, 0x87, 0x40, 6, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("Player controls lost their independent constructor-zero disable word or yaw-offset field.");
    }
}
