using System.Numerics;
using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class UiSystemColorContracts
{
    internal static void Run()
    {
        HardPaletteDeclarations();
        InlineHardPaletteDeclarations();
        static FalloutInstallationSettings Source(params FalloutInstallationSetting[] rows) =>
            FalloutInstallationSettings.ReadLayers([], rows, new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            { ["HUDAlt"] = 0xff432aff, ["MainMenu"] = 0xffb642ff, ["AuthoredPalette"] = 0x020304ff });
        static FalloutInstallationSetting Setting(string key, string value) => new("Interface", key, value);
        static Vector3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
        var components = new[] { Setting("iSystemColorHUDMainRed", "23"), Setting("iSystemColorHUDMainGreen", "177"),
            Setting("iSystemColorHUDMainBlue", "92") };
        Require(FalloutUiSystemColors.Read(Source(components), "entity_HUDMain") == Rgb(23, 177, 92),
            "Absent packed storage lost the complete authored component palette.");
        Require(FalloutUiSystemColors.Read(Source([.. components, Setting("uHUDColor", "287454020")]), "entity_hudmain") == Rgb(17, 34, 51),
            "An explicit packed override lost exact channels or caller case.");
        Require(FalloutUiSystemColors.Read(Source([.. components, Setting("uHUDColor", "0")]), "entity_HUDMain") == Vector3.Zero,
            "An explicit authored black override was replaced with a guessed visible color.");
        Require(FalloutUiSystemColors.Read(Source(Setting("iSystemColorPipboyRed", "111"),
            Setting("iSystemColorPipboyGreen", "17"), Setting("iSystemColorPipboyBlue", "233")), "entity_Pipboy") == Rgb(111, 17, 233),
            "Pip-Boy default components were mistaken for uninitialized packed storage.");
        Require(FalloutUiSystemColors.Read(Source(Setting("uPipboyColor", "2864434397")), "entity_Pipboy") == Rgb(170, 187, 204),
            "Pip-Boy override was replaced by HUD or default values.");
        Require(FalloutUiSystemColors.Read(Source(Setting("iSystemColorAuthoredPaletteRed", "2"),
            Setting("iSystemColorAuthoredPaletteGreen", "3"), Setting("iSystemColorAuthoredPaletteBlue", "4")),
            "entity_AuthoredPalette") == Rgb(2, 3, 4), "A declared source palette required a named-game or fixed palette whitelist.");
        Require(FalloutUiSystemColors.Read(Source(Setting("iSystemColorAuthoredPaletteRed", "NaN")), "entity_AuthoredPalette") == Rgb(2, 3, 4),
            "An unrelated obsolete component row corrupted a source hard palette.");
        Require(FalloutUiSystemColors.Read(Source(Setting("iSystemColorHUDAltRed", "255"),
            Setting("iSystemColorHUDAltGreen", "182"), Setting("iSystemColorHUDAltBlue", "0")), "entity_HUDAlt") == Rgb(255, 67, 42),
            "Obsolete component rows replaced the actual hard hostile palette with amber.");
        Require(FalloutUiSystemColors.Read(Source(Setting("uHUDColor", "287454020")), "entity_MainMenu") == Rgb(255, 182, 66),
            "A hard palette entry incorrectly inherited a soft HUD override.");
        Require(FalloutUiSystemColors.Read(Source(), "entity_NoSystemColor") == Vector3.One,
            "No-system-color altered the untinted canvas contract.");
        Reject(() => FalloutUiSystemColors.Read(Source(components[..2]), "entity_HUDMain"));
        foreach (var value in new[] { "-1", "256", "1.5", "NaN", "Infinity" })
            Reject(() => FalloutUiSystemColors.Read(Source([.. components[..2], Setting("iSystemColorHUDMainBlue", value)]), "entity_HUDMain"));
        Reject(() => FalloutUiSystemColors.Read(Source([.. components, Setting("uHUDColor", "4294967296")]), "entity_HUDMain"));
        Reject(() => FalloutUiSystemColors.Read(Source([.. components, Setting("uHUDColor", "invalid")]), "entity_HUDMain"));
        Reject(() => FalloutUiSystemColors.Read(Source(), "entity_UnownedPalette"));
        Reject(() => FalloutUiSystemColors.Read(Source(components), "HUDMain"));
        Reject(() => FalloutUiSystemColors.Read(Source(components), "entity_"));
        Console.WriteLine("OPENNV_UI_SYSTEM_COLOR_CONTRACT_PASS sourceComponents=true explicitPacked=true authoredBlack=true missingMalformedRefused=true gameBranches=false");
    }

    private static void HardPaletteDeclarations()
    {
        var code = new byte[640];
        const uint codeBase = 0x1000;
        var cursor = 0;
        void Push(uint value)
        {
            code[cursor++] = 0x68;
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(cursor), value); cursor += 4;
        }
        void Call(int at, int target)
        {
            code[at] = 0xe8;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(at + 1), target - at - 5);
        }
        Push(2); Push(0x33); Push(0x22); Push(0x11); Push(0x2000);
        new byte[] { 0x8b, 0x4d, 0xf0 }.CopyTo(code, cursor); cursor += 3;
        var registrationCall = cursor; Call(cursor, 256); cursor += 5;
        new byte[] { 0x55, 0x8b, 0xec }.CopyTo(code, 256);
        new byte[] { 0x8b, 0x4d, 0x18, 0x51, 0x68, 0xff, 0, 0, 0,
            0x8b, 0x55, 0x14, 0x52, 0x8b, 0x45, 0x10, 0x50, 0x8b, 0x4d, 0x0c, 0x51 }.CopyTo(code, 316);
        Call(337, 368);
        new byte[] { 0x83, 0xc4, 0x10, 0x50, 0x8b, 0x55, 0x08, 0x52, 0x8b, 0x4d, 0xe8 }.CopyTo(code, 342);
        Call(353, 464);
        new byte[] { 0x8b, 0x45, 0x08, 0xc1, 0xe0, 0x18 }.CopyTo(code, 372);
        new byte[] { 0x8b, 0x4d, 0x0c, 0x81, 0xe1, 0xff, 0, 0, 0, 0xc1, 0xe1, 0x10 }.CopyTo(code, 381);
        new byte[] { 0x8b, 0x55, 0x10, 0x81, 0xe2, 0xff, 0, 0, 0, 0xc1, 0xe2, 0x08 }.CopyTo(code, 399);
        new byte[] { 0x8b, 0x45, 0x14, 0x25, 0xff, 0, 0, 0 }.CopyTo(code, 417);
        new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc }.CopyTo(code, 464);
        new byte[] { 0x8b, 0x55, 0xfc, 0xc7, 0x02 }.CopyTo(code, 487);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(492), 0x3000);
        new byte[] { 0x8b, 0x45, 0xfc, 0x8b, 0x4d, 0x0c, 0x89, 0x48, 0x0c, 0x8b, 0x45, 0xfc }.CopyTo(code, 496);
        string? Name(uint pointer) => pointer == 0x2000 ? "AuthoredHard" : null;
        string? HardType(uint pointer) => pointer == 0x3000 ? ".?AVHardSystemColor@SystemColorManager@@" : null;
        Require(FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, HardType)["authoredhard"] == 0x112233ff,
            "Source hard palette lost RGBA order, registration or source name.");
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(6), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(11), 0x122);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(16), 0x1234);
        Require(FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, HardType)["AuthoredHard"] == 0x3422ffff,
            "Source RGBA packing lost low-byte or signed immediate behavior.");
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(6), 0x33);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(11), 0x22);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(16), 0x11);
        code[410] = 7;
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, HardType));
        code[410] = 8;
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name,
            _ => ".?AVSoftSystemColor@SystemColorManager@@"));
        Array.Copy(code, 0, code, 96, cursor); Call(96 + registrationCall, 256);
        Require(FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, HardType).Count == 1,
            "Equivalent repeated hard-palette construction was treated as contradictory source data.");
        code[97] = 3;
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, HardType));
    }

    private static void InlineHardPaletteDeclarations()
    {
        // A synthetic optimized constructor: independent registers, names,
        // slots and relocations exercise operand relationships rather than a
        // captured executable body or a game's palette catalog.
        var code = new byte[1024];
        const uint codeBase = 0x5000;
        const int factory = 256, red = 2, green = 1, blue = 0, owner = 3;
        var at = 0;
        void Emit(params byte[] bytes) { bytes.CopyTo(code, at); at += bytes.Length; }
        void Word(uint value) { BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value); at += 4; }
        void Push(uint value) { Emit(0x68); Word(value); }
        void Call(int target) { Emit(0xe8); Word(unchecked((uint)(target - at - 4))); }
        void Argument(int register, byte offset, bool byteValue = false)
        {
            if (byteValue) Emit(0x0f, 0xb6); else Emit(0x8b);
            Emit((byte)(0x45 | register << 3), offset);
        }
        void Memory(byte opcode, int register, int basis, byte offset, bool extended = false)
        {
            if (extended) Emit(0x0f);
            Emit(opcode, (byte)(0x40 | register << 3 | basis), offset);
        }
        void Constant(int basis, byte offset, uint value)
        {
            Emit(0xc7, (byte)(basis | (offset == 0 ? 0 : 0x40)));
            if (offset != 0) Emit(offset);
            Word(value);
        }
        void Shift(int register) => Emit(0xc1, (byte)(0xe0 + register), 8);
        void Binary(byte opcode, int destination, int source) => Emit(opcode, (byte)(0xc0 | destination << 3 | source));
        void Registration(byte receiver)
        {
            Emit(0x6a, 0xff); Push(0x1ff); Push(0x122); Push(0x134); Push(0x2000);
            Emit(0x8b, receiver); Call(factory);
        }
        Registration(0xce);
        var firstLength = at;
        at = 64; Registration(0xcf);
        at = factory; Emit(0x55, 0x8b, 0xec);
        at += 24;
        Argument(red, 12); Argument(green, 16, true); Argument(blue, 20, true);
        var shift = at; Shift(red); Binary(0x0b, green, red); Constant(owner, 0, 0x3000);
        Shift(green); Binary(0x33, red, red); Binary(0x0b, green, blue); Constant(owner, 4, 0);
        Shift(green); Memory(0x8d, blue, owner, 4);
        var alpha = at + 2; Emit(0x81, (byte)(0xc8 + green)); Word(255);
        Memory(0x89, red, blue, 4); Memory(0x89, green, 5, 24); Memory(0xff, 6, 5, 8);
        Emit(0xc6, 0x45, 0xf8, 1); Push(0x2020); Emit((byte)(0x50 + blue)); var nameCall = at; Call(800);
        Memory(0x8b, red, owner, 4); Emit(0x83, 0xc4, 12); Binary(0x85, red, red);
        Emit(0x74, 18); Memory(0xb7, blue, owner, 8, true);
        Emit((byte)(0x40 + blue), (byte)(0x50 + blue), (byte)(0x50 + red)); var internCall = at; Call(800);
        Memory(0x8b, red, owner, 4); Emit(0x83, 0xc4, 8);
        Emit(0x56, (byte)(0x50 + red)); var slotCall = at; Call(800);
        var packedLoad = at; Argument(blue, 24); Emit(0x83, 0xc4, 8); Constant(owner, 0, 0x4000);
        var store = at; Memory(0x89, blue, owner, 12);
        Emit(0x8b, 0xe5, 0x5d, 0xc2, 20, 0);
        var end = at;
        string? Name(uint value) => value switch { 0x2000 => "SyntheticHard", 0x2020 => "&%s;", _ => null };
        string? Type(uint value) => value switch
        {
            0x3000 => ".?AVSystemColor@SystemColorManager@@",
            0x4000 => ".?AVHardSystemColor@SystemColorManager@@",
            _ => null,
        };
        IReadOnlyDictionary<string, uint> Read() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, Type);
        Require(Read().Count == 1 && Read()["synthetichard"] == 0x3422ffff,
            "Inline channel packing, byte truncation, register receivers or equivalent declarations changed the owned palette.");
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(3), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(67), uint.MaxValue);
        Require(Read()["SyntheticHard"] == 0x3422ffff, "A negative channel changed optimized byte packing.");
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(3), 0x1ff);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(67), 0x1ff);
        var earlyReturn = factory + 8;
        new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 }.CopyTo(code, earlyReturn);
        Reject(() => Read());
        new byte[] { 0x8b, 0xe5, 0x5d, 0xc2, 16, 0 }.CopyTo(code, earlyReturn);
        Reject(() => Read());
        Array.Clear(code, earlyReturn, 6);
        code[shift + 2] = 7; Reject(() => Read()); code[shift + 2] = 8;
        code[alpha] = 254; Reject(() => Read()); code[alpha] = 255;
        code[store + 2] = 8; Reject(() => Read()); code[store + 2] = 12;
        foreach (var alias in new[] { owner, 4, 5 })
        {
            code[packedLoad + 1] = (byte)(0x45 | alias << 3);
            code[store + 1] = (byte)(0x40 | alias << 3 | owner);
            Reject(() => Read());
        }
        code[packedLoad + 1] = (byte)(0x45 | blue << 3);
        code[store + 1] = (byte)(0x40 | blue << 3 | owner);
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code[..(end - 1)], codeBase, Name, Type));
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name,
            pointer => pointer == 0x4000 ? ".?AVSoftSystemColor@SystemColorManager@@" : Type(pointer)));
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase,
            pointer => pointer == 0x2020 ? "%s%s" : Name(pointer), Type));
        code[65] = 1; Reject(() => Read()); code[65] = 255;
        code[67] = 0; Reject(() => Read()); code[67] = 255;
        // An otherwise identical second constructor is still another owner.
        Array.Copy(code, factory, code, 600, end - factory);
        foreach (var call in new[] { nameCall, internCall, slotCall }) { at = call + 600 - factory; Call(800); }
        at = 64 + firstLength - 5; Call(600);
        Reject(() => Read());
    }

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException or OverflowException) { return; }
        throw new InvalidDataException("An unowned or malformed palette was admitted.");
    }
}
