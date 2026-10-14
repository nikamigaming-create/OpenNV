using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPackageStateSource ReadPackageState(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutPackageStateSource.Reviewed(controls); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Package setter changed its selected original image.");
        _ = ReadPlayerFollowingContinuation(path, controls);
        var (code, image) = Load(original);
        var high = Class(".?AVHighProcess@@", 460);
        var current = Get(high, 0x22c);
        Need(current, [0x55, 0x8b, 0xec, 0x57, 0xff, 0x75, 0xc, 0x8b, 0xf9, 0x8b, 0x4d, 8], "current setter's independent extra derivation");
        var extra = Call(current + 0xc);
        Need(current + 0x11, [0x50, 0xff, 0x75, 8, 0x8d, 0x4f, 4], "actual Base-current state receiver");
        var setter = Call(current + 0x18);
        var primary = Get(high, 0x20c);
        Need(primary + 0xa, [0x85, 0xff, 0x75], "nonnull primary wrapper branch");
        Need(primary + 0x31, [0xff, 0x75, 0xc, 0x8b, 0xcf], "primary extra derivation before equality");
        if (Call(primary + 0x36) != extra || Call(primary + 0x43) != setter)
            throw new NotSupportedException("Current/primary did not borrow the same original extra and six-field setter.");
        Need(primary + 0x3b, [0x50, 0x57, 0x8d, 0x8e, 0xe4, 0, 0, 0], "distinct MiddleHigh-primary state receiver");
        Need(extra + 6, [0x8b, 0x4e, 0x30, 0x85, 0xc9, 0x74], "actual target pointer null branch");
        Need(extra + 0x35, [0x33, 0xc0, 0x5e, 0x5d, 0xc2, 4, 0], "target-absent extra returns null");
        Need(setter, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x57, 0x8b, 0x7d, 8, 0x3b, 0x3e, 0x74], "pointer identity before every state child");
        if (setter + 0xe + unchecked((sbyte)code[setter + 0xd]) != setter + 0x47)
            throw new NotSupportedException("Pointer-equal setter no longer skips clear/count/time/context stores.");
        var clear = Call(setter + 0xe);
        Need(setter + 0x13, [0x85, 0xff, 0x74], "new-null return after clear");
        Need(setter + 0x17, [0xf0, 0xff, 0x47, 0x7c, 0xb9], "shared atomic package retain before GameHour");
        var hour = Call(setter + 0x20);
        Need(setter + 0x25, [0xd9, 0x5e, 0x10, 0x89, 0x3e, 0x0f, 0xbe, 0x47, 0x20, 0x50], "Float32 time before package pointer and signed-kind context");
        var context = Call(setter + 0x2f);
        Need(setter + 0x34, [0x89, 0x46, 4, 0x83, 0xc4, 4, 0x8b, 0x45, 0xc, 0xc7, 0x46, 0xc, 0, 0, 0, 0,
            0x89, 0x46, 8, 0x5f, 0x5e, 0x5d, 0xc2, 8, 0], "context then index then previously derived Extra store");
        Need(clear + 8, [0x8b, 0xe, 0x85, 0xc9, 0x74], "old-package null skips release children");
        Need(clear + 0x13, [0x3b, 0x88, 0xc4, 1, 0, 0], "actual Player special-package identity");
        Need(clear + 0x1b, [0xc7, 0x80, 0xc4, 1, 0, 0, 0, 0, 0, 0], "matching Player special pointer store before release");
        Need(clear + 0x27, [0x83, 0xc8, 0xff, 0xf0, 0xf, 0xc1, 0x41, 0x7c, 0x48, 0x75], "atomic decrement and exact zero selection");
        var temporary = Call(clear + 0x34);
        Need(temporary, [0x8b, 0x41, 0x1c, 0xc1, 0xe8, 0xb, 0x24, 1, 0xc3], "independent mutable temporary-package flag");
        Need(clear + 0x3d, [0x8b, 0x3e, 0xc7, 6, 0, 0, 0, 0], "temporary pointer clears before the deletion child");
        Need(clear + 0x71, [0xc7, 6, 0, 0, 0, 0, 0x8b, 0x4e, 4, 0x85, 0xc9, 0x74], "ordinary pointer clear and independent context-null skip");
        Need(clear + 0x7e, [0x8b, 1, 0x6a, 1, 0xff, 0x10, 0xc7, 0x46, 4, 0, 0, 0, 0], "real context destruction before its pointer clear");
        Need(clear + 0x8c, [0xc7, 0x46, 0x10, 0, 0, 0x80, 0xbf, 0xc7, 0x46, 0xc, 0xff, 0xff, 0xff, 0xff,
            0xc7, 0x46, 0x14, 0, 0, 0, 0, 0xc7, 0x46, 8, 0, 0, 0, 0], "ordered time/index/flags/Extra clear stores");
        Need(hour, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x41, 0xc, 0x85, 0xc0, 0x74, 0x11,
            0xf3, 0xf, 0x10, 0x40, 0x24], "actual retained GLOB GameHour read");
        Need(hour + 0x1c, [0xc7, 0x45, 0xfc, 0, 0, 0x40, 0x41, 0xd9, 0x45, 0xfc, 0x8b, 0xe5, 0x5d, 0xc3], "genuine null-hour literal twelve");
        Need(context + 0x22, [0x8b, 0x45, 8, 0x33, 0xc9, 0x83, 0xc0, 0xfe, 0x83, 0xf8, 0xe, 0xf, 0x87], "signed context bounds after kind minus two");
        Need(context + 0x33, [0xf, 0xb6, 0x80], "actual context dispatch remap");
        Need(context + 0x3a, [0xff, 0x24, 0x85], "actual context dispatch table");
        var remap = image.Read(Word(code, context + 0x36), 15);
        if (!remap.SequenceEqual(new byte[] { 0, 5, 5, 5, 5, 5, 5, 5, 5, 5, 1, 2, 3, 5, 4 }))
            throw new NotSupportedException("Package context dispatch changed its five allocating signed kinds.");
        var table = image.Read(Word(code, context + 0x3d), 24);
        var fallback = Offset(Word(table, 20));
        Need(fallback, [0x8b, 0xc1, 0x8b, 0x4d, 0xf4, 0x64, 0x89, 0xd, 0, 0, 0, 0, 0x59, 0x8b, 0xe5, 0x5d, 0xc3], "actual default null context return");
        for (var index = 0; index < 5; ++index)
        {
            var allocation = Offset(Word(table, index * 4));
            if (allocation == fallback || !Match(code, allocation, index == 4 ? new byte[] { 0x6a, 8 } : new byte[] { 0x6a, new byte[] { 24, 100, 56, 20 }[index] }))
                throw new NotSupportedException("Allocating context arm lost its real distinct allocation child.");
        }
        var packageConstructor = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68]) &&
            Match(code, at + 0xc0, [0xc7, 0x47, 0x18, 0xff, 0xff, 0xff, 0xff, 0xc6, 0x47, 0x20, 0xff]) &&
            Match(code, at + 0xd8, [0xc7, 0x47, 0x7c, 0, 0, 0, 0]), 0xfb);
        Need(packageConstructor + 0x3b, [0xc7, 7], "actual package constructor class publication");
        var packageTable = Word(code, packageConstructor + 0x3d);
        if (image.SourceClassTables(".?AVTESPackage@@", 42).Count(candidate => candidate == packageTable) != 1)
            throw new NotSupportedException("Package constructor selected no exact original package class table.");
        Need(packageConstructor + 0x83, [0xc7, 0x47, 0x1c, 0, 0, 0, 0], "independent package flags constructor");
        var loader = Get(packageTable, 0x20);
        Need(loader + 0x598, [0x3d, 0x50, 0x4b, 0x44, 0x54], "selected PKDT loader branch");
        Need(loader + 0x72c, [0x66, 0xf, 0xd6, 0x47, 0x1c, 0x89, 0x47, 0x24], "loaded flags/kind remain distinct from procedure/count");
        return source;

        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var selected] ? selected :
            throw new NotSupportedException("Mutable package source class is absent or ambiguous: " + name);
        int Get(uint selected, uint slot) => Offset(Word(image.Read(selected + slot, 4), 0));
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Package state lost an exact selected direct child.");
        int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
            throw new NotSupportedException("Mutable package constructor is absent or ambiguous.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Package state changed its original " + role + "."); }
    }
}
