using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeHudTarget(string Action, string Name, string? Lock);
internal sealed record NativeHudAmmo(int Loaded, int Reserve);

internal partial class NativeOwnedGameplayHud : Control
{
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _reticle, _info, _prompt, _name;
    private readonly Func<NativeHudTarget?> _target;
    private readonly Func<bool> _shown;
    private readonly Func<GameplayVitals>? _vitals;
    private readonly XElement? _hp, _ap, _hpMeter, _apMeter;
    private GameplayVitals? _lastVitals;
    private readonly Func<NativeHudAmmo?>? _ammunition;
    private readonly Func<string?>? _notice;
    private readonly XElement? _ammo, _warning;
    private NativeHudAmmo? _lastAmmo;
    private string? _lastNotice;
    private NativeHudTarget? _last;
    internal string? Error { get; private set; }
    internal object State => new
    {
        visible = Visible,
        target = _last,
        vitals = _lastVitals,
        ammo = _lastAmmo,
        notice = _lastNotice,
        error = Error,
        source = "HUDMainMenu/ReticleCenter/Info/HitPoints/ActionPoints"
    };

    internal NativeOwnedGameplayHud(FalloutPluginStack records, string activateKey, Func<NativeHudTarget?> target, Func<bool> shown,
        Func<GameplayVitals>? vitals = null, Func<NativeHudAmmo?>? ammunition = null, Func<string?>? notice = null)
    {
        Name = "HUDMainMenuGameplay"; MouseFilter = MouseFilterEnum.Ignore; ProcessMode = ProcessModeEnum.Always;
        _target = target; _shown = shown; _vitals = vitals; _ammunition = ammunition; _notice = notice;
        var source = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/main/hud_main_menu.xml")).Elements("menu").Single();
        var menu = new XElement(source.Name, source.Attributes(),
            source.Elements().Where(element => element.Attribute("name") is null).Select(element => new XElement(element)));
        XElement Branch(string name)
        {
            var tile = new XElement(source.Elements().Single(element => (string?)element.Attribute("name") == name));
            menu.Add(tile); return tile;
        }
        _reticle = Branch("ReticleCenter"); _info = Branch("Info");
        XElement Template(string name, XElement parent)
        {
            var tile = new XElement(source.Elements("template").Single(element => (string?)element.Attribute("name") == name).Elements().Single());
            parent.Add(tile); return tile;
        }
        var crosshair = Template("template_reticle_center", _reticle);
        _prompt = _info.Elements().Single(element => (string?)element.Attribute("name") == "justify_center_hotrect");
        _name = Template("template_justify_center_text", _info);
        _tiles = new(menu, setting => FalloutGameSettingStrings.Read(records, setting));
        _tiles.Bind(menu, "visible", 1);
        _tiles.Bind(_reticle, "locus", 1); _tiles.Bind(_info, "locus", 1);
        _tiles.Bind(crosshair, "x", -32); _tiles.Bind(crosshair, "y", -32);
        _tiles.Bind(_prompt, "_y", 0); _tiles.Bind(_prompt, "user0", 0);
        _tiles.BindText(_prompt, "_PCButtonText", activateKey);
        _tiles.Bind(_name, "y", 45);
        _tiles.Bind(_info, "visible", 0);
        if (vitals is not null)
        {
            (_hp, _hpMeter) = Meter("HitPoints", "sHitPointsShort", false);
            (_ap, _apMeter) = Meter("ActionPoints", "sActionPointsShort", true);
            if (ammunition is not null)
            {
                _ammo = Template("template_justify_right_text", _ap);
                _tiles.Bind(_ammo, "x", _tiles.Number(_ap, "width")); _tiles.Bind(_ammo, "y", 55);
                _tiles.Bind(_ammo, "visible", 0); _tiles.Bind(_ammo, "alpha", 255);
            }
        }
        if (notice is not null)
        {
            _warning = Template("template_justify_center_text", menu);
            _tiles.Bind(_warning, "visible", 0); _tiles.Bind(_warning, "alpha", 255); _tiles.Bind(_warning, "y", 55);
        }
        (XElement Branch, XElement Meter) Meter(string branchName, string labelSetting, bool right)
        {
            var branch = Branch(branchName);
            _tiles.Bind(branch, "locus", 1);
            var label = Template(right ? "template_justify_right_text" : "template_justify_left_text", branch);
            _tiles.Bind(label, "visible", 1); _tiles.Bind(label, "alpha", 255);
            _tiles.Bind(label, "x", right ? _tiles.Number(branch, "width") : 0); _tiles.Bind(label, "y", 0);
            _tiles.Text[label] = FalloutGameSettingStrings.Read(records, labelSetting);
            var meter = Template("template_meter", branch);
            _tiles.Bind(meter, "x", right ? 0 : 62); _tiles.Bind(meter, "y", 19);
            _tiles.Bind(meter, "width", _tiles.Number(meter, "_TotalWidth"));
            return (branch, meter);
        }
        SetMeta("opennv_ui_source", "menus/main/hud_main_menu.xml; source-Info-and-reticle-templates");
        SetMeta("opennv_ui_unverified", "retail-placement,compass,combat-effects");
    }
    public override void _Ready() { Layout(); }
    private NativeViewportLayout? _viewportLayout;
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    private void Layout()
    {
        var scale = GetViewportRect().Size.Y / 960;
        Scale = Vector2.One * scale; Size = _tiles.Screen = GetViewportRect().Size / scale; _tiles.ResolutionConverter = 1 / scale;
        _tiles.Bind(_reticle, "x", Size.X / 2); _tiles.Bind(_reticle, "y", Size.Y / 2);
        _tiles.Bind(_info, "x", Size.X / 2); _tiles.Bind(_info, "y", Size.Y / 2 + 72);
        if (_warning is not null) _tiles.Bind(_warning, "x", Size.X / 2);
        if (_hp is not null && _ap is not null)
        {
            _tiles.Bind(_hp, "x", 48); _tiles.Bind(_hp, "y", Size.Y - 125);
            _tiles.Bind(_ap, "x", Size.X - 48 - _tiles.Number(_ap, "width")); _tiles.Bind(_ap, "y", Size.Y - 125);
        }
        _tiles.ValidateDrawing();
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            Visible = _shown();
            if (!Visible) return;
            var ammo = _ammunition?.Invoke();
            if (_ammo is not null && ammo != _lastAmmo)
            {
                _lastAmmo = ammo; _tiles.Bind(_ammo, "visible", ammo is null ? 0 : 1);
                _tiles.Text[_ammo] = ammo is null ? "" : $"{ammo.Loaded}/{ammo.Reserve}"; QueueRedraw();
            }
            var notice = _notice?.Invoke();
            if (_warning is not null && notice != _lastNotice)
            {
                _lastNotice = notice; _tiles.Bind(_warning, "visible", notice is null ? 0 : 1);
                _tiles.Text[_warning] = notice ?? ""; QueueRedraw();
            }
            var vitals = _vitals?.Invoke();
            if (vitals is not null && vitals != _lastVitals)
            {
                vitals.Validate(); _lastVitals = vitals;
                _tiles.Bind(_hpMeter!, "width", _tiles.Number(_hpMeter!, "_TotalWidth") * vitals.HitPoints / vitals.MaximumHitPoints);
                _tiles.Bind(_apMeter!, "width", _tiles.Number(_apMeter!, "_TotalWidth") * vitals.ActionPoints / vitals.MaximumActionPoints);
                QueueRedraw();
            }
            var target = _target();
            if (target == _last) return;
            _last = target;
            _tiles.Bind(_info, "visible", target is null ? 0 : 1);
            _tiles.Bind(_prompt, "visible", target is null ? 0 : 1);
            _tiles.Text[_prompt] = target?.Action ?? "";
            _tiles.Text[_name] = target is null ? "" : target.Name + (target.Lock is { } locked ? "\n" + locked : "");
            _tiles.ValidateDrawing();
            QueueRedraw();
        }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_GAMEPLAY_HUD_FAIL {error.Message}"); }
    }
    public override void _Draw()
    {
        if (Error is not null) return;
        try { _tiles.Draw(this); }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_GAMEPLAY_HUD_FAIL {error.Message}"); }
    }
}
