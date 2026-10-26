using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class HudControlTokenProbe
{
    internal static void Run()
    {
        var controls = new FalloutInputControls(_ => null, defaults: _ => new(255, 255));
        controls.Set(0, 17); controls.Set(1, 31); controls.Set(2, 30); controls.Set(3, 32); controls.Set(27, 44);
        string Label(uint control)
        {
            var code = controls.Get(control);
            return code == -1 ? "mouse=" + controls.Get(control, 1) : "key=" + code;
        }
        const string source = "Move &-sUActnForward;&-sUActnSldleft;&-sUActnBack;&-sUActnSldright;. Object: &sUActnGrab;.";
        var rendered = FalloutMessageControlTokens.Resolve(source, Label);
        Require(rendered == "Move [key=17][key=30][key=31][key=32]. Object: [key=44].", "Adjacent control slots lost their order or surrounding source text.");
        var revision = controls.Revision;
        controls.Set(0, 18);
        Require(controls.Revision > revision && FalloutMessageControlTokens.Resolve(source, Label).StartsWith("Move [key=18]", StringComparison.Ordinal) &&
            source.Contains("&-sUActnForward;", StringComparison.Ordinal), "Rebinding retained a default or changed the source message.");
        controls.Set(27, 255); controls.Set(27, 258, 1);
        Require(FalloutMessageControlTokens.Resolve("&sUActnGrab;", Label) == "[mouse=258]", "Mouse-only control did not retain its mapped lane.");
        Require(FalloutMessageControlTokens.Resolve("literal & punctuation", _ => throw new Exception("Literal asked for a binding.")) == "literal & punctuation",
            "Unrelated source text was changed.");
        Reject(() => FalloutMessageControlTokens.Resolve("&sUActnUnsupported;", Label));
        Reject(() => FalloutMessageControlTokens.Resolve("&sUActnGrab", Label));
        Reject(() => FalloutMessageControlTokens.Resolve("&sUActnGrab;", _ => ""));
        Console.WriteLine("OPENNV_HUD_CONTROL_TOKEN_CONTRACT_PASS adjacent=true sourceProse=true rebind=true mouseLane=true sourceUnchanged=true unknownRefused=true glyphParity=unverified");
    }

    internal static void Owned(string root)
    {
        using var source = RuntimeLiveContentSource.Open(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var settings = FalloutInstallationSettings.Read(source);
        var controls = new FalloutInputControls(name => settings.Contains("Controls", name) ? settings.Require("Controls", name) : null,
            defaults: name => { var value = settings.ControlDefault(name); return new(value.Keyboard, value.Mouse); });
        var checkedMessages = 0; var checkedBindings = 0;
        var touchedControls = new HashSet<uint>();
        foreach (var record in records.EffectiveRecords("MESG"))
        {
            var fields = record.ReadSubrecords().ToArray();
            var identity = fields.SingleOrDefault(field => field.Signature == "EDID");
            if (identity.Signature != "EDID" || !FalloutDialogueTopic.Text(identity.Data.Span).Contains("Tutorial", StringComparison.OrdinalIgnoreCase)) continue;
            var description = fields.SingleOrDefault(field => field.Signature == "DESC");
            if (description.Signature != "DESC") continue;
            var text = FalloutDialogueTopic.Text(description.Data.Span);
            if (!text.Contains("sUActn", StringComparison.OrdinalIgnoreCase)) continue;
            var hash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
            var rendered = FalloutMessageControlTokens.Resolve(text, control =>
            {
                touchedControls.Add(control); checkedBindings++;
                var code = controls.Get(control);
                if (code == -1) code = controls.Get(control, 1);
                if (code == -1) throw new NotSupportedException($"Owned tutorial {record.FormKey} control {control} is unbound.");
                return code.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            Require(rendered != text && !rendered.Contains("sUActn", StringComparison.OrdinalIgnoreCase) &&
                hash == Convert.ToHexString(SHA256.HashData(record.ReadData())), "Owned tutorial retained a token or changed source bytes.");
            checkedMessages++;
        }
        Require(checkedMessages > 0 && touchedControls.Contains(0) && touchedControls.Contains(27), "Owned tutorial audit omitted movement or object handling.");
        Console.WriteLine($"OPENNV_OWNED_HUD_CONTROL_TOKEN_PASS messages={checkedMessages} bindings={checkedBindings} controls={touchedControls.Count} " +
            "winningMessages=true installedBindings=true sourceUnchanged=true nativeLabels=separate glyphParity=unverified");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unsupported HUD control token was admitted.");
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
}
