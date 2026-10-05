using Godot;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeInteractionUiAudit
{
    private async Task OwnedTerminalMenu(string[] arguments)
    {
        if (arguments.Length < 6)
            throw new ArgumentException("Expected game root, mod ID/root, reference plugin/hex ID and dependency roots.");
        var game = arguments[1];
        var installation = new FalloutModStackSelection([new(arguments[2], arguments[3], arguments[6..])]).Resolve(game);
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var colorTile = new System.Xml.Linq.XElement("text", new System.Xml.Linq.XAttribute("name", "ColorFixture"),
            new System.Xml.Linq.XElement("brightness", "entity_uselocalcolor"),
            new System.Xml.Linq.XElement("red", 31), new System.Xml.Linq.XElement("green", 67),
            new System.Xml.Linq.XElement("blue", 103), new System.Xml.Linq.XElement("alpha", 127));
        var colors = new NativeOwnedMenuTree(colorTile);
        if (colors.DrawingColor(colorTile) != new Color(31 / 255f, 67 / 255f, 103 / 255f, 127 / 255f))
            throw new InvalidDataException("Local RGB shortcut incorrectly used the system tint or negative brightness.");
        var reference = new FalloutFormKey(arguments[4], Convert.ToUInt32(arguments[5], 16));
        var placed = records.GetEffective(reference);
        var terminal = FalloutTerminal.Read(records, FalloutDialogueTopic.RequiredForm(placed, "NAME"));
        static string ReferenceHash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
        var sourceHash = ReferenceHash(placed);
        var selections = 0;
        var closed = false;
        Exception? presentationFailure = null;
        void RequireReference(FalloutFormKey caller)
        {
            if (caller != reference || records.GetEffective(caller).Signature != "REFR" ||
                ReferenceHash(records.GetEffective(caller)) != sourceHash)
                throw new InvalidDataException("Source UI fixture replaced its placed reference.");
        }
        // This component fixture has no world, access, animation or quest owner.
        // Conditional rows stay visibly blocked; no gameplay predicate or source
        // effect is manufactured to obtain a renderable terminal menu.
        var session = new FalloutTerminalMenu(records, reference, RequireReference,
            (_, _) => throw new NotSupportedException("Source UI fixture has no gameplay condition owner."),
            _ => { selections++; throw new InvalidDataException("Source UI fixture cannot execute a result."); });
        var menu = new NativeOwnedComputersMenu(records, session,
            () => { session.Close(); closed = true; }, error => presentationFailure = error);
        AddChild(menu);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (presentationFailure is not null) throw presentationFailure;
            if (menu.Error is not null) throw new InvalidDataException(menu.Error);
            var buttons = menu.GetChildren().OfType<NativeBitmapMenuButton>().ToArray();
            if (buttons.Length <= 1 || buttons.Any(button => button.Size.X <= 0 || button.Size.Y <= 0))
                throw new InvalidDataException("Source terminal controls have no native extent.");
            if (session.VisibleEntries.Any(entry => entry.Entry.Conditions.Count > 0 && entry.Selectable))
                throw new InvalidDataException("A conditional source row was admitted without gameplay authority.");
            var back = buttons.Single(button => button.Text == FalloutGameSettingStrings.Read(records, "sComputersBack"));
            back.EmitSignal(BaseButton.SignalName.Pressed);
            if (!closed || session.Active || session.LastReceipt is not null || selections != 0)
                throw new InvalidDataException("Terminal close applied a source result or lost its menu lifetime.");
            session.RequireSaveable();
            if (ReferenceHash(records.GetEffective(reference)) != sourceHash ||
                FalloutTerminal.Read(records, terminal.Record.FormKey).SourceHash != terminal.SourceHash)
                throw new InvalidDataException("Native terminal UI modified its owned source.");
            GD.Print($"OPENNV_NATIVE_TERMINAL_UI_PASS reference={reference} ownerMvid={typeof(NativeOwnedComputersMenu).Module.ModuleVersionId} " +
                $"sourceControls={buttons.Length} sourceXmlFonts=true sourceUnchanged=true " +
                "conditionalRows=blocked gameplayAccessAndEffects=unverified pixelsAndTiming=unverified recording=false");
        }
        finally { menu.Free(); }
    }
}
