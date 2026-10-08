using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private async Task UiSystemColors(string root, string[] options)
    {
        SubViewport? view = null;
        Texture2D? atlas = null;
        try
        {
            using var selected = OpenUiColorSource(root, options);
            RuntimeLiveContentSource.Configure(root, selected.Game, selected.ContentRoots.Skip(1).ToArray(),
                selected.PluginSources.Select(plugin => plugin.Name).ToArray(), selected.Settings);
            var source = RuntimeLiveContentSource.Current!;
            var settings = FalloutInstallationSettings.Read(source);
            const string dialogPath = "menus/dialog/dialog_menu.xml";
            if (!source.TryRead(dialogPath, null, out var original, out var identity)) throw new FileNotFoundException(dialogPath);
            var hash = SHA256.HashData(original);
            var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read(dialogPath)).Elements("menu").Single();
            var tree = new NativeOwnedMenuTree(menu);
            var speaker = menu.Descendants().Single(tile => (string?)tile.Attribute("name") == "DM_SpeakerNameLabel");
            var actual = tree.TileColor(speaker);
            var expected = IndependentColor(settings, "HUDMain", "uHUDColor");
            if (actual != expected || tree.Color != expected)
                throw new InvalidDataException("The original dialogue tree differs from its independently read owned palette.");
            var pipBoy = tree.TileColor(new XElement("text", new XElement("systemcolor", "entity_Pipboy")));
            if (pipBoy != IndependentColor(settings, "Pipboy", "uPipboyColor"))
                throw new InvalidDataException("Pip-Boy color differs from its independent owned components/override.");
            var font = tree.Font(speaker); atlas = font.Atlas;
            view = new()
            {
                Size = new(640, 128),
                Disable3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            AddChild(view);
            view.AddChild(new ColorRect { Size = view.Size, Color = new(0, 0, 0, 1) });
            view.AddChild(new UiSourceFontPixels(font, actual));
            for (var frame = 0; frame < 4; ++frame)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var pixels = view.GetTexture().GetImage();
            if (pixels.IsEmpty()) throw new InvalidDataException("Owned font color check has no actual native viewport pixels.");
            var colored = 0; var peak = new Color(0, 0, 0, 1);
            for (var y = 0; y < pixels.GetHeight(); ++y)
                for (var x = 0; x < pixels.GetWidth(); ++x)
                {
                    var pixel = pixels.GetPixel(x, y);
                    if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0) ++colored;
                    if (pixel.R + pixel.G + pixel.B > peak.R + peak.G + peak.B) peak = pixel;
                }
            if (actual.R + actual.G + actual.B > 0 && colored == 0 ||
                actual.R + actual.G + actual.B == 0 && colored != 0)
                throw new InvalidDataException("Native source glyphs lost their admitted palette or invented color for an authored black override.");
            if (!source.TryRead(dialogPath, null, out var after, out _) || !SHA256.HashData(after).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Color check changed the winning owned dialogue bytes.");
            GD.Print("OPENNV_OWNED_UI_SYSTEM_COLOR_PASS " + JsonSerializer.Serialize(new
            {
                source.Game,
                source.StackId,
                identity,
                sourceSha256 = Convert.ToHexString(hash),
                font = font.Font.TextureName,
                hudPackedOverride = settings.Contains("Interface", "uHUDColor"),
                pipBoyPackedOverride = settings.Contains("Interface", "uPipboyColor"),
                hud = new[] { actual.R, actual.G, actual.B },
                pipBoy = new[] { pipBoy.R, pipBoy.G, pipBoy.B },
                nativePeak = new[] { peak.R, peak.G, peak.B },
                coloredPixels = colored,
                sourceBytesUnchanged = true,
                recording = false,
                retainedFrames = 0,
                campaignAcceptance = false,
                retailPixelParity = false,
            }));
        }
        finally { view?.Free(); atlas?.Dispose(); RuntimeLiveContentSource.Clear(); }
    }

    private static Color IndependentColor(FalloutInstallationSettings settings, string component, string packedName)
    {
        if (settings.Contains("Interface", packedName))
        {
            var packed = settings.Unsigned("Interface", packedName);
            return new((packed >> 24) / 255f, ((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f);
        }
        return new(settings.Number("Interface", "iSystemColor" + component + "Red") / 255,
            settings.Number("Interface", "iSystemColor" + component + "Green") / 255,
            settings.Number("Interface", "iSystemColor" + component + "Blue") / 255);
    }

    private static RuntimeLiveContentSource OpenUiColorSource(string root, string[] options)
    {
        if (options is ["--mod-stack", var file])
        {
            var selection = JsonSerializer.Deserialize<FalloutModSelection[]>(File.ReadAllText(file))
                ?? throw new InvalidDataException("Mod selection must be an actual launcher selection list.");
            return new FalloutModStackSelection(selection).Resolve(root).OpenSource();
        }
        if (options.Length != 0) throw new ArgumentException("Select an installation with optional --mod-stack.");
        var installation = NativeGameInstallation.Detect(root);
        return RuntimeLiveContentSource.Open(root, installation.Game switch
        {
            NativeGame.FalloutNewVegas => RuntimeLiveContentSource.FalloutNewVegasGame,
            NativeGame.Fallout3 => RuntimeLiveContentSource.Fallout3Game,
            _ => throw new NotSupportedException("Selected installation has no owned Gamebryo UI reader."),
        });
    }
}

internal sealed partial class UiSourceFontPixels(NativeBitmapFontAsset font, Color color) : Control
{
    public override void _Draw() => font.Draw(this, new(16, 16), "OpenNV source glyphs", color);
}
