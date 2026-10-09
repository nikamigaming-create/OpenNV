using System.Globalization;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedLevelUpMenu : Control
{
    private sealed record Row(XElement Tile, NativeBitmapMenuButton Button, int? Skill, FalloutFormKey? Perk,
        string Name, string Description, string? Icon);
    private sealed record Target(XElement Tile, NativeOwnedTileTarget Button, Row? Row, int Direction, XElement? Bar);
    private readonly NativeLevelUpMenuSource _source;
    private NativeOwnedMenuTree Tiles => _source.Tiles;
    private readonly FalloutLevelUpMenuSession _session;
    private readonly Func<bool> _ownsRequest;
    private readonly Action _accepted;
    private readonly Action<Exception> _failed;
    private readonly List<Row> _skills = [], _perks = [];
    private readonly List<Target> _targets = [];
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button, Action Action)> _actions = [];
    private readonly Dictionary<Key, Action> _shortcuts = [];
    private readonly Dictionary<JoyButton, Action> _padShortcuts = [];
    private readonly Dictionary<XElement, float> _scroll = [];
    private XElement _skillList = null!, _perkList = null!, _title = null!, _description = null!, _icon = null!, _counter = null!;
    private Row? _focused;
    private NativeViewportLayout? _viewportLayout;
    private FalloutLevelUpPage? _laidOutPage;
    private bool _submitted, _faulted;
    internal string? Error { get; private set; }
    internal uint MenuId => NativeLevelUpMenuSource.MenuId;
    internal object State => new
    {
        source = NativeLevelUpMenuSource.LogicalPath,
        identity = _source.Identity,
        sha256 = _source.Sha256,
        generation = _session.Generation,
        level = _session.Level,
        page = _session.Page.ToString(),
        budget = _session.Budget,
        assigned = _session.Assigned,
        selected = _session.SelectedPerk?.Form,
        focusedSkill = _focused?.Skill,
        focusedPerk = _focused?.Perk,
        error = Error ?? _session.Error,
        submitted = _submitted,
        unbound = "source-candidate-visibility-and-no-available-perk-policy,source-list-sort-ties,scrollbar-drag,focus-and-click-sounds,menu-notification-cadence,exact-layout-timing,matched-retail-and-XR-pixels",
    };

    private NativeOwnedLevelUpMenu(NativeLevelUpMenuSource source, FalloutLevelUpMenuSession session,
        Func<bool> ownsRequest, Action accepted, Action<Exception> failed)
    {
        _source = source; _session = session; _ownsRequest = ownsRequest; _accepted = accepted; _failed = failed;
        Name = "LevelUpMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
    }
    internal static NativeOwnedLevelUpMenu Create(NativeLevelUpMenuSource source, FalloutLevelUpMenuSession session,
        Func<bool> ownsRequest, Action accepted, Action<Exception> failed)
    {
        source.Catalogue.RequireSession(session);
        var menu = new NativeOwnedLevelUpMenu(source, session, ownsRequest, accepted, failed);
        try { menu.Initialize(); return menu; }
        catch { menu.Free(); throw; }
    }
    private void Initialize()
    {
        _skillList = _source.Named("LUM_SkillList"); _perkList = _source.Named("LUM_PerkList");
        _title = _source.Named("LUM_Headline_Title"); _description = _source.Named("LUM_SelectionText");
        _icon = _source.Named("LUM_SelectionIcon"); _counter = _source.Named("LUM_PointCounter");
        var skillTemplate = _source.Named("LUM_SkillTemplate").Elements().Single();
        var perkTemplate = _source.Named("LUM_PerkTemplate").Elements().Single();
        foreach (var skill in _source.Catalogue.Skills)
        {
            var row = AddRow(_skillList, skillTemplate, skill.Name, skill.Description, skill.Icon, skill.ActorValue, null);
            _skills.Add(row);
            foreach (var (name, direction) in new[] { ("LUM_Template_LeftArrow", -1), ("LUM_Template_RightArrow", 1) })
            {
                var tile = row.Tile.Descendants().Single(tile => (string?)tile.Attribute("name") == name);
                var button = new NativeOwnedTileTarget { Name = name, Text = skill.Name, FocusMode = FocusModeEnum.None };
                AddChild(button);
                button.SetMeta("opennv_source_skill", skill.ActorValue);
                button.SetMeta("opennv_source_direction", direction);
                _targets.Add(new(tile, button, row, direction, null));
                button.Pressed += () => Try(() => { Focus(row); _session.ChangeSkill(skill.ActorValue, direction); Refresh(); });
                Hover(tile, button);
            }
        }
        foreach (var perk in _source.Catalogue.Perks)
            _perks.Add(AddRow(_perkList, perkTemplate, perk.Name!, perk.Description ?? "", perk.Icon ?? perk.SmallIcon, null, perk.Form));

        AddAction("LUM_ResetButton", () => { _session.Reset(); Refresh(); });
        AddAction("LUM_ContinueButton", Submit);
        AddAction("LUM_BackButton", () => { _session.Back(); Refresh(); });
        foreach (var shortcut in Tiles.Root.Elements().Where(property => property.Name.LocalName.StartsWith("_PCButton_", StringComparison.Ordinal)))
        {
            var action = _actions.SingleOrDefault(action => (string?)action.Tile.Attribute("name") == shortcut.Value.Trim());
            if (action.Tile is null || !Enum.TryParse<Key>(shortcut.Name.LocalName[10..], out var key) || !_shortcuts.TryAdd(key, action.Action))
                throw new NotSupportedException("Level-up source keyboard action is absent, ambiguous or unowned.");
        }
        foreach (var (tile, _, action) in _actions)
        {
            var declared = tile.Element("_xbox_button")?.Value.Trim();
            if (declared is null) continue;
            var button = declared.ToLowerInvariant() switch
            {
                "entity_xbuttona" => JoyButton.A,
                "entity_xbuttonb" => JoyButton.B,
                "entity_xbuttonx" => JoyButton.X,
                "entity_xbuttony" => JoyButton.Y,
                _ => throw new NotSupportedException("Level-up source controller action has an unowned token."),
            };
            if (!_padShortcuts.TryAdd(button, action)) throw new NotSupportedException("Level-up source controller actions are ambiguous.");
        }
        Tiles.Root.Elements("template").Remove();
        foreach (var list in new[] { _skillList, _perkList })
        {
            _scroll.Add(list, 0);
            var bar = list.Elements().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar");
            Tiles.Bind(bar, "_SetInCode", 1); Tiles.Bind(bar, "_current_value", 0);
            foreach (var marker in bar.Descendants().Where(tile => (string?)tile.Attribute("name") == "scrollbar_vert_marker"))
            { Tiles.Bind(marker, "dragy", 0); Tiles.Bind(marker, "dragoffsety", 0); }
            foreach (var (name, direction, page) in new[]
            {
                ("scrollbar_vert_up", -1, false), ("scrollbar_vert_down", 1, false),
                ("scrollbar_vert_page_up", -1, true), ("scrollbar_vert_page_down", 1, true),
            })
            {
                var tile = bar.Elements().Single(tile => (string?)tile.Attribute("name") == name);
                var button = new NativeOwnedTileTarget { Name = name, Text = name, FocusMode = FocusModeEnum.None };
                AddChild(button); _targets.Add(new(tile, button, null, direction, bar));
                button.Pressed += () => Try(() => Scroll(list, direction * Tiles.Number(bar, page ? "_jump_size" : "_step_size")));
                Hover(tile, button);
            }
        }
        SetMeta("opennv_ui_source", NativeLevelUpMenuSource.LogicalPath);
        SetMeta("opennv_ui_source_sha256", _source.Sha256);
        SetMeta("opennv_source_request_generation", _session.Generation.ToString(CultureInfo.InvariantCulture));
    }
    private Row AddRow(XElement list, XElement template, string name, string description, string? icon, int? skill, FalloutFormKey? perk)
    {
        var tile = new XElement(template);
        tile.SetAttributeValue("name", skill is { } slot ? "Skill_" + slot : "Perk_" + _source.RuntimeFormId(perk!.Value));
        list.Add(tile); Tiles.BindText(tile, "string", name); Tiles.Bind(tile, "_enabled", 1);
        Tiles.Bind(tile, "listindex", skill is not null ? _skills.Count : _perks.Count);
        var font = Tiles.Font(tile.Descendants("text").First());
        var button = new NativeBitmapMenuButton(font.Font, font.Atlas, Tiles.Color)
        { Name = (string)tile.Attribute("name")!, Text = name, DrawText = false, FocusMode = FocusModeEnum.All };
        AddChild(button);
        button.SetMeta("opennv_source_choice_kind", skill is not null ? "level-up-skill" : "level-up-perk");
        if (skill is { } skillSlot) button.SetMeta("opennv_source_skill", skillSlot);
        if (perk is { } perkForm) button.SetMeta("opennv_source_perk", perkForm.ToString());
        var row = new Row(tile, button, skill, perk, name, description, icon);
        button.Pressed += () => Try(() =>
        {
            Focus(row);
            if (row.Perk is { } form) _session.SelectPerk(form);
            Refresh();
        });
        button.MouseEntered += () => Try(() => { Focus(row); Refresh(); });
        button.FocusEntered += () => Try(() => { Focus(row); EnsureVisible(row); Refresh(); });
        return row;
    }
    private void AddAction(string name, Action action)
    {
        var tile = _source.Named(name); var font = Tiles.Font(tile.Descendants("text").First());
        var button = new NativeBitmapMenuButton(font.Font, font.Atlas, Tiles.Color)
        { Name = name, DrawText = false, FocusMode = FocusModeEnum.All };
        AddChild(button); _actions.Add((tile, button, action));
        button.SetMeta("opennv_source_action_id", (int)Tiles.Number(tile, "id"));
        button.Pressed += () => Try(action); Hover(tile, button);
    }
    private void Hover(XElement tile, BaseButton button)
    {
        button.MouseEntered += () => { Tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
        button.MouseExited += () => { Tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
        button.FocusEntered += () => { Tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
        button.FocusExited += () => { Tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
    }
    public override void _EnterTree() => _viewportLayout = new(this, Refresh);
    public override void _ExitTree() { _viewportLayout?.Dispose(); _viewportLayout = null; }
    public override void _Ready() => Refresh();
    public override void _Process(double delta) { if (!_submitted && !_faulted) Refresh(); }
    private IReadOnlyList<Row> CurrentRows => _session.Page == FalloutLevelUpPage.Skills ? _skills : _perks;
    private XElement CurrentList => _session.Page == FalloutLevelUpPage.Skills ? _skillList : _perkList;
    private void Focus(Row row)
    {
        _focused = row; Tiles.Text[_description] = row.Description;
        if (row.Icon is { Length: > 0 } icon) { Tiles.SetFilename(_icon, icon); Tiles.Bind(_icon, "visible", 1); }
        else Tiles.Bind(_icon, "visible", 0);
        var list = row.Skill is not null ? _skillList : _perkList;
        Tiles.Bind(list, "_highlight_y", Tiles.Number(row.Tile, "y"));
        Tiles.Bind(list, "_selected_height", Tiles.Number(row.Tile, "height"));
    }
    private void EnsureVisible(Row row)
    {
        var list = row.Skill is not null ? _skillList : _perkList;
        var y = Tiles.Number(row.Tile, "_y"); var height = Tiles.Number(row.Tile, "height");
        if (y < _scroll[list]) _scroll[list] = y;
        else if (y + height > _scroll[list] + Tiles.Number(list, "height")) _scroll[list] = y + height - Tiles.Number(list, "height");
    }
    private void Refresh() => Try(ApplyLayout);
    private void ApplyLayout()
    {
        var scale = GetViewportRect().Size.Y / 960;
        if (scale <= 0) return;
        Scale = Vector2.One * scale; Size = Tiles.Screen = GetViewportRect().Size / scale; Tiles.ResolutionConverter = 1 / scale;
        var skillPage = _session.Page == FalloutLevelUpPage.Skills;
        Tiles.Bind(Tiles.Root, "_CurrentPage", skillPage ? 0 : 1); Tiles.Bind(Tiles.Root, "_EndPage", _session.HasPerkPage ? 1 : 0);
        Tiles.Bind(Tiles.Root, "_CurrPoints", skillPage ? _session.Assigned : _session.SelectedPerk is null ? 0 : 1);
        Tiles.Bind(Tiles.Root, "_MaxPoints", skillPage ? _session.Budget : 1);
        Tiles.Text[_title] = _source.Title(_session.Level);
        var remaining = skillPage ? _session.Budget - _session.Assigned : _session.SelectedPerk is null ? 1 : 0;
        Tiles.Text[_counter] = _source.Counter(_session.Page, remaining); Tiles.Bind(_counter, "visible", remaining == 0 ? 0 : 1);
        LayoutList(_skillList, _skills, skillPage); LayoutList(_perkList, _perks, !skillPage);
        if (_laidOutPage != _session.Page)
        {
            _laidOutPage = _session.Page; _focused = CurrentRows.FirstOrDefault();
            if (_focused is null) { Tiles.Text[_description] = ""; Tiles.Bind(_icon, "visible", 0); }
            else
            {
                Focus(_focused);
                var button = _focused.Button;
                Callable.From(() => { if (!_faulted && !_submitted && IsInsideTree() && button.Visible) button.GrabFocus(); }).CallDeferred();
            }
        }
        if (_focused is { } focused) Focus(focused);
        foreach (var (tile, button, _) in _actions)
        {
            var name = (string)tile.Attribute("name")!;
            if (name == "LUM_ContinueButton") Tiles.BindText(tile, "string", Tiles.String(tile, _session.Page == FalloutLevelUpPage.Perks || !_session.HasPerkPage ? "_Title_1" : "_Title_0"));
            button.Text = Tiles.String(tile); button.Visible = Tiles.Number(tile, "visible") != 0;
            button.Disabled = name == "LUM_ContinueButton" && !_session.CanContinue || name == "LUM_BackButton" && skillPage;
            Place(button, tile);
        }
        foreach (var target in _targets)
        {
            if (target.Row is { } row)
            {
                var enabled = skillPage && _session.CanChangeSkill(row.Skill!.Value, target.Direction);
                Tiles.Bind(target.Tile, "target", enabled ? 1 : 0); Tiles.Bind(target.Tile, "visible", enabled ? 1 : 0);
                target.Button.Disabled = !enabled; PlaceClipped(target.Button, target.Tile, _skillList, skillPage);
                target.Button.Visible &= enabled;
            }
            else
            {
                target.Button.Visible = target.Bar!.Parent == CurrentList && Tiles.Number(target.Bar, "visible") != 0 && Tiles.Number(target.Tile, "visible") != 0;
                Place(target.Button, target.Tile);
            }
        }
        Tiles.ValidateDrawing(); QueueRedraw();
    }
    private void LayoutList(XElement list, IReadOnlyList<Row> rows, bool active)
    {
        Tiles.Bind(list, "visible", active ? 1 : 0); Tiles.Bind(list, "_enabled", active ? 1 : 0);
        var y = 0f;
        foreach (var row in rows)
        {
            if (Tiles.Number(row.Tile, "height", false) == 0)
                Tiles.Bind(row.Tile, "height", Tiles.Number(row.Tile.Descendants("text").First(), "height") + Tiles.Number(row.Tile, "_VerticalSpacing"));
            Tiles.Bind(row.Tile, "_y", y); y += Tiles.Number(row.Tile, "height");
            if (row.Skill is { } skill)
            {
                var added = _session.AllocatedDeltas.GetValueOrDefault(skill);
                var basis = _session.UnmodifiedSkill(skill); var current = _session.DisplayedSkill(skill);
                Tiles.Bind(row.Tile, "_BaseValue", basis - added); Tiles.Bind(row.Tile, "_ExtraValue", current - basis);
                Tiles.Bind(row.Tile, "_AddedValue", added); Tiles.Bind(row.Tile, "_OverflowValue", Math.Max(0, basis - 100));
                Tiles.BindText(row.Tile, "_DisplayString", current.ToString(CultureInfo.InvariantCulture));
                row.Button.Disabled = false;
            }
            else if (row.Perk is { } form)
            {
                var state = _session.PerkState(form);
                Tiles.Bind(row.Tile, "_enabled", state.Enabled ? 1 : 0); Tiles.Bind(row.Tile, "_selected", state.Selected ? 1 : 0);
                // Ineligible rows remain focusable for source descriptions.
                // Selection still rechecks the actual rank/condition owner.
                row.Button.Disabled = false;
            }
        }
        if (rows.Count != 0 && string.IsNullOrWhiteSpace(list.Element("height")?.Value))
            Tiles.Bind(list, "height", Tiles.Number(rows[0].Tile, "height") * Tiles.Number(list, "_number_of_visible_items"));
        var height = Tiles.Number(list, "height");
        if (height <= 0 && rows.Count != 0) throw new InvalidDataException("Level-up source list has no positive viewport height.");
        _scroll[list] = Math.Clamp(_scroll[list], 0, Math.Max(0, y - height));
        var steps = y > height && y > 0 ? MathF.Ceiling(rows.Count * (y - height) / y) + 1 : 1;
        if (steps > 1) Tiles.Bind(list, "_scroll_delta", (y - height) / (steps - 1));
        var bar = list.Elements().Single(tile => (string?)tile.Attribute("name") == "lb_scrollbar");
        Tiles.Bind(bar, "_number_of_items", steps);
        var increment = Tiles.Number(list, "_scroll_delta");
        if (increment <= 0 || !float.IsFinite(increment)) throw new InvalidDataException("Level-up source scrolling has no positive step.");
        Tiles.Bind(bar, "_current_value", _scroll[list] / increment);
        foreach (var row in rows) PlaceClipped(row.Button, row.Tile, list, active);
    }
    private void Place(BaseButton button, XElement tile)
    {
        button.Position = Tiles.Position(tile); button.Size = new(Tiles.Number(tile, "width"), Tiles.Number(tile, "height"));
    }
    private void PlaceClipped(BaseButton button, XElement tile, XElement list, bool active)
    {
        var bounds = new Rect2(Tiles.Position(list), new(Tiles.Number(list, "width"), Tiles.Number(list, "height")));
        var rect = new Rect2(Tiles.Position(tile), new(Tiles.Number(tile, "width"), Tiles.Number(tile, "height"))).Intersection(bounds);
        button.Visible = active && rect.HasArea(); button.Position = rect.Position; button.Size = rect.Size;
    }
    private void Scroll(XElement list, float step) { _scroll[list] += step * Tiles.Number(list, "_scroll_delta"); Refresh(); }
    private void Submit()
    {
        if (!_session.CanContinue) return;
        _session.Continue();
        if (!_session.Completed) { Refresh(); return; }
        _submitted = true; _accepted();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_faulted || _submitted) return;
        Try(() =>
        {
            if (input is InputEventJoypadButton { Pressed: true } pad && _padShortcuts.TryGetValue(pad.ButtonIndex, out var padAction))
            { padAction(); GetViewport().SetInputAsHandled(); return; }
            if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { Scroll(CurrentList, mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1); GetViewport().SetInputAsHandled(); }
            if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
            if (_shortcuts.TryGetValue(key.Keycode, out var action)) { action(); GetViewport().SetInputAsHandled(); return; }
            if (key.Keycode is Key.Left or Key.Right && _focused?.Skill is { } skill && _session.Page == FalloutLevelUpPage.Skills)
            { _session.ChangeSkill(skill, key.Keycode == Key.Left ? -1 : 1); Refresh(); GetViewport().SetInputAsHandled(); }
            if (key.Keycode is Key.Up or Key.Down && CurrentRows.Count != 0)
            {
                var rows = CurrentRows; var current = _focused is null ? 0 : rows.ToList().IndexOf(_focused);
                var row = rows[Math.Clamp(current + (key.Keycode == Key.Up ? -1 : 1), 0, rows.Count - 1)];
                Focus(row); EnsureVisible(row); Refresh(); row.Button.GrabFocus(); GetViewport().SetInputAsHandled();
            }
            // An unmatched key, including Escape, cannot cancel a consumed
            // source level. Only the source-bound Back/Reset actions reverse it.
            if (key.Keycode == Key.Escape) GetViewport().SetInputAsHandled();
        });
    }
    private void Try(Action action)
    {
        if (_faulted || _submitted) return;
        try
        {
            if (!_ownsRequest()) throw new InvalidOperationException("Native level-up input no longer owns its source request.");
            if (_session.Error is { } error) throw new InvalidOperationException("Source level-up request retains a failed operation: " + error);
            action();
        }
        catch (Exception error)
        {
            _faulted = true; Error = error.Message;
            foreach (var row in _skills.Concat(_perks)) row.Button.Disabled = true;
            foreach (var (_, button, _) in _actions) button.Disabled = true;
            foreach (var target in _targets) target.Button.Disabled = true;
            _failed(error);
        }
    }
    public override void _Draw() { if (_faulted) return; Try(() => Tiles.Draw(this)); }
}
