using OpenNV.Runtime.Content;

internal static partial class NumericIniSettingProbe
{
    internal static void Run()
    {
        Layers(); Decoder(); TypedReads();
        Console.WriteLine("OPENNV_NUMERIC_INI_PASS typed=true sourceLayers=true nonnumericShadow=true rendererSeparate=true relocatedAssociations=true malformedRefused=true parity=unverified");
    }

    private static void Layers()
    {
        var declarations = new FalloutIniDeclaration[]
        {
            new("bBoolean:General", FalloutIniCollection.Main, 1),
            new("iSigned:General", FalloutIniCollection.Main, unchecked((uint)-123)),
            new("uUnsigned:General", FalloutIniCollection.Main, uint.MaxValue),
            new("fFloat:Display", FalloutIniCollection.Main, 0x3dcccccd),
            new("fOverride:Display", FalloutIniCollection.Main, 0x3f800000),
            new("fShared:Display", FalloutIniCollection.Main, 0x40000000),
            new("fShared:Display", FalloutIniCollection.Prefs, 0x40400000),
            new("sShadow:General", FalloutIniCollection.Main, 7),
            new("sShadow:General", FalloutIniCollection.Prefs, 9),
            new("fShadow:General", FalloutIniCollection.Main, 0x41200000),
            new("FShadow:General", FalloutIniCollection.Prefs, 9),
            new("rColour:Menu", FalloutIniCollection.Main, 0xffabcdef),
            new("fRenderer:Display", FalloutIniCollection.Renderer, 0x41200000),
            new("fNoSection", FalloutIniCollection.Main, 0x40800000),
        };
        var values = new List<FalloutInstallationSetting> { new("Display", "fOverride", "71.25"), new("Display", "fUndeclared", "50") };
        var layers = new FalloutIniLayer[]
        {
            new("default.ini", FalloutIniCollection.Main, [new("Display", "fOverride", "30")]),
            new("prefs.ini", FalloutIniCollection.Prefs, [new("Display", "fShared", "40")]),
            new("profile", null, values),
        };
        var owner = new FalloutNumericIniSettings(declarations, layers, "synthetic-executable");
        values.Clear();
        var installation = FalloutInstallationSettings.ReadIniLayers(() => declarations, layers, "synthetic-executable");
        Require(installation.Unsigned("General", "uUnsigned") == uint.MaxValue,
            "Unsigned installation default lost canonical bits or required a file override.");
        Reject(() => installation.Unsigned("General", "iSigned"));
        Reject(() => installation.Unsigned("General", "uMissing"));
        Require(owner.Get("BBOOLEAN:GENERAL") == 1 && owner.Get("ISIGNED:GENERAL") == -123 &&
            owner.Get("UUNSIGNED:GENERAL") == uint.MaxValue && owner.Get("fFloat:Display") == (double)0.1f,
            "Canonical types or Float32 widening changed with caller case.");
        Require(owner.Get("fOverride:Display") == 71.25 && owner.Get("fShared:Display") == 40 &&
            owner.Find(FalloutIniCollection.Main, "fShared:Display")!.Number == 2 &&
            owner.Find(FalloutIniCollection.Main, "fOverride:Display")!.Origin == "profile" &&
            owner.Layers[^1].Values.Count == 2, "Layer provenance, capture or collection precedence was lost.");
        Require(owner.Get("sShadow:General") == -1 && owner.Get("fShadow:General") == -1 && owner.Get("rColour:Menu") == -1 &&
            owner.Find(FalloutIniCollection.Prefs, "sShadow:General")!.Declaration.Payload == 9 &&
            owner.Get("fRenderer:Display") == -1 && owner.Get("fUndeclared:Display") == -1 &&
            owner.Get("") == -1 && owner.Get("fNoSection") == 4,
            "Nonnumeric, renderer-only or undeclared settings became numeric; lookup invented a section grammar.");
        Reject(() => new FalloutNumericIniSettings([declarations[0], declarations[0] with { Name = "BBOOLEAN:GENERAL" }], [], "fixture"));
        Reject(() => new FalloutNumericIniSettings([new("bInvalid:General", FalloutIniCollection.Main, 2)], [], "fixture"));
        Reject(() => new FalloutNumericIniSettings([new("fInvalid:General", FalloutIniCollection.Main, 0x7f800000)], [], "fixture"));
        Reject(() => new FalloutNumericIniSettings(declarations,
            [new("invalid", null, [new("Display", "fFloat", "NaN")])], "fixture"));
        Reject(() => new FalloutNumericIniSettings(declarations,
            [new("invalid", null, [new("General", "uUnsigned", "-1")])], "fixture"));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException or OverflowException) { return; }
        throw new InvalidDataException("Malformed INI source was accepted.");
    }
}
