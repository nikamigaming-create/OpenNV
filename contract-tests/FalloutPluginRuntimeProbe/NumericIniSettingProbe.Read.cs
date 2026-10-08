using OpenNV.Runtime.Content;

internal static partial class NumericIniSettingProbe
{
    private static void TypedReads()
    {
        var declarations = new FalloutIniDeclaration[]
        {
            new("fValue:Display", FalloutIniCollection.Main, 0x40000000),
            new("fValue:Display", FalloutIniCollection.Prefs, 0x40400000),
            new("fDefault:General", FalloutIniCollection.Main, 0x3dcccccd),
            new("fNegative:General", FalloutIniCollection.Main, 0xbf800000),
            new("fNoSection", FalloutIniCollection.Main, 0x40800000),
            new("iValue:General", FalloutIniCollection.Main, unchecked((uint)-47)),
            new("uValue:General", FalloutIniCollection.Main, uint.MaxValue),
            new("bValue:General", FalloutIniCollection.Main, 1),
            new("fShadow:Display", FalloutIniCollection.Main, 0x41200000),
            new("FShadow:Display", FalloutIniCollection.Prefs, 0),
            new("sValue:General", FalloutIniCollection.Main, 0),
            new("fRenderer:Display", FalloutIniCollection.Renderer, 0x40c00000),
        };
        var layers = new FalloutIniLayer[]
        {
            new("default.ini", FalloutIniCollection.Main, [new("Display", "fValue", "5")]),
            new("prefs.ini", FalloutIniCollection.Prefs, [new("Display", "fValue", "7")]),
            new("custom.ini", FalloutIniCollection.Main,
                [new("Display", "fValue", "9"), new("Display", "fUndeclared", "11")]),
        };
        var owner = new FalloutNumericIniSettings(declarations, layers, "synthetic-executable");
        Require(owner.Float("FVALUE:DISPLAY") == 7 && owner.Read("fValue:Display") == 7 &&
            owner.Get("fValue:Display") == 7 && owner.Float("fDefault:General") == 0.1f &&
            owner.Read("fDefault:General") == (double)0.1f,
            "Strict native reads lost canonical Float32 type/default or preferences precedence.");
        Require(owner.Float("fNegative:General") == -1 && owner.Read("fNegative:General") == -1 &&
            owner.Float("fNoSection") == 4 && owner.Read("iValue:General") == -47 &&
            owner.Read("uValue:General") == uint.MaxValue && owner.Read("bValue:General") == 1,
            "A legitimate negative numeric value, source name or integer payload became a missing result.");
        var profile = new FalloutNumericIniSettings(declarations,
            [.. layers, new("profile", null, [new("Display", "fValue", "0.1")])], "synthetic-executable");
        Require(profile.Float("fValue:Display") == 0.1f &&
            profile.Find(FalloutIniCollection.Prefs, "fValue:Display")!.Origin == "profile" && owner.Float("fValue:Display") == 7,
            "Profile Float32 precedence mutated an independently captured settings owner.");
        foreach (var name in new[] { "fMissing:Display", "fUndeclared:Display", "fRenderer:Display", "fShadow:Display", "sValue:General" })
        {
            Reject(() => owner.Read(name));
            Reject(() => owner.Float(name));
        }
        foreach (var name in new[] { "iValue:General", "uValue:General", "bValue:General" }) Reject(() => owner.Float(name));
        Require(owner.Get("fMissing:Display") == -1 && owner.Get("fShadow:Display") == -1,
            "Strict native reads changed NVSE's failure return or bypassed a nonnumeric preference declaration.");
        Console.WriteLine("OPENNV_NUMERIC_INI_NATIVE_READ_PASS typed=true defaults=true preferencesFirst=true profile=true negativeValueDistinct=true missingRefused=true nonnumericShadowRefused=true rendererSeparate=true capturedOwner=true parity=unverified");
    }
}
