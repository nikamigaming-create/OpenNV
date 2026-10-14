using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutStandaloneMenuLifecycleSource ReadStandaloneMenuLifecycles(string path,
        FalloutStandaloneInterfaceSource current)
    {
        var source = FalloutStandaloneMenuLifecycleSource.Reviewed(current);
        using (var stream = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != current.Main.EngineSha256)
                throw new InvalidDataException("Menu lifecycle changed the actual selected owned executable.");
        var (code, image) = Load(path);
        var startMenu = image.SourceClassTables(".?AVStartMenu@@", 17).ToArray() is [var start] ? start :
            throw new NotSupportedException("Menu lifecycle has no unique selected StartMenu class.");
        Need(Offset(Word(image.Read(startMenu + 0x34, 4), 0)), [0xb8, 0xf5, 3, 0, 0, 0xc3], "StartMenu pure identity getter");
        var startCtor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == startMenu &&
            Match(code, at - 10, [0x8d, 0x4e, 0x7c, 0xc7, 0x45, 0xfc, 0, 0, 0, 0]), 12, 0x11);
        var baseCtor = Call(startCtor - 15);
        Need(baseCtor, [0x51, 0xc7, 1], "inherited Menu field constructor association");
        Need(baseCtor + 7, [0x8b, 0xc1, 0xc7, 0x41, 8, 0, 0, 0, 0, 0xc7, 0x41, 0xc, 0, 0, 0, 0], "inherited null lists");
        Need(baseCtor + 0x21, [0xc7, 0x41, 0x10, 0, 0, 0, 0, 0xc7, 0x41, 0x14, 0, 0, 0, 0], "independent null identity");
        Need(baseCtor + 0x2f, [0xc7, 0x41, 0x24, 4, 0, 0, 0, 0x66, 0xc7, 0x41, 0x1c, 0, 1], "Menu lifecycle four and flags256");

        var push = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0x75, 8, 0x57, 0x8b, 0xf9, 0x83, 0xfe, 3]), 0, 0xd7);
        Need(push + 0x1b, [0xc7, 0x87, 0x14, 1, 0, 0, 3, 0, 0, 0], "special code3 head store");
        Need(push + 0x3a, [0x42, 0x83, 0xc1, 4, 0x83, 0xfa, 9, 0x7c], "special head shift through slot8");
        Need(push + 0x80, [0x75, 0x2a, 0xc7, 0x47, 0xc, 3, 0, 0, 0], "first ordinary push mode3");
        Need(push + 0x77, [0x89, 0xb4, 0x97, 0x14, 1, 0, 0], "actual ten-word first-empty store");
        Need(push + 0xac, [0x83, 0xfa, 1, 0x75, 0x20, 0x81, 0xbf, 0x14, 1, 0, 0, 0xe9, 3, 0, 0], "only-HUD dependent image arm");
        var remove = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x8b, 0xc1, 0x83, 0xb8, 0x14, 1, 0, 0, 0]), 0, 0x155);
        Need(remove + 0x78, [0x8b, 0x84, 0x8b, 0x14, 1, 0, 0, 0x89, 2, 0x85, 0xc0, 0x74], "ordered removal source read before destination store");
        Need(remove + 0x138, [0xc7, 0x40, 0xc, 4, 0, 0, 0], "empty removal mode4 before dependent image writer");

        var visible = Unique(at => Match(code, at, [0x8b, 1, 0xff, 0x50, 0x34, 0x8d, 0x88, 0x17, 0xfc, 0xff, 0xff,
            0x83, 0xf9, 0x3b, 0x77, 6, 0x88, 0x98]), 0, 22);
        var active = Word(code, visible + 18) + FalloutStandaloneMenuLifecycleSource.FirstMenu;
        if (image.ReadLoaderData(active, 60).Any(value => value != 0))
            throw new NotSupportedException("Active-menu table lost its independent sixty source-loaded zero bytes.");
        var destructor = Unique(at => Match(code, at, [0x8b, 0x4b, 0x20, 0xc7, 3]) &&
            Match(code, at + 9, [0x8d, 0x81, 0x17, 0xfc, 0xff, 0xff, 0x83, 0xf8, 0x3b]), 0, 0x12d);
        Need(destructor + 0x14, [0xc6, 0x81], "base destructor active-byte clear before identity removal");
        if (Word(code, destructor + 0x16) + FalloutStandaloneMenuLifecycleSource.FirstMenu != active)
            throw new NotSupportedException("Menu visible/retirement writers selected different active tables.");
        Need(destructor + 0x10d, [0x6a, 0xe], "empty stack conditional actual Menu control query");
        Need(destructor + 0x12a, [0x66, 0x83, 0x88, 0x92, 0x1b, 0, 0, 8], "same input manager MenuWord bit8 writer");
        var quiet = Unique(at => Match(code, at, [0x8b, 0xca, 0xe8]) &&
            Match(code, at + 7, [0xc7, 0x47, 0xc, 1, 0, 0, 0]), 0, 20);
        var flush = Call(quiet + 2);
        Need(flush, [0x51, 0x8b, 0x49, 0x2c, 0x85, 0xc9, 0x74], "quiet return actual nullable mouse device");
        Need(flush + 8, [0x6a, 0, 0x8d, 0x54, 0x24, 4, 0xc7, 0x44, 0x24, 4, 0xff, 0xff, 0xff, 0xff,
            0x8b, 1, 0x52, 0x6a, 0, 0x6a, 0x14, 0x51, 0xff, 0x50, 0x28], "NULL-output buffered mouse flush before mode1");

        var effect = Unique(at => Match(code, at, [0x6a, 0xf, 0xe8]) &&
            Match(code, at - 0x1c, [0x8b, 0xc8, 0xe8]) && Match(code, at - 0x15, [0xeb, 2, 0x33, 0xc0]), 0x20, 10);
        var effectCtor = Call(effect - 0x1a);
        Need(effectCtor, [0x51, 0x56, 0x8b, 0xf1, 0x89, 0x74, 0x24, 4, 0xe8], "real shared slot15 effect constructor");
        Need(effectCtor + 0x13, [0x8b, 0xc6, 0xc7, 0x46, 0x58, 0, 0, 0, 0, 0x5e, 0x59, 0xc3],
            "effect resource-null constructor leaves later scalar and byte unwritten");
        var entering = Unique(at => Match(code, at, [0x51, 0x56, 0x8b, 0xf1, 0x8b, 0x4e, 4, 0x85, 0xc9, 0x74, 0x3a, 0x68, 0xc1, 0xf, 0, 0]), 0, 0x68);
        Need(entering + 0x5c, [0xc7, 0x46, 0x24, 8, 0, 0, 0], "entering lifecycle8 follows transition return");
        var transitionWrapper = Call(entering + 0x54);
        Need(transitionWrapper, [0x55, 0x8b, 0xec, 0xf3, 0xf, 0x10, 0x45, 0xc, 0x51, 0xf3, 0xf, 0x11, 4, 0x24, 0xff, 0x75, 8], "actual queue replacement wrapper");
        var allocation = Call(transitionWrapper + 0x11);
        Need(allocation + 0x5c, [0xc7, 0x87, 0x80, 2, 0, 0, 0xd, 0, 0, 0], "real queue allocation kind13");
        Need(allocation + 0x110, [0xf3, 0xf, 0x5f, 0xc1, 0xf3, 0xf, 0x11, 0x41, 8], "source duration MAXSS before tail publication");
        Need(allocation + 0x157, [0x89, 0xb7, 0x80, 2, 0, 0], "actual prior-kind return after tail publication");
        var leaving = Unique(at => Match(code, at, [0x51, 0x56, 0x8b, 0xf1, 0x68, 0xa3, 0xf, 0, 0, 0x8b, 0x4e, 4, 0xe8]) &&
            Match(code, at + 0x11, [0x84, 0xc0, 0xf, 0x84]), 0, 0x9c);
        Need(leaving + 0x63, [0xc7, 0x46, 0x24, 2, 0, 0, 0], "leaving lifecycle2 after independent queue allocation");
        if (Call(leaving + 0x5b) != transitionWrapper) throw new NotSupportedException("Menu lifecycle arms changed their shared allocator.");
        var closeReference = Unique(at => Match(code, at, [0xf, 0xb6, 0x45, 8, 0x6a, 1, 0x51, 0x8b, 0x49, 4,
            0x66, 0xf, 0x6e, 0xc0, 0xf, 0x5b, 0xc0, 0xf3, 0xf, 0x11, 4, 0x24, 0x68, 0x72, 0x17, 0, 0]), 0, 0x2b);
        if (Call(closeReference + 0x26) != leaving)
            throw new NotSupportedException("Source StartMenu close-reference callback lost its actual leaving transition.");
        var update = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68]) &&
            Match(code, at + 0x11, [0x83, 0xec, 0x20, 0x56, 0x57]) &&
            Match(code, at + 0x27, [0x8b, 0xf9, 0x89, 0x7d, 0xec]) &&
            Match(code, at + 0x6b, [0xc7, 0x80, 0x80, 2, 0, 0, 0xd, 0, 0, 0]), 0, 0x440);
        Need(update + 0x6b, [0xc7, 0x80, 0x80, 2, 0, 0, 0xd, 0, 0, 0], "same actual thread kind13 update scope");
        Need(update + 0x138, [0x83, 0xfa, 2], "independent leaving lifecycle selection");
        Need(update + 0x25a, [0xf3, 0xf, 0x5c, 0x4d, 0xe8], "leaving Float32 one-minus-ratio order");
        Need(update + 0x327, [0xc7, 0x47, 0x24, 1, 0, 0, 0], "entered menu completes its lifecycle1");
        var held = Unique(at => Match(code, at, [0x53, 0x56, 0x57, 0x8b, 0xd9, 0xe8]) &&
            Match(code, at + 0xa, [0x8b, 0xbb, 0xec, 5, 0, 0, 0x8b, 0xf0, 0x85, 0xf6]), 0, 0xa6);
        var commonGetter = Call(held + 5);
        Need(commonGetter, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0xb4, 1, 0, 0, 0x33, 0xc0, 0xc3], "held direct Common animation virtual or absent process");
        Need(held + 0x12, [0x85, 0xf6, 0xf, 0x84], "held Common context null arm");
        Need(held + 0x1a, [0x85, 0xff, 0xf, 0x84], "held first-person pointer null arm");
        Need(held + 0xa2, [0x5f, 0x5e, 0x5b, 0xc3], "held null return before group or timer effects");
        return source;

        int Offset(uint value) => SourceOffset(code, image.CodeBase, value);
        int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
            checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Menu lifecycle lost its actual selected child association.");
        int Unique(Func<int, bool> predicate, int before, int after) =>
            Enumerable.Range(before, code.Length - before - after + 1).Where(predicate).ToArray() is [var at] ? at :
            throw new NotSupportedException("Menu lifecycle source association is absent or ambiguous.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Menu lifecycle changed its original " + role + "."); }
    }
}
