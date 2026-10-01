using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

// LSCR images and LSCT tips are independent source components. LoadingMenu's
// ancillary animation/progress widgets remain visible coverage gaps.
internal sealed partial class NativeOwnedLoadingScreens : Control
{
    private readonly NativeGamebryoLoadingBackground _background;
    private readonly Tip[] _tips;
    private readonly IReadOnlyList<FalloutLoadingScreen> _candidates;
    internal object State => new
    {
        candidates = _candidates.Select(screen => screen.Identity.ToString()).ToArray(),
        represented = "winning-LSCR-images-and-LSCT-tips",
        unbound = new[] { "LoadingMenu ancillary NIF", "progress/statistics widgets", "matched selection/fade/layout timing" },
        parity = "unverified"
    };

    internal NativeOwnedLoadingScreens(FalloutPluginStack records, FalloutCellDefinition cell, bool locationSpecificOnly)
    {
        Name = "OwnedLoadingScreens";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        try
        {
            _candidates = FalloutLoadingScreenCatalog.InGame(records, cell, locationSpecificOnly);
            foreach (var type in _candidates.Select(screen => screen.Type).OfType<FalloutFormKey>().Distinct())
                _ = FalloutLoadingScreenType.Tip(records.GetEffective(type));
            var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
            var menu = FalloutMenuXml.Read("menus/loading_menu.xml");
            var initialPath = menu.Descendants().Single(tile => (string?)tile.Attribute("name") == "loading_tile_slide_01")
                .Element("filename")?.Value.Trim() ?? throw new InvalidDataException("LoadingMenu has no initial owned slide.");
            _background = new(settings, NativeOwnedMediaLoader.LoadTexture("textures/" + initialPath), mainMenu: false);
            _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_background);
            _tips = [new Tip(), new Tip()];
            foreach (var tip in _tips) AddChild(tip);
            _background.ScreenChanged += (slot, screen) => _tips[slot].Select(records, settings, screen);
            _background.SetCatalog(_candidates);
            SetMeta("opennv_loading_destination", cell.FormKey.ToString());
            SetMeta("opennv_loading_location_specific_only", locationSpecificOnly);
            SetMeta("opennv_loading_ui_coverage", "partial: LSCR image/LSCT tip; ancillary LoadingMenu widgets unbound");
        }
        catch { Free(); throw; }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        for (var slot = 0; slot < _tips.Length; slot++)
        {
            _tips[slot].Scale = Vector2.One * GetViewportRect().Size.Y / 960;
            _tips[slot].Modulate = new Color(1, 1, 1, _background.SlideOpacity(slot));
        }
    }

    private sealed partial class Tip : Control
    {
        private FalloutLoadingTip? _layout;
        private NativeBitmapFontAsset? _font;
        private string[] _lines = [];
        private readonly Dictionary<int, NativeBitmapFontAsset> _fonts = [];
        public override void _Notification(int what)
        {
            if (what == NotificationPredelete)
            {
                foreach (var font in _fonts.Values) font.Atlas.Dispose();
                _fonts.Clear();
            }
        }
        internal void Select(FalloutPluginStack records, FalloutInstallationSettings settings, FalloutLoadingScreen screen)
        {
            _layout = screen.Type is { } type ? FalloutLoadingScreenType.Tip(records.GetEffective(type)) : null;
            _lines = [];
            if (_layout is { } layout)
            {
                if (!_fonts.TryGetValue(layout.Font, out _font)) _fonts.Add(layout.Font, _font = NativeBitmapFontAsset.Read(settings, layout.Font));
                var lines = new List<string>();
                foreach (var paragraph in screen.Description.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
                {
                    var line = "";
                    foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var next = line.Length == 0 ? word : line + " " + word;
                        if (line.Length != 0 && _font.Font.Measure(next) > layout.Width) { lines.Add(line); line = word; }
                        else line = next;
                    }
                    lines.Add(line);
                }
                _lines = lines.ToArray();
            }
            QueueRedraw();
        }
        public override void _Draw()
        {
            if (_layout is not { } layout || _font is null) return;
            var clip = new Rect2(layout.X, layout.Y, layout.Width, layout.Height);
            var color = new Color(layout.Red / 255, layout.Green / 255, layout.Blue / 255);
            var y = (float)layout.Y;
            foreach (var line in _lines)
            {
                var width = _font.Font.Measure(line);
                var x = layout.X + (layout.Alignment switch { 1 => 0, 2 => (layout.Width - width) / 2, 4 => layout.Width - width, _ => throw new InvalidDataException("Loading tip alignment is invalid.") });
                _font.Draw(this, new(x, y), line, color, _font.Font.TileBaseline, clip);
                y += _font.Font.Height;
            }
        }
    }
}
