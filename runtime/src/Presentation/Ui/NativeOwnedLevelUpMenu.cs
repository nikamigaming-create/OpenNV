using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record FalloutLevelUpSkillPresentation(int ActorValue, FalloutNativeSkillIdentity Skill,
    string Description, string? Icon);

internal sealed partial class NativeOwnedLevelUpMenu : Control
{
    internal const uint MenuId = 0x403;
    internal const string SourcePath = "menus/levelup_menu.xml";
    private sealed record Row(XElement Tile, NativeBitmapMenuButton Button, int? Skill, FalloutLevelUpPerkSource? Perk,
        string Description, string? Icon);
    private sealed record ListPage(XElement Tile, XElement Scrollbar, List<Row> Rows);
    private readonly FalloutPluginStack _records;
    private readonly FalloutLevelUpMenuSession _owner;
    private readonly NativeOwnedMenuTree _tiles;
    private readonly ListPage _skills, _perks;
    private readonly XElement _title, _description, _icon, _counter;
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button, Action Action)> _actions = [];
    private readonly List<(XElement Tile, NativeOwnedTileTarget Button, Row Row, int Direction)> _arrows = [];
    private readonly List<(XElement Tile, NativeOwnedTileTarget Button, ListPage Page)> _scrollTargets = [];
    private readonly Dictionary<Key, Action> _shortcuts = [];
    private readonly Action _completed;
    private readonly Action<Exception> _failed;
    private NativeViewportLayout? _viewportLayout;
    private Row? _focused;
    private float _scroll;
    private bool _submitted, _leaving;
    private FalloutLevelUpPage _lastPage;
    internal string? Error { get; private set; }
    private ListPage Current => _owner.Page == FalloutLevelUpPage.Perks ? _perks : _skills;
    internal object State => new
    {
        source = SourcePath,
        menu = MenuId,
        _owner.Generation,
        _owner.Level,
        _owner.Page,
        _owner.Budget,
        _owner.Assigned,
        selectedPerk = _owner.SelectedPerk?.Form.ToString(),
        skills = _skills.Rows.Select(row => new
        {
            actorValue = row.Skill,
            current = _owner.DisplayedSkill(row.Skill!.Value),
            unmodified = _owner.UnmodifiedSkill(row.Skill.Value),
            points = _owner.Allocated.GetValueOrDefault(row.Skill.Value),
            delta = _owner.AllocatedDeltas.GetValueOrDefault(row.Skill.Value),
        }).ToArray(),
        unsupportedPerks = _perks.Rows.Where(row => _owner.UnsupportedPerk(row.Perk!.Form) is not null)
            .Select(row => new { form = row.Perk!.Form.ToString(), error = _owner.UnsupportedPerk(row.Perk.Form) }).ToArray(),
        error = Error ?? _owner.Error,
        unbound = "source-list-sort-ties,scrollbar-drag,focus-and-click-audio,XP-notification-timing,retail-and-XR-pixels"
    };

    internal NativeOwnedLevelUpMenu(FalloutPluginStack records, FalloutLevelUpMenuSession owner,
        IReadOnlyList<FalloutLevelUpSkillPresentation> skills, IReadOnlyList<FalloutLevelUpPerkSource> perks,
        Action completed, Action<Exception> failed)
    {
        try
        {
            Name = "LevelUpMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
            _records = records; _owner = owner; _completed = completed; _failed = failed;
            if (!skills.Select(skill => skill.ActorValue).SequenceEqual(owner.Skills) ||
                !perks.Select(perk => new FalloutLevelUpPerkChoice(perk.Form, perk.SourceSha256, perk.MaximumRank)).SequenceEqual(owner.Perks))
                throw new InvalidDataException("Level-up presentation differs from its authoritative source rows.");
            var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read(SourcePath)).Elements("menu").Single();
            if ((string?)menu.Attribute("name") != "LevelUpMenu")
                throw new NotSupportedException("Owned level-up XML has a different menu owner.");
            _tiles = new(menu, name => FalloutGameSettingStrings.Read(records, name));
            XElement Named(string name) => menu.DescendantsAndSelf().Single(tile => (string?)tile.Attribute("name") == name);
            _title = Named("LUM_Headline_Title"); _description = Named("LUM_SelectionText");
            _icon = Named("LUM_SelectionIcon"); _counter = Named("LUM_PointCounter");
            var skillList = Named("LUM_SkillList");
            var perkList = Named("LUM_PerkList");
            _skills = new(skillList, skillList.Descendants().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar"), []);
            _perks = new(perkList, perkList.Descendants().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar"), []);
            var skillTemplate = Named("LUM_SkillTemplate").Elements().Single();
            var perkTemplate = Named("LUM_PerkTemplate").Elements().Single();
            foreach (var skill in skills)
            {
                var tile = new XElement(skillTemplate);
                tile.SetAttributeValue("name", "Skill_" + skill.ActorValue);
                _skills.Tile.Add(tile); _tiles.BindText(tile, "string", skill.Skill.DisplayName);
                var row = CreateRow(tile, skill.Skill.DisplayName, skill.ActorValue, null, skill.Description, skill.Icon);
                _skills.Rows.Add(row);
                foreach (var (name, id, direction) in new[]
                {
                    ("LUM_Template_LeftArrow", 13, -1), ("LUM_Template_RightArrow", 14, 1),
                })
                {
                    var arrow = tile.Descendants().Single(value => (string?)value.Attribute("name") == name);
                    RequireId(arrow, id);
                    var button = new NativeOwnedTileTarget { Name = name + "_" + skill.ActorValue, FocusMode = FocusModeEnum.None };
                    button.Pressed += () => Try(() => { _owner.ChangeSkill(skill.ActorValue, direction); Focus(row); Refresh(); });
                    AddChild(button); _arrows.Add((arrow, button, row, direction));
                }
            }
            foreach (var perk in perks)
            {
                var tile = new XElement(perkTemplate);
                tile.SetAttributeValue("name", "Perk_" + records.RuntimeFormId(perk.Form));
                _perks.Tile.Add(tile); _tiles.BindText(tile, "string", perk.Name ?? perk.EditorId);
                var row = CreateRow(tile, perk.Name ?? perk.EditorId, null, perk, perk.Description ?? "", perk.Icon);
                _perks.Rows.Add(row);
            }
            foreach (var (name, id, action) in new (string, int, Action)[]
            {
                ("LUM_ResetButton", 6, () => { _owner.Reset(); Refresh(); }),
                ("LUM_ContinueButton", 7, Continue),
                ("LUM_BackButton", 8, () => { _owner.Back(); Refresh(); }),
            })
            {
                var tile = Named(name); RequireId(tile, id);
                var font = _tiles.Font(tile.Descendants("text").First());
                var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
                { Name = name, DrawText = false, FocusMode = FocusModeEnum.All };
                button.Pressed += () => Try(action);
                button.MouseEntered += () => Hover(tile, true); button.MouseExited += () => Hover(tile, false);
                button.FocusEntered += () => Hover(tile, true); button.FocusExited += () => Hover(tile, false);
                AddChild(button); _actions.Add((tile, button, action));
            }
            foreach (var shortcut in menu.Elements().Where(value => value.Name.LocalName.StartsWith("_PCButton_", StringComparison.Ordinal)))
            {
                var target = _actions.SingleOrDefault(action => (string?)action.Tile.Attribute("name") == shortcut.Value.Trim());
                if (target.Tile is null || !Enum.TryParse<Key>(shortcut.Name.LocalName[10..], out var key) ||
                    !_shortcuts.TryAdd(key, target.Action))
                    throw new NotSupportedException("Level-up source shortcut is unbound or ambiguous.");
            }
            menu.Elements("template").Remove();
            foreach (var page in new[] { _skills, _perks })
            {
                _tiles.Bind(page.Scrollbar, "_current_value", 0); _tiles.Bind(page.Scrollbar, "_SetInCode", 1);
                foreach (var marker in page.Scrollbar.Descendants().Where(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker"))
                { _tiles.Bind(marker, "dragy", 0); _tiles.Bind(marker, "dragoffsety", 0); }
                foreach (var (name, direction, jump) in new[]
                {
                    ("scrollbar_vert_up", -1, false), ("scrollbar_vert_down", 1, false),
                    ("scrollbar_vert_page_up", -1, true), ("scrollbar_vert_page_down", 1, true),
                })
                {
                    var tile = page.Scrollbar.Elements().Single(value => (string?)value.Attribute("name") == name);
                    var button = new NativeOwnedTileTarget { Name = name, FocusMode = FocusModeEnum.None };
                    button.Pressed += () => Try(() => Scroll(direction * _tiles.Number(page.Scrollbar, jump ? "_jump_size" : "_step_size")));
                    button.MouseEntered += () => Hover(tile, true); button.MouseExited += () => Hover(tile, false);
                    AddChild(button); _scrollTargets.Add((tile, button, page));
                }
            }
            _lastPage = owner.Page;
            SetMeta("opennv_ui_source", SourcePath + ";winning-AVIF-PERK;owned-font-and-atlas");
        }
        catch { Free(); throw; }
    }

    private void RequireId(XElement tile, int expected)
    {
        if (_tiles.Number(tile, "id") != expected)
            throw new NotSupportedException("Level-up owned action has a different source command ID.");
    }

    private Row CreateRow(XElement tile, string name, int? skill, FalloutLevelUpPerkSource? perk, string description, string? icon)
    {
        var font = _tiles.Font(tile.Descendants("text").First());
        var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.Color)
        { Name = (string)tile.Attribute("name")!, Text = name, DrawText = false, FocusMode = FocusModeEnum.All };
        var row = new Row(tile, button, skill, perk, description, icon);
        button.Pressed += () => Try(() =>
        {
            if (row.Perk is not null) _owner.SelectPerk(row.Perk.Form);
            Focus(row); Refresh();
        });
        button.MouseEntered += () => Try(() => { Focus(row); Refresh(); });
        button.FocusEntered += () => Try(() => { Focus(row); EnsureVisible(row); Refresh(); });
        AddChild(button);
        return row;
    }

    private void Hover(XElement tile, bool active) { _tiles.Bind(tile, "mouseover", active ? 1 : 0); QueueRedraw(); }
    public override void _EnterTree() => _viewportLayout = new(this, Refresh);
    public override void _ExitTree()
    {
        _leaving = true;
        _viewportLayout?.Dispose();
        StopInteraction();
    }
    public override void _Ready()
    {
        Refresh();
        if (Error is null && Current.Rows.Count != 0)
        {
            Focus(Current.Rows[0]); Refresh();
            Callable.From(Current.Rows[0].Button.GrabFocus).CallDeferred();
        }
    }
    public override void _Process(double delta) { if (!_submitted) Refresh(); }

    private void Focus(Row row)
    {
        _focused = row;
        var unsupported = row.Perk is { } perk ? _owner.UnsupportedPerk(perk.Form) : null;
        _tiles.Text[_description] = unsupported is null ? row.Description :
            row.Description + "\n\nUnsupported perk effect: " + unsupported;
        if (row.Icon is { Length: > 0 } icon) { _tiles.SetFilename(_icon, icon); _tiles.Bind(_icon, "visible", 1); }
        else _tiles.Bind(_icon, "visible", 0);
        _tiles.Bind(Current.Tile, "_highlight_y", _tiles.Number(row.Tile, "y"));
        _tiles.Bind(Current.Tile, "_selected_height", _tiles.Number(row.Tile, "height"));
    }
    private void EnsureVisible(Row row)
    {
        var y = _tiles.Number(row.Tile, "_y"); var height = _tiles.Number(row.Tile, "height");
        var viewport = _tiles.Number(Current.Tile, "height");
        if (y < _scroll) _scroll = y;
        else if (y + height > _scroll + viewport) _scroll = y + height - viewport;
    }

    private void Refresh() => Try(() =>
    {
        if (_submitted) return;
        var viewportSize = GetViewportRect().Size;
        if (viewportSize.Y <= 0) return;
        Scale = new(viewportSize.Y / 720, viewportSize.Y / 720);
        _tiles.Screen = viewportSize / Scale;
        _tiles.ResolutionConverter = 1 / Scale.Y;
        if (_owner.Page != _lastPage)
        {
            _lastPage = _owner.Page; _focused = null; _scroll = 0;
        }
        _tiles.Bind(_tiles.Root, "_CurrentPage", _owner.Page == FalloutLevelUpPage.Perks ? 1 : 0);
        _tiles.Bind(_tiles.Root, "_EndPage", _owner.HasPerkPage ? 1 : 0);
        _tiles.Bind(_tiles.Root, "_CurrPoints", _owner.Page == FalloutLevelUpPage.Perks ? _owner.SelectedPerk is null ? 0 : 1 : _owner.Assigned);
        _tiles.Bind(_tiles.Root, "_MaxPoints", _owner.Page == FalloutLevelUpPage.Perks ? 1 : _owner.Budget);
        _tiles.Text[_title] = FalloutLevelUpMenuText.Title(_records, _owner.Level);
        var remaining = _owner.Page == FalloutLevelUpPage.Perks ? _owner.SelectedPerk is null ? 1 : 0 : _owner.Budget - _owner.Assigned;
        _tiles.Text[_counter] = FalloutLevelUpMenuText.Counter(_records, _owner.Page == FalloutLevelUpPage.Perks, remaining);
        _tiles.Bind(_counter, "visible", remaining == 0 ? 0 : 1);
        foreach (var page in new[] { _skills, _perks })
        {
            _tiles.Bind(page.Tile, "visible", page == Current ? 1 : 0);
            var y = 0f;
            for (var index = 0; index < page.Rows.Count; ++index)
            {
                var row = page.Rows[index];
                _tiles.Bind(row.Tile, "listindex", index);
                if (_tiles.Number(row.Tile, "height", false) == 0)
                    _tiles.Bind(row.Tile, "height", _tiles.Number(row.Tile.Descendants("text").First(), "height") +
                        _tiles.Number(row.Tile, "_VerticalSpacing"));
                _tiles.Bind(row.Tile, "_y", y);
                if (row.Skill is { } skill)
                {
                    var unmodified = _owner.UnmodifiedSkill(skill);
                    var delta = _owner.AllocatedDeltas.GetValueOrDefault(skill);
                    _tiles.Bind(row.Tile, "_MinValue", 0); _tiles.Bind(row.Tile, "_MaxValue", 100);
                    _tiles.Bind(row.Tile, "_BaseValue", unmodified - delta);
                    _tiles.Bind(row.Tile, "_ExtraValue", _owner.DisplayedSkill(skill) - unmodified);
                    _tiles.Bind(row.Tile, "_AddedValue", delta);
                    _tiles.Bind(row.Tile, "_OverflowValue", Math.Max(0, unmodified - 100));
                    _tiles.BindText(row.Tile, "_DisplayString", _owner.DisplayedSkill(skill).ToString(CultureInfo.InvariantCulture));
                    _tiles.Bind(row.Tile, "_enabled", 1);
                }
                else
                {
                    _tiles.Bind(row.Tile, "_selected", _owner.SelectedPerk?.Form == row.Perk!.Form ? 1 : 0);
                    _tiles.Bind(row.Tile, "_enabled", _owner.PerkEnabled(row.Perk!.Form) ? 1 : 0);
                }
                y += _tiles.Number(row.Tile, "height");
                row.Button.Visible = page == Current;
                row.Button.Disabled = row.Perk is not null && !_owner.PerkEnabled(row.Perk.Form);
            }
            if (page != Current) continue;
            if (string.IsNullOrWhiteSpace(page.Tile.Element("height")?.Value) && page.Rows.Count != 0)
                _tiles.Bind(page.Tile, "height", _tiles.Number(page.Rows[0].Tile, "height") * _tiles.Number(page.Tile, "_number_of_visible_items"));
            var viewport = _tiles.Number(page.Tile, "height");
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, y - viewport));
            var steps = y > viewport && y > 0 ? MathF.Ceiling(page.Rows.Count * (y - viewport) / y) + 1 : 1;
            if (steps > 1) _tiles.Bind(page.Tile, "_scroll_delta", (y - viewport) / (steps - 1));
            _tiles.Bind(page.Scrollbar, "_number_of_items", steps);
            var scrollDelta = _tiles.Number(page.Tile, "_scroll_delta");
            _tiles.Bind(page.Scrollbar, "_current_value", scrollDelta == 0 ? 0 : _scroll / scrollDelta);
            var bounds = new Rect2(_tiles.Position(page.Tile), new(_tiles.Number(page.Tile, "width"), viewport));
            foreach (var row in page.Rows)
            {
                var rect = new Rect2(_tiles.Position(row.Tile), new(_tiles.Number(row.Tile, "width"), _tiles.Number(row.Tile, "height"))).Intersection(bounds);
                row.Button.Visible = rect.HasArea(); row.Button.Position = rect.Position; row.Button.Size = rect.Size;
            }
        }
        if (_focused is null && Current.Rows.Count != 0) _focused = Current.Rows[0];
        if (_focused is not null) Focus(_focused);
        foreach (var (tile, button, _) in _actions)
        {
            var back = (string?)tile.Attribute("name") == "LUM_BackButton";
            button.Visible = !back || _owner.Page == FalloutLevelUpPage.Perks;
            button.Disabled = (string?)tile.Attribute("name") == "LUM_ContinueButton" && !_owner.CanContinue;
            button.Text = _tiles.String(tile); button.Position = _tiles.Position(tile);
            button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
        foreach (var (tile, button, row, direction) in _arrows)
        {
            var visible = _owner.Page == FalloutLevelUpPage.Skills && _owner.CanChangeSkill(row.Skill!.Value, direction);
            _tiles.Bind(tile, "visible", visible ? 1 : 0);
            button.Visible = visible && row.Button.Visible;
            if (!button.Visible) continue;
            button.Position = _tiles.Position(tile);
            button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
        foreach (var (tile, button, page) in _scrollTargets)
        {
            button.Visible = page == Current && _tiles.Number(page.Scrollbar, "visible") != 0 && _tiles.Number(tile, "visible") != 0;
            if (!button.Visible) continue;
            button.Position = _tiles.Position(tile);
            button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
        _tiles.ValidateDrawing(); QueueRedraw();
    });

    private void Continue()
    {
        if (!_owner.Continue()) return;
        if (_owner.Completed) { _submitted = true; StopInteraction(); _completed(); }
        else Refresh();
    }
    private void Scroll(float step) { _scroll += step * _tiles.Number(Current.Tile, "_scroll_delta"); Refresh(); }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Error is not null || _submitted) return;
        Try(() =>
        {
            if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { Scroll(mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1); GetViewport().SetInputAsHandled(); }
            if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
            if (_shortcuts.TryGetValue(key.Keycode, out var action)) { action(); GetViewport().SetInputAsHandled(); return; }
            if (_focused?.Skill is { } skill && key.Keycode is Key.Left or Key.Right)
            {
                _owner.ChangeSkill(skill, key.Keycode == Key.Left ? -1 : 1); Refresh(); GetViewport().SetInputAsHandled();
            }
            if (key.Keycode is Key.Up or Key.Down && Current.Rows.Count != 0)
            {
                var index = _focused is null ? 0 : Current.Rows.IndexOf(_focused);
                var row = Current.Rows[Math.Clamp(index + (key.Keycode == Key.Up ? -1 : 1), 0, Current.Rows.Count - 1)];
                Focus(row); EnsureVisible(row); Refresh(); row.Button.GrabFocus(); GetViewport().SetInputAsHandled();
            }
        });
    }
    private void Try(Action action)
    {
        if (Error is not null || _submitted || _leaving) return;
        try { action(); }
        catch (Exception error)
        {
            Error = error.Message;
            StopInteraction();
            try { _failed(error); }
            catch (Exception notificationError) { GD.PushError($"OPENNV_LEVELUP_FAILURE_CALLBACK {notificationError.Message}"); }
        }
    }
    private void StopInteraction()
    {
        SetProcess(false); SetProcessUnhandledInput(false);
        foreach (var page in new[] { _skills, _perks })
            foreach (var row in page.Rows)
                if (GodotObject.IsInstanceValid(row.Button)) row.Button.Disabled = true;
        foreach (var (_, button, _) in _actions)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = true;
        foreach (var (_, button, _, _) in _arrows)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = true;
        foreach (var (_, button, _) in _scrollTargets)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = true;
    }

    public override void _Draw() => Try(() => _tiles.Draw(this));
}

internal static class FalloutLevelUpMenuText
{
    internal static string Title(FalloutPluginStack records, int level) =>
        Format(FalloutGameSettingStrings.Read(records, "sLevelUpTitleText"), level);

    internal static string Counter(FalloutPluginStack records, bool perks, int remaining) =>
        Format(FalloutGameSettingStrings.Read(records, perks ?
            remaining == 1 ? "sLevelUpPerkCounter" : "sLevelUpPerkCounterPl" :
            remaining == 1 ? "sLevelUpSkillCounter" : "sLevelUpSkillCounterPl"), remaining);

    internal static string Format(string source, int value)
    {
        var output = new StringBuilder();
        var consumed = false;
        for (var at = 0; at < source.Length; ++at)
        {
            if (source[at] != '%') { output.Append(source[at]); continue; }
            if (++at == source.Length) throw new NotSupportedException("Level-up source string has an unfinished format.");
            if (source[at] == '%') { output.Append('%'); continue; }
            if (consumed || source[at] is not ('d' or 'i' or 'u'))
                throw new NotSupportedException("Level-up source string requires another typed format owner.");
            if (source[at] == 'u' && value < 0)
                throw new NotSupportedException("Level-up unsigned source format cannot display a negative counter.");
            consumed = true;
            output.Append(value.ToString(CultureInfo.InvariantCulture));
        }
        return output.ToString();
    }
}
