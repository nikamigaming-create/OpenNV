using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerPostAnimationSource ReadPlayerPostAnimation(string path, FalloutPlayerReturnTailSource tail)
    {
        tail.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != tail.Scene.Main.EngineSha256)
            throw new InvalidDataException("Player post-animation changed its actual original source.");
        var (code, image) = Load(original);
        var player = Table(".?AVPlayerCharacter@@", 191); var high = Table(".?AVHighProcess@@", 465);
        var frame = Get(player, 0x2f8);
        Need(frame + 0xca2, [0x80, 0xbf, 0xe5, 6, 0, 0, 0, 0x0f, 0x85], "same following branch");
        var late = checked(frame + 0xcaf + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(frame + 0xcab)));
        var post = late + 0x38a;
        Need(post, [0x8a, 0x8f, 0xa8, 5, 0, 0, 0x84, 0xc9, 0x74, 0x2d], "fresh initial current view");
        var prepare = Call(post + 0x2a);
        if (Call(post + 0x64) != prepare) throw new NotSupportedException("View arms changed their one actual preparation child.");
        Need(post + 0x2f, [0x8a, 0x8f, 0xa8, 5, 0, 0, 0x84, 0xc9, 0x0f, 0x94, 0xc0,
            0x88, 0x87, 0xa8, 5, 0, 0, 0x84, 0xc9, 0x75, 0x25], "fresh post-preparation read before first store/second arm");
        var status = Call(post + 0x76); var animationFrame = Call(post + 0xa1);
        if (Call(post + 0xc3) != status || Call(post + 0xee) != animationFrame)
            throw new NotSupportedException("Two actual views changed their independent status/AnimData frame bodies.");
        foreach (var at in new[] { post + 0x7b, post + 0xc8 })
            Need(at, [0x80, 0xbf, 0xa8, 5, 0, 0, 0, 0x75, 8, 0x8b, 0x87, 0xec, 5, 0, 0], "fresh view selects independent direct first pointer");
        Need(post + 0xa6, [0x80, 0xbf, 0xa8, 5, 0, 0, 0], "fresh view read before second toggle");
        Need(post + 0xb3, [0x0f, 0x94, 0xc0], "second equality toggle");
        Need(post + 0xbd, [0x88, 0x87, 0xa8, 5, 0, 0], "SAME second view field store");
        Need(post + 0xf3, [0x6a, 0, 0x8b, 0xcf, 0xe8], "unconditional camera false child");
        Need(post + 0xfc, [0x8b, 0x4f, 0x3c, 0x6a, 0, 0x6a, 1, 0x57, 0xe8], "actual ParentCELL attachment true/false");
        Need(post + 0x112, [0x6a, 1, 0x8b, 1, 0x8b, 0x80, 0xd0, 1, 0, 0, 0xff, 0xd0], "fresh model getter before independent resource offer");
        Need(post + 0x129, [0x6a, 0, 0x8b, 0x4f, 0x3c, 0xe8], "same actual CELL water false child");
        Need(post + 0x133, [0x80, 0xbf, 0xe6, 6, 0, 0, 0, 0x89, 0x87, 0, 7, 0, 0, 0x0f, 0x84],
            "inhibition read then water store then branch");

        Need(prepare, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x54, 0x53, 0x56, 0x57, 0x8b, 0xf9, 0x6a, 0], "current life false argument");
        Need(prepare + 0xf, [0x8b, 0x80, 0x2c, 2, 0, 0, 0xff, 0xd0], "actual Life virtual before activity");
        Need(prepare + 0x23, [0x8b, 0x80, 0x30, 2, 0, 0, 0xff, 0xd0], "independent activity virtual before current view");
        Need(prepare + 0x3c, [0x80, 0xb8, 0xa8, 5, 0, 0, 0], "Player current view-zero early return");
        Need(prepare + 0x4d, [0xff, 0x90, 0xe4, 1, 0, 0], "actual selected AnimData getter");
        var controller = Call(prepare + 0x5a);
        Need(controller, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0x88, 2, 0, 0, 0x33, 0xc0, 0xc3],
            "current controller getter before null tests");
        Need(prepare + 0x62, [0x85, 0xdb, 0x0f, 0x84], "context null test follows controller getter");
        Need(prepare + 0x6a, [0x8b, 0x4f, 0x60, 0x85, 0xc9, 0x0f, 0x84], "independent current process null test");
        Need(prepare + 0x75, [0x85, 0xc0, 0x0f, 0x84], "independent current controller null test");
        Need(prepare + 0x7d, [0x83, 0x79, 0x28, 0, 0x75, 5], "actual process prerequisite conditional");
        Need(Call(prepare + 0x83), [0xc2, 0, 0], "actual prerequisite child pure return");
        Need(prepare + 0x88, [0x8b, 0xb3, 0xf0, 0, 0, 0], "selected current slot four before group logic");
        Need(prepare + 0xf4, [0x0f, 0xb7, 0x43, 0x54], "selected current group four read");
        Need(prepare + 0x104, [0x74, 0x12, 0x0f, 0xb7, 0x43, 0x54], "independent selected timed-group alternatives");
        Need(prepare + 0x113, [0x83, 0xf8, 0x4d], "second timed preparation group");
        var lowGroup = Call(prepare + 0xf9);
        Need(lowGroup, [0x55, 0x8b, 0xec, 0x0f, 0xb6, 0x45, 8, 0x5d, 0xc3], "real unsigned low-byte group projection");
        Need(prepare + 0x158, [0x6a, 1, 0xe8], "independent explicit first context read");
        Need(prepare + 0x164, [0x8b, 0x86, 0xf0, 0, 0, 0], "independent first slot four");
        Need(prepare + 0x1d9, [0x0f, 0xb7, 0x46, 0x54], "independent first group four");
        Need(Get(player, 0x1e4), [0x8b, 0x81, 0xfc, 5, 0, 0, 0x85, 0xc0, 0x75, 0x17,
            0x38, 0x81, 0xa8, 5, 0, 0, 0x75, 0x0a, 0x8b, 0x81, 0xec, 5, 0, 0, 0x85, 0xc0, 0x75, 5, 0xe9],
            "same override-first-current-view-direct-first-otherwise-Common selector");
        Need(Get(high, 0x408), [0x0f, 0xbe, 0x81, 0x3c, 1, 0, 0, 0xc3], "actual signed activity field");
        var load = Get(player, 0x1c8);
        Need(load + 0x3b5, [0x8a, 0x87, 0xa8, 5, 0, 0], "outer Player load saves same current view");
        Need(load + 0x3bf, [0x88, 0x85, 0x93, 0xfe, 0xff, 0xff, 0xc6, 0x87, 0xa8, 5, 0, 0, 1], "actual saved byte then temporary view one");
        Need(load + 0x3d1, [0x8a, 0x8d, 0x93, 0xfe, 0xff, 0xff, 0x88, 0x8f, 0xa8, 5, 0, 0], "exact actual saved view restore after inherited child");
        return FalloutPlayerPostAnimationSource.Reviewed(tail);

        uint Table(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Player post-animation class is absent or ambiguous: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
            checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Post-animation selected child association changed.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Player post-animation changed " + role + "."); }
    }
}
