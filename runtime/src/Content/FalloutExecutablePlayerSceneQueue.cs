namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerSceneReferenceQueue(byte[] code, Image image, int start)
    {
        if (!Match(code, start, [0x83, 0x3d]) || code[start + 6] != 0 ||
            !Match(code, start + 7, [0x75, 0x0d, 0x83, 0x3d]) || code[start + 15] != 0 ||
            !Match(code, start + 16, [0x0f, 0x84, 0x1a, 1, 0, 0, 0xb8]))
            throw new NotSupportedException("Player scene queue lost its independent inline-head and next-node empty branch.");
        var head = Word(code, start + 11);
        if (Word(code, start + 2) != head + 4 || Word(code, start + 23) != head || !image.IsWritableExtent(head, 8) ||
            !Match(image.ReadLoaderData(head, 8), 0, [0, 0, 0, 0, 0, 0, 0, 0]) ||
            !Match(code, start + 0x28, [0x8b, 0x80, 0xd0, 1, 0, 0, 0xff, 0xd0]) ||
            !Match(code, start + 0x46, [0x6a, 0, 0x6a, 1, 0x6a, 1, 0x83, 0x60, 0x30, 0xfe, 0x50, 0xe8]) ||
            !Match(code, start + 0xa2, [0x8b, 0x45, 0xdc, 0x8b, 0x40, 4, 0x89, 0x45, 0xdc]) ||
            !Match(code, start + 0x130, [0x8b, 0x4f, 0x60, 0x57, 0x8b, 1, 0xff, 0x90, 0x60, 4, 0, 0]))
            throw new NotSupportedException("Player reference queue changed constructor storage, current-node/update order or following process virtual.");
        // The selected load method reads resolved reference words and prepends
        // each nonnull result. The independent destroy helper removes a first
        // match; it is not an enqueue operation or an actor-process list.
        var loaders = 0; var removers = 0;
        for (var at = 0; at <= code.Length - 0x56; ++at)
        {
            if (Match(code, at, [0x83, 0x3d]) && Word(code, at + 2) == head &&
                Match(code, at + 6, [0, 0x74, 0x47, 0x6a, 8, 0xb9]) &&
                Match(code, at + 0x21, [0x8b, 0x0d]) && Word(code, at + 0x23) == head &&
                Match(code, at + 0x27, [0x89, 0x0a, 0xc7, 0x42, 4, 0, 0, 0, 0]) &&
                code[at + 0x42] == 0xa1 && Word(code, at + 0x43) == head + 4 &&
                Match(code, at + 0x47, [0x89, 0x42, 4, 0x89, 0x15]) && Word(code, at + 0x4c) == head + 4 &&
                Match(code, at + 0x50, [0x89, 0x3d]) && Word(code, at + 0x52) == head)
                ++loaders;
            if (!Match(code, at, [0x83, 0x7d, 8, 0, 0x74, 0x0e, 0x8d, 0x45, 8, 0xb9]) ||
                Word(code, at + 10) != head || !Match(code, at + 14, [0x50, 0xe8])) continue;
            var remove = checked(at + 20 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 16)));
            if (!Match(code, remove + 0x24, [0x8b, 0x45, 8, 0x8b, 0, 0x85, 0xc0]) ||
                !Match(code, remove + 0x44, [0x39, 6, 0x74, 0x1c]) ||
                !Match(code, remove + 0x6c, [0x8b, 0x47, 4, 0x89, 0x41, 4, 0x8b, 7, 0x89, 1]))
                throw new NotSupportedException("Player reference destruction lost its first-match inline-head promotion.");
            ++removers;
        }
        if (loaders != 1 || removers != 1)
            throw new NotSupportedException("Player scene queue source load/destroy writers are absent or ambiguous.");
    }
}
