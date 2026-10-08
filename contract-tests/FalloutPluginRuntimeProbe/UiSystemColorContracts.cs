using System.Numerics;
using OpenNV.Runtime.Content;

internal static class UiSystemColorContracts
{
    internal static void Run()
    {
        static FalloutInstallationSettings Source(params FalloutInstallationSetting[] rows) =>
            FalloutInstallationSettings.ReadLayers([], rows);
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

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException or OverflowException) { return; }
        throw new InvalidDataException("An unowned or malformed palette was admitted.");
    }
}
