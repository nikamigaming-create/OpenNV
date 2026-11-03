using System.Numerics;
using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class UiSystemColorContracts
{
    internal static void Run()
    {
        HardPaletteDeclarations();
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
        Reject(() => FalloutExecutableStringTable.ReadHardSystemColors(code, codeBase, Name, HardType));
    }

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException or OverflowException) { return; }
        throw new InvalidDataException("An unowned or malformed palette was admitted.");
    }
}
