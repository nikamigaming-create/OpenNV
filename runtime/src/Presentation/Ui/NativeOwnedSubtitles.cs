using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

// Background SayTo speech uses HUDMainMenu, independently of activation prompts.
internal sealed partial class NativeOwnedSubtitles : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _branch, _text, _info;
    private readonly FalloutHudSubtitleDeclarations _declaration;
    private readonly FalloutInstallationSettings _settings;
    private readonly Func<FalloutSpeechSubtitle?> _speech;
    private readonly Func<bool> _shown;
    private readonly FalloutUiComponentStore? _scriptUi;
    private long _uiRevision = -1;
    private FalloutSpeechSubtitle? _displayed;
    private NativeViewportLayout? _viewportLayout;
    internal string? Error { get; private set; }
    internal object State => new
    {
        visible = Visible,
        subtitle = _displayed,
        generalEnabled = _settings.Boolean("GamePlay", "bGeneralSubtitles"),
        error = Error,
        source = "owned-HUDMainMenu-Subtitles-template-font-placement",
        unbound = "text-style,queue-hold-fade,matched-timing-and-canvas-scale,XR-final-eye"
    };

    internal NativeOwnedSubtitles(FalloutPluginStack records, Func<FalloutSpeechSubtitle?> speech, Func<bool> shown,
        FalloutUiComponentStore? scriptUi = null)
    {
        Name = "HUDMainMenuSubtitles"; MouseFilter = MouseFilterEnum.Ignore; ProcessMode = ProcessModeEnum.Always;
        _speech = speech; _shown = shown; _scriptUi = scriptUi; Visible = false;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned subtitle source is absent.");
        _settings = FalloutInstallationSettings.Read(source);
        _declaration = FalloutExecutableStringTable.ReadHudSubtitleDeclarations(
            Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe"));
        var sourceMenu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/main/hud_main_menu.xml")).Elements("menu").Single();
        var menu = new XElement(sourceMenu.Name, sourceMenu.Attributes(),
            sourceMenu.Elements().Where(element => element.Attribute("name") is null).Select(element => new XElement(element)));
        _branch = new XElement(sourceMenu.Elements().Single(element => (string?)element.Attribute("name") == "Subtitles"));
        _info = new XElement(sourceMenu.Elements().Single(element => (string?)element.Attribute("name") == "Info"));
        // Its dimensions remain source inputs; activation children have a separate HUD owner.
        foreach (var child in _info.Elements().Where(element => element.Attribute("name") is not null).ToArray()) child.Remove();
        _text = new XElement(sourceMenu.Elements("template").Single(element =>
            (string?)element.Attribute("name") == _declaration.TextTemplate).Elements().Single());
        _branch.Add(_text); menu.Add(_info, _branch);
        _tiles = new(menu, setting => FalloutGameSettingStrings.Read(records, setting), scriptUi);
        _tiles.Bind(menu, "visible", 1); _tiles.Bind(_info, "visible", 0);
        _tiles.Bind(_branch, "locus", 1); _tiles.Bind(_branch, "visible", 1);
        _tiles.Bind(_text, "visible", 1); _tiles.Bind(_text, "alpha", 255);
    }

    internal void Prepare(FalloutSpeechSubtitle subtitle)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        if (!Admitted(subtitle)) return;
        _tiles.Text[_text] = subtitle.Text;
        _tiles.ValidateDrawing();
    }
    private bool Admitted(FalloutSpeechSubtitle? subtitle) => subtitle is not null &&
        (subtitle.Forced || _settings.Boolean("GamePlay", "bGeneralSubtitles"));
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    public override void _Ready() => Layout();
    private void Layout()
    {
        var size = GetViewportRect().Size;
        var scale = size.Y / 960;
        Scale = Vector2.One * scale; Size = _tiles.Screen = size / scale; _tiles.ResolutionConverter = 1 / scale;
        var wide = size.X / size.Y >= 16f / 9;
        var width = _tiles.Number(_branch, "width");
        var at = _declaration.Place(Size.X, Size.Y,
            _settings.Number("Interface", wide ? "iSafeZoneYWide" : "iSafeZoneY"), _tiles.Number(_info, "height"),
            width, _tiles.Number(_branch, "height"));
        _tiles.Bind(_branch, "x", at.X); _tiles.Bind(_branch, "y", at.Y);
        _tiles.Bind(_text, "x", at.TextX); _tiles.Bind(_text, "wrapwidth", width);
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            var next = _speech();
            Visible = _shown() && Admitted(next);
            if (_scriptUi is not null && _uiRevision != _scriptUi.Revision)
            {
                _uiRevision = _scriptUi.Revision; Layout();
                if (next is not null) Prepare(next);
            }
            if (next == _displayed) return;
            if (next is not null) Prepare(next);
            _displayed = next; _tiles.Text[_text] = next?.Text ?? "";
            QueueRedraw();
        }
        catch (Exception error)
        {
            Error = error.Message; Visible = false;
            GD.PushError($"OPENNV_SUBTITLE_DIVERGENCE {Error}");
        }
    }
    public override void _Draw() => _tiles.Draw(this);
}
