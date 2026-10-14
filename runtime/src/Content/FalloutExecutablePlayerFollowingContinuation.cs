using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerFollowingContinuationSource ReadPlayerFollowingContinuation(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutPlayerFollowingContinuationSource.Reviewed(controls); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Following Player continuation changed its selected original image.");
        // This already checks the actual Character/Actor initializer, Low
        // allocation, High constructor/copy/publication and all four stores.
        _ = ReadPlayerReferenceHud(path, controls.Main);
        var (code, image) = Load(original);
        var player = Class(".?AVPlayerCharacter@@", 191);
        var high = Class(".?AVHighProcess@@", 460);
        var middle = Class(".?AVMiddleHighProcess@@", 460);
        var selectedCallback = Get(player, 0x258);
        Need(selectedCallback, [0xc2, 4, 0], "selected void callback with ignored argument");
        var life = Get(player, 0x22c);
        Need(life, [0x55, 0x8b, 0xec, 0x8b, 0x81, 0xfc, 0, 0, 0, 0x8a, 0x55, 8,
            0x83, 0xf8, 2, 0x74, 9, 0x84, 0xd2, 0x74, 0xb, 0x83, 0xf8, 1, 0x75, 0x10,
            0xb0, 1, 0x5d, 0xc2, 4, 0, 0x83, 0xf8, 1, 0x74, 0xf5, 0x83, 0xf8, 6, 0x74, 0xf0,
            0x32, 0xc0, 0x5d, 0xc2, 4, 0], "pure raw Actor-life predicate");
        var constructor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
            Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
        RequirePlayerMovementControlConstructor(code, constructor);
        Need(constructor + 0x653, [0xc6, 0x87, 0x28, 6, 0, 0, 0], "independent constructor timer byte");
        Need(constructor + 0x7fc, [0xff, 0x90, 0x58, 2, 0, 0], "second-byte store followed by selected callback");
        Need(constructor + 0x82e, [0xff, 0x90, 0x58, 2, 0, 0], "first-byte store followed by selected callback");
        foreach (var table in new[] { high, middle })
        {
            Need(Get(table, 0x228), [0x8b, 0x41, 4, 0xc3], "independent inherited current package getter");
            Need(Get(table, 0x208), [0x8b, 0x81, 0xe4, 0, 0, 0, 0xc3], "independent primary package getter");
        }
        var middleConstructor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
            Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0]), 0x440);
        Need(middleConstructor + 0x35, [0x8d, 0x8e, 0xe4, 0, 0, 0], "distinct primary state construction receiver");
        var stateConstructor = Call(middleConstructor + 0x3f);
        Need(stateConstructor, [0xc7, 1, 0, 0, 0, 0, 0x8b, 0xc1], "first package pointer constructor");
        foreach (var (relative, offset, bits) in new[] { (8, 4, 0u), (15, 8, 0u), (22, 0x10, 0xbf800000u),
            (29, 0xc, uint.MaxValue), (36, 0x14, 0u) })
        {
            Need(stateConstructor + relative, [0xc7, 0x41, (byte)offset], "independent package state constructor field");
            if (Word(code, stateConstructor + relative + 3) != bits) throw new NotSupportedException("Package state changed a full constructor word.");
        }
        Need(stateConstructor + 43, [0xc3], "package constructor pure return");
        var middleLow = Call(middleConstructor - 5); var low = Call(middleLow + 0x29); var basis = Call(low + 0x29);
        Need(basis + 0xa, [0x8d, 0x4e, 4], "Base current state construction receiver");
        if (Call(basis + 0x13) != stateConstructor) throw new NotSupportedException("Current package did not inherit the same independent complete constructor.");
        var copy = Get(high, 4); var lowCopy = Call(copy + 0x18b);
        Need(lowCopy + 0xd, [0x8d, 0x4e, 4], "Low current package move receiver");
        Need(lowCopy + 0x13, [0x8d, 0x47, 4, 0x50], "independent High current package move destination");
        var move = Call(lowCopy + 0x17);
        Need(move, [0x55, 0x8b, 0xec, 0x8b, 0x55, 8, 0x8b, 1, 0x89, 2], "move package pointer before procedure/index/time");
        foreach (var (relative, offset) in new[] { (10, 4), (16, 0xc), (22, 0x10) })
            Need(move + relative, [0x8b, 0x41, (byte)offset, 0x89, 0x42, (byte)offset], "exact moved word");
        Need(move + 28, [0xc7, 1, 0, 0, 0, 0, 0xc7, 0x41, 4, 0, 0, 0, 0,
            0xc7, 0x41, 0xc, 0xff, 0xff, 0xff, 0xff, 0xc7, 0x41, 0x10, 0, 0, 0x80, 0xbf, 0x5d, 0xc2, 4, 0],
            "four source clears without extra/flags/callback or reference count effect");
        var following = Get(player, 0x2f8) + 0xca2;
        int? late = null;
        foreach (var (relative, field) in new[] { (0, 0x6e5), (13, 0x6e4), (26, 0x6e6), (39, 0x6e7) })
        {
            Need(following + relative, [0x80, 0xbf, (byte)field, (byte)(field >> 8), 0, 0, 0, 0x0f, 0x85], "ordered following inhibition byte");
            var target = NearConditional(following + relative + 7);
            if (late is { } expected && target != expected) throw new NotSupportedException("Following inhibition lost its shared late-tail target.");
            late = target;
        }
        Need(following + 52, [0x8b, 7, 0x8b, 0xcf, 0x6a, 0, 0x8b, 0x80, 0x2c, 2, 0, 0, 0xff, 0xd0, 0x84, 0xc0, 0x0f, 0x85],
            "false-argument life predicate after returned inhibition");
        if (NearConditional(following + 68) != late) throw new NotSupportedException("Following life predicate changed its late-tail branch.");
        Need(following + 79, [0x8b, 0x48, 0x60, 0x8b, 1, 0x8b, 0x80, 0x28, 2, 0, 0, 0xff, 0xd0, 0x85, 0xc0, 0x74, 0x10], "current package getter and null skip");
        Need(following + 106, [0xff, 0x90, 0x30, 2, 0, 0], "selected nonnull current clear");
        Need(following + 117, [0x8b, 0x48, 0x60, 0x8b, 1, 0x8b, 0x80, 8, 2, 0, 0, 0xff, 0xd0, 0x85, 0xc0, 0x74, 0x10], "independent primary getter and null skip");
        Need(following + 144, [0xff, 0x90, 0x10, 2, 0, 0], "selected nonnull primary clear");
        Need(following + 150, [0x80, 0xbf, 0x28, 6, 0, 0, 0, 0x74, 0x32], "zero timer byte skips independent scalar");
        Need(following + 209, [0x8b, 0x75, 0xe0, 0x8b, 0xce, 0x6a, 1, 0x6a, 7, 0xc6, 0x45, 0xd6, 0],
            "retained input manager and exact rising Ready Item query");
        var inputQuery = Call(following + 222);
        Need(following + 227, [0x85, 0xc0, 0x75, 0x30, 0x50, 0x6a, 7, 0x8b, 0xce],
            "zero rising count selects exact held query");
        Need(following + 241, [0x85, 0xc0, 0x75, 0x22, 0x6a, 2, 0x6a, 7, 0x8b, 0xce],
            "zero held count selects exact falling query");
        if (Call(following + 236) != inputQuery || Call(following + 251) != inputQuery)
            throw new NotSupportedException("Ready Item queries did not use the same retained source input-manager child.");
        Need(following + 256, [0x85, 0xc0, 0x0f, 0x85], "falling count independently selects following frame services");
        Need(following + 264, [0xa3], "all-zero count selects independent hold-time global store");
        Need(following + 269, [0xa2], "all-zero count selects independent latch global store");
        Need(following + 279, [0xf6, 0x87, 0xdc, 5, 0, 0, 8], "positive rising/held count selects actual fighting bit");
        return source;

        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Following continuation lost its unique selected class: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Unique(Func<int, bool> predicate, int tail)
        {
            var found = Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray();
            return found is [var at] ? at : throw new NotSupportedException("Following continuation constructor is absent or ambiguous.");
        }
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Following continuation lost an actual direct constructor/copy child.");
        int NearConditional(int at) => checked(at + 6 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 2)));
        void Need(int at, byte[] bytes, string role)
        {
            if (!Match(code, at, bytes)) throw new NotSupportedException("Following continuation changed its original " + role + ".");
        }
    }
}
