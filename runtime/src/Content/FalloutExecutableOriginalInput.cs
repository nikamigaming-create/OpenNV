using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutOriginalInputSource ReadOriginalInputQueries(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutOriginalInputSource.Reviewed(controls); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Original input source changed its selected original executable.");
        _ = ReadPlayerReferenceHud(path, controls.Main);
        var (code, image) = Load(original);
        var tables = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (tables is not [var player]) throw new NotSupportedException("Original input lost its unique selected Player class.");
        var caller = Offset(Word(image.Read(player + 0x2f8, 4), 0));
        Need(caller + 0x18d, [0x8b, 0x4f, 0x60, 0x89, 0x45, 0xe0], "actual current manager result retained by Player");
        var (constructor, _) = RequirePlayerInputManager(code, image, Call(caller + 0x188));
        Need(caller + 0xd73, [0x8b, 0x75, 0xe0, 0x8b, 0xce, 0x6a, 1, 0x6a, 7, 0xc6, 0x45, 0xd6, 0], "same retained manager Ready Item rising invocation");
        var query = Call(caller + 0xd80);
        Need(caller + 0xd85, [0x85, 0xc0, 0x75, 0x30, 0x50, 0x6a, 7, 0x8b, 0xce], "conditional held after zero rising count");
        Need(caller + 0xd93, [0x85, 0xc0, 0x75, 0x22, 0x6a, 2, 0x6a, 7, 0x8b, 0xce], "conditional falling after zero held count");
        if (Call(caller + 0xd8e) != query || Call(caller + 0xd9d) != query)
            throw new NotSupportedException("Player Ready Item children changed their genuine shared original query.");
        RequireOriginalInputConstruction(code, image, constructor);
        RequireOriginalInputQuery(code, image, query);
        RequireOriginalInputPoll(code, image, query);
        return source;

        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Call(int at) => OriginalInputCall(code, at);
        void Need(int at, byte[] bytes, string role) => OriginalInputNeed(code, at, bytes, role);
    }
    private static void RequireOriginalInputConstruction(byte[] code, Image image, int constructor)
    {
        Need(0x34, [0xc7, 0x86, 0xf4, 0x18, 0, 0, 0, 0, 0, 0], "independent original joystick count");
        Need(0x52, [0x66, 0x89, 0x8e, 0x92, 0x1b, 0, 0], "constructor menu-word zero");
        Need(0x1a0, [0x8b, 0x3d], "actual mouse swap import");
        Need(0x1a6, [0x6a, 0, 0xff, 0xd7, 0x50, 0x89, 0x86, 0x4c, 0x1b, 0, 0, 0xff, 0xd7, 0xff, 0x15], "swap returned store before host restore");
        if (image.ImportName(Word(code, constructor + 0x1a2)) != "SwapMouseButton" ||
            image.ImportName(Word(code, constructor + 0x1b5)) != "GetDoubleClickTime")
            throw new NotSupportedException("Original input platform constructor changed its actual USER32 imports.");
        Need(0x1b9, [0x89, 0x86, 0x54, 0x1b, 0, 0], "actual double-click return store");
        Need(0x2bd, [0x68, 0, 2, 0, 0, 0x8d, 0x86, 0xf8, 0x18, 0, 0, 0x6a, 0, 0x50], "both complete keyboard buffers zero");
        Need(0x2d0, [0x0f, 0x57, 0xc0], "zero register before both complete mouse states");
        Need(0x2d6, [0x0f, 0x11, 0x86, 0x24, 0x1b, 0, 0], "first mouse-state zero extent");
        Need(0x2e2, [0x0f, 0x11, 0x86, 0x34, 0x1b, 0, 0], "second mouse-state zero extent");
        Need(0x2eb, [0x66, 0x0f, 0xd6, 0x86, 0x44, 0x1b, 0, 0], "remaining mouse-state zero extent");
        Need(0x4d9, [0xc6, 6, 0], "distinct controller-suppression constructor zero");
        void Need(int relative, byte[] bytes, string role) => OriginalInputNeed(code, constructor + relative, bytes, role);
    }
    private static void RequireOriginalInputQuery(byte[] code, Image image, int query)
    {
        Need(0, [0x51, 0x53, 0x8b, 0x5c, 0x24, 0x0c, 0x56, 0x8b, 0xf1, 0x83, 0xfb, 0x1c, 0x0f, 0x8d], "signed ordinary action range");
        Need(0x1c, [0x38, 0x0d], "current controller selection before binding");
        Need(0x24, [0x8a, 0x85, 0xe8, 0x1b, 0, 0, 0x8b, 0xce], "independent selected gamepad binding");
        Need(0x42, [0x57, 0x33, 0xff, 0x81, 0xc5, 0x94, 0x1b, 0, 0], "keyboard before mouse source table");
        Need(0x50, [0x8a, 0x45, 0, 0x3c, 0xff, 0x74, 0x14, 0x53, 0x50, 0x57, 0x8b, 0xce], "FF skip before dispatcher");
        var dispatcher = OriginalInputCall(code, query + 0x5c);
        Need(0x61, [0x8b, 0x4c, 0x24, 0x10, 3, 0xc8, 0x89, 0x4c, 0x24, 0x10, 0x47, 0x83, 0xc5, 0x1c, 0x83, 0xff, 2, 0x7c, 0xdc], "two ordered device results added as Int32");
        Need(0x7d, [0x83, 0xfb, 0x0e, 0x75, 0x31, 0x85, 0xc9, 0x7e, 0x2d, 0x0f, 0xb7, 0x86, 0x92, 0x1b, 0, 0, 0xa8, 8], "positive Menu Mode source word read");
        Need(0x91, [0x83, 0xf0, 8, 0x33, 0xc9, 0x66, 0x89, 0x86, 0x92, 0x1b, 0, 0], "menu bit clear and count zero before mode test");
        Need(0xa0, [0x85, 0xed, 0x74, 5, 0x83, 0xfd, 1, 0x75, 0xa, 0x83, 0xc8, 8, 0x66, 0x89, 0x86, 0x92, 0x1b, 0, 0], "menu bit restore only held and rising");
        OriginalInputNeed(code, dispatcher + 0x19, [0xff, 0x74, 0x24, 0x14, 0x0f, 0xb6, 0x44, 0x24, 0x14, 0x50], "mouse dispatcher actual byte binding");
        OriginalInputNeed(code, dispatcher + 0x2d, [0xff, 0x74, 0x24, 0x14, 0x0f, 0xb6, 0x44, 0x24, 0x14, 0x50], "keyboard dispatcher actual byte binding");
        var mouse = OriginalInputCall(code, dispatcher + 0x23); var keyboard = OriginalInputCall(code, dispatcher + 0x37);
        RequireOriginalKeyboardQuery(code, image, keyboard); RequireOriginalMouseQuery(code, image, mouse);
        // The extra keyboard actions use source signed bit7 comparisons and
        // deliberately ignore the ordinary mode argument.
        Need(0xd6, [0x38, 0x86, 0xaf, 0x1a, 0, 0, 0x7c, 0x46], "extra screenshot previous signed byte branch");
        Need(0xde, [0x38, 0x86, 0xaf, 0x19, 0, 0, 0xb9, 1, 0, 0, 0], "extra screenshot current signed byte");
        Need(0xf4, [0x38, 0x86, 0x21, 0x1a, 0, 0, 0x7c, 0x28], "extra console previous signed byte branch");
        Need(0xfc, [0x38, 0x86, 0x21, 0x19, 0, 0, 0xb9, 1, 0, 0, 0], "extra console current signed byte");
        Need(0x110, [0x80, 0xbe, 0xf9, 0x19, 0, 0, 0], "extra Escape previous byte");
        Need(0x119, [0x80, 0xbe, 0xf9, 0x18, 0, 0, 0], "extra Escape current byte");
        void Need(int relative, byte[] bytes, string role) => OriginalInputNeed(code, query + relative, bytes, role);
    }
    private static void RequireOriginalKeyboardQuery(byte[] code, Image image, int keyboard)
    {
        OriginalInputNeed(code, keyboard, [0x8b, 0x44, 0x24, 8, 0x33, 0xd2, 0x56, 0x8b, 0xf1, 0x83, 0xf8, 3, 0x77, 0x7b, 0xff, 0x24, 0x85], "keyboard unsigned mode switch");
        RequireOriginalInputTargets(image, Word(code, keyboard + 0x11), keyboard, image.CodeBase, [0x15, 0x2a, 0x4b, 0x6c]);
        foreach (var (at, displacement, instruction) in new[] { (0x19, 0x18f8u, new byte[] { 0x0f, 0xb6, 0x94, 0x30 }),
            (0x2e, 0x19f8u, new byte[] { 0x38, 0x94, 0x30 }), (0x37, 0x18f8u, new byte[] { 0x38, 0x94, 0x30 }),
            (0x4f, 0x18f8u, new byte[] { 0x38, 0x94, 0x30 }), (0x58, 0x19f8u, new byte[] { 0x38, 0x94, 0x30 }),
            (0x70, 0x19f8u, new byte[] { 0x8a, 0x8c, 0x30 }), (0x77, 0x18f8u, new byte[] { 0x32, 0x8c, 0x30 }) })
        {
            OriginalInputNeed(code, keyboard + at, instruction, "current/previous keyboard source byte");
            if (Word(code, keyboard + at + instruction.Length) != displacement) throw new NotSupportedException("Original keyboard query changed its actual byte-buffer association.");
        }
        OriginalInputNeed(code, keyboard + 0x83, [0xf6, 0xc1, 0x80, 0x0f, 0x45, 0xd0], "keyboard either-edge bit7 result");
        OriginalInputNeed(code, keyboard + 0x21, [0xc1, 0xea, 7, 0x8b, 0xc2, 0x5e, 0xc2, 8, 0], "keyboard held bit7 result");
        OriginalInputNeed(code, keyboard + 0x35, [0x7c, 0x52], "keyboard rising rejects previous bit7");
        OriginalInputNeed(code, keyboard + 0x3e, [0x7d, 0x49], "keyboard rising requires current bit7");
        OriginalInputNeed(code, keyboard + 0x56, [0x7c, 0x31], "keyboard falling rejects current bit7");
        OriginalInputNeed(code, keyboard + 0x5f, [0x7d, 0x28], "keyboard falling requires previous bit7");
    }
    private static void RequireOriginalMouseQuery(byte[] code, Image image, int mouse)
    {
        OriginalInputNeed(code, mouse, [0x8b, 0x44, 0x24, 4, 0x33, 0xd2, 0x83, 0xf8, 8, 0x0f, 0x84], "wheel before mode and swap");
        OriginalInputNeed(code, mouse + 0xf, [0x83, 0xf8, 9, 0x0f, 0x84], "negative wheel before mode and swap");
        OriginalInputNeed(code, mouse + 0x1d, [0x39, 0x91, 0x4c, 0x1b, 0, 0], "left current swap read");
        OriginalInputNeed(code, mouse + 0x31, [0x39, 0x91, 0x4c, 0x1b, 0, 0], "right current swap read");
        OriginalInputNeed(code, mouse + 0x3a, [0x8b, 0x74, 0x24, 0x0c, 0x83, 0xfe, 4, 0x0f, 0x87], "mouse unsigned mode switch");
        RequireOriginalInputTargets(image, Word(code, mouse + 0x4a), mouse, image.CodeBase, [0x4e, 0x5f, 0x7c, 0x99, 0xb4]);
        foreach (var (at, bytes) in new[] { (0x4e, new byte[] { 0x0f, 0xb6, 0x94, 8, 0x30, 0x1b, 0, 0 }),
            (0x5f, new byte[] { 0x38, 0x94, 8, 0x44, 0x1b, 0, 0 }), (0x68, new byte[] { 0x38, 0x94, 8, 0x30, 0x1b, 0, 0 }),
            (0x7c, new byte[] { 0x38, 0x94, 8, 0x30, 0x1b, 0, 0 }), (0x85, new byte[] { 0x38, 0x94, 8, 0x44, 0x1b, 0, 0 }),
            (0x99, new byte[] { 0x0f, 0xb6, 0x94, 8, 0x44, 0x1b, 0, 0 }), (0xa1, new byte[] { 0x0f, 0xb6, 0x84, 8, 0x30, 0x1b, 0, 0 }) })
            OriginalInputNeed(code, mouse + at, bytes, "current/previous actual mouse bytes");
        OriginalInputNeed(code, mouse + 0xb4, [0x83, 0xf8, 7, 0x77, 0xf, 0x38, 0x94, 8, 0x58, 0x1b, 0, 0], "separate mode-four bounded byte");
        OriginalInputNeed(code, mouse + 0xce, [0x39, 0x91, 0x2c, 0x1b, 0, 0, 0x7c, 0xd], "signed negative wheel");
        OriginalInputNeed(code, mouse + 0xdb, [0x39, 0x91, 0x2c, 0x1b, 0, 0, 0x7e, 0xf3], "signed positive wheel");
    }
    private static void RequireOriginalInputPoll(byte[] code, Image image, int query)
    {
        var selection = Word(code, query + 0x1e);
        var candidates = new List<int>();
        for (var at = 0; at <= code.Length - 0x174; ++at)
            if (Match(code, at, [0x0f, 0x10, 5]) && Match(code, at + 7, [0x53, 0x56, 0x68]) &&
                Word(code, at + 3) == Word(code, at + 10) && Match(code, at + 0x1e, [0x85, 0xc0, 0x75, 8, 0x38, 3]) &&
                Match(code, at + 0x2f, [0xa2]) && Word(code, at + 0x30) == selection) candidates.Add(at);
        if (candidates is not [var poll]) throw new NotSupportedException("Original input query has no unique actual current selector poll.");
        var imported = OriginalInputCall(code, poll + 0x19);
        OriginalInputNeed(code, imported, [0xff, 0x25], "actual controller import thunk");
        if (image.OriginalInputImport(Word(code, imported + 2)) != ("XINPUT1_3.dll", (ushort)2))
            throw new NotSupportedException("Original input current selection changed its actual XINPUT1_3 ordinal2 producer.");
        var currentController = Word(code, poll + 3); var previousController = Word(code, poll + 0x15);
        if (image.ReadLoaderData(currentController, 16).Any(value => value != 0) || image.ReadLoaderData(previousController, 16).Any(value => value != 0))
            throw new NotSupportedException("Original input changed its complete original controller loader buffers.");
        OriginalInputNeed(code, poll + 0xb5, [0x8b, 0x53, 0x2c, 0x85, 0xd2, 0x74, 0x4e], "actual keyboard device presence");
        OriginalInputNeed(code, poll + 0xbc, [0x8d, 0xab, 0xf8, 0x18, 0, 0, 0xb9, 0x40, 0, 0, 0, 0x8d, 0xbb, 0xf8, 0x19, 0, 0, 0x8b, 0xf5, 0xf3, 0xa5], "complete current-to-previous keyboard copy before acquire");
        OriginalInputNeed(code, poll + 0xd4, [0xff, 0x50, 0x1c, 0x85, 0xc0, 0x74, 0x17, 0x83, 0xf8, 1, 0x74, 0x12], "keyboard acquire accepts exactly zero or one");
        OriginalInputNeed(code, poll + 0xe0, [0x68, 0, 1, 0, 0, 0x6a, 0, 0x55], "failed keyboard acquire clears current only");
        OriginalInputNeed(code, poll + 0xf8, [0xff, 0x51, 0x64, 0x8b, 0x43, 0x2c, 0x55, 0x68, 0, 1, 0, 0, 0x50, 0x8b, 8, 0xff, 0x51, 0x24], "actual keyboard poll/read with unchecked state HRESULT");
        OriginalInputNeed(code, poll + 0x12f, [0x0f, 0x57, 0xc0, 0x66, 0x0f, 0xd6, 0x83, 0x58, 0x1b, 0, 0], "separate mode-four clear before mouse acquire");
        OriginalInputNeed(code, poll + 0x140, [0xff, 0x51, 0x1c, 0x85, 0xc0, 0x74, 0x15, 0x83, 0xf8, 1, 0x74, 0x10], "mouse acquire accepts exactly zero or one");
        OriginalInputNeed(code, poll + 0x14c, [0x0f, 0x57, 0xc0, 0x0f, 0x11, 6, 0xc7, 0x46, 0x10, 0, 0, 0, 0], "failed mouse acquire clears complete current state");
        OriginalInputNeed(code, poll + 0x162, [0xff, 0x51, 0x64, 0x8b, 0x43, 0x30, 0x56, 0x6a, 0x14, 0x50, 0x8b, 8, 0xff, 0x51, 0x24, 0x5e, 0x5b, 0xc3], "actual mouse poll/read with unchecked state HRESULT");
    }
    private static int OriginalInputCall(byte[] code, int at)
    {
        OriginalInputNeed(code, at, [0xe8], "direct selected caller/callee association");
        return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
    }
    private static void OriginalInputNeed(byte[] code, int at, byte[] bytes, string role)
    { if (!Match(code, at, bytes)) throw new NotSupportedException("Original input changed its source " + role + "."); }
    private static void RequireOriginalInputTargets(Image image, uint table, int origin, uint codeBase, int[] offsets)
    {
        var bytes = image.Read(table, checked(offsets.Length * sizeof(uint)));
        for (var index = 0; index < offsets.Length; ++index)
            if (Word(bytes, index * sizeof(uint)) != checked(codeBase + (uint)(origin + offsets[index])))
                throw new NotSupportedException("Original input changed an actual ordered query switch target.");
    }
    private sealed partial class Image
    {
        internal (string Library, ushort Ordinal)? OriginalInputImport(uint slot)
        {
            var directory = headers.PEHeader!.ImportTableDirectory;
            for (var at = 0; at <= directory.Size - 20; at += 20)
            {
                var row = Read(checked(Base + (uint)directory.RelativeVirtualAddress + (uint)at), 20);
                var names = Word(row, 0); var table = Word(row, 16);
                if (names == 0 && table == 0) break;
                if (names == 0 || table == 0) continue;
                for (var offset = 0U; offset < 65536; offset += 4)
                {
                    var declaration = Word(Read(checked(Base + names + offset), 4), 0);
                    if (declaration == 0) break;
                    if ((ulong)Base + table + offset != slot) continue;
                    if ((declaration & 0xffff0000) != 0x80000000) return null;
                    return (Literal(checked(Base + Word(row, 12))) ?? throw new InvalidDataException("Original ordinal import has no source DLL name."), (ushort)declaration);
                }
            }
            return null;
        }
    }
}
