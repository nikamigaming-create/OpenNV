using System.Globalization;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Original Pip-Boy tiles bound to the live gameplay owners.</summary>
internal sealed partial class NativeOwnedPipBoyMenu : Control
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPipBoyState _state;
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutQuestState _quests;
    private readonly FalloutReferenceWorld _references;
    private readonly Func<GameplayVitals> _vitals;
    private readonly Func<FalloutNativeSpecialState> _special;
    private readonly Func<string, float>? _actorValue;
    private readonly string _playerName;
    private readonly IReadOnlyList<FalloutNativeSkillIdentity> _skills, _tags;
    private readonly IReadOnlyList<FalloutNativeTraitIdentity> _traits;
    private FalloutFormKey? _world;
    private Vector3 _player;
    private float _heading;
    private NativeOwnedMenuTree _tiles = null!;
    private readonly List<(XElement Tile, NativeBitmapMenuButton Button)> _buttons = [];
    private readonly Dictionary<string, XElement> _templates = [];
    private NativeOwnedWorldMap? _map;
    private int _offset;
    private FalloutCampaignItem? _selectedItem;
    private int _selectedAttribute;
    private FalloutFormKey? _selectedDetail;
    internal string? Error { get; private set; }
    internal event Action? PageChanged;
    internal void RefreshWorld(FalloutFormKey? world, Vector3 player, float heading)
    {
        _world = world; _player = player; _heading = heading;
        Select(_state.Page, _state.Selection);
    }
    internal object State => new
    {
        open = _state.Open,
        page = _state.Page.ToString(),
        selection = _state.Selection,
        items = _state.Items.Count,
        tags = _tags.Select(tag => tag.RuntimeFormId).ToArray(),
        map = _map?.State,
        error = Error,
        unbound = "limb-damage,radiation,timed-skill-effects,skill-advancement,perks,local-map,radio,fast-travel,aid-effects,item-drop-and-repair"
    };

    internal NativeOwnedPipBoyMenu(FalloutPluginStack records, FalloutPipBoyState state, FalloutPlayerInventory inventory,
        FalloutQuestState quests, FalloutReferenceWorld references, Func<GameplayVitals> vitals,
        Func<FalloutNativeSpecialState> special, string playerName, IReadOnlyList<FalloutNativeSkillIdentity> skills,
        IReadOnlyList<FalloutNativeSkillIdentity> tags, IReadOnlyList<FalloutNativeTraitIdentity> traits,
        FalloutFormKey? world, Vector3 sourcePlayer, float heading, Func<string, float>? actorValue = null)
    {
        Name = "OwnedPipBoyMenu"; ProcessMode = ProcessModeEnum.Always; Size = new(1280, 960);
        _records = records; _state = state; _inventory = inventory; _quests = quests; _references = references;
        _vitals = vitals; _special = special; _playerName = playerName; _world = world; _player = sourcePlayer; _heading = heading;
        _skills = skills; _tags = tags; _traits = traits;
        _actorValue = actorValue;
    }
    public override void _Ready() => Refresh();
    private XElement Tile(string name) => _tiles.Root.DescendantsAndSelf().SingleOrDefault(tile => (string?)tile.Attribute("name") == name)
        ?? throw new InvalidDataException($"Pip-Boy source tile is absent: {name}.");
    private string Setting(string name) => FalloutGameSettingStrings.Read(_records, name);
    private void Bind(string name, string trait, float value) => _tiles.Bind(Tile(name), trait, value);
    private void Text(string name, string value) => _tiles.Text[Tile(name)] = value;
    private void Hide(params string[] names) { foreach (var name in names) Bind(name, "visible", 0); }
    internal void Select(FalloutPipBoyPage page, int index = 0)
    {
        _state.Select(page, index); _offset = 0; _selectedItem = null; Refresh(); PageChanged?.Invoke();
    }
    private void Refresh()
    {
        Error = null;
        SetMeta("opennv_page", (int)_state.Page);
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _buttons.Clear(); _templates.Clear(); _map = null;
        try
        {
            var path = _state.Page switch { FalloutPipBoyPage.Stats => "stats", FalloutPipBoyPage.Items => "inventory", _ => "map" };
            var root = FalloutMenuXml.Expand(FalloutMenuXml.Read($"menus/main/{path}_menu.xml")).Elements("menu").Single();
            root.SetElementValue("systemcolor", "entity_pipboy");
            foreach (var template in root.Descendants("template").ToArray())
            {
                _templates.Add((string)template.Attribute("name")!, new(template.Elements().Single())); template.Remove();
            }
            _tiles = new(root, Setting) { Screen = Size };
            if (_state.Page == FalloutPipBoyPage.Stats) BuildStats();
            else if (_state.Page == FalloutPipBoyPage.Items) BuildItems();
            else BuildData();
            Layout(); _tiles.ValidateDrawing();
        }
        catch (Exception error) { Fail(error); }
        QueueRedraw();
    }
    private void Fail(Exception error)
    {
        Error = error.Message; GD.PushError($"OPENNV_PIPBOY_UNBOUND {Error}");
        SetMeta("opennv_pipboy_error", Error);
    }
    private void Target(XElement tile, string label, Action action)
    {
        var font = _tiles.Font(tile.DescendantsAndSelf("text").FirstOrDefault() ?? tile);
        var button = new NativeBitmapMenuButton(font.Font, font.Atlas, _tiles.TileColor(tile)) { Text = label, DrawText = false };
        button.Pressed += () => { try { action(); } catch (Exception error) { Fail(error); QueueRedraw(); } };
        button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
        button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
        AddChild(button); _buttons.Add((tile, button));
    }
    private void Tabs(string parentName, string[] labels)
    {
        var parent = Tile(parentName); _tiles.Bind(parent, "_CurrentTab", _state.Selection);
        _tiles.Bind(parent, "_NumberOfTabs", labels.Length);
        _tiles.Bind(parent, "_LeftLineLength", 30);
        for (var index = 0; index < labels.Length; index++)
        {
            var tile = new XElement(_templates["TabButtonTemplate"]); tile.SetAttributeValue("name", "PageTab" + index); parent.Add(tile);
            _tiles.Text[tile] = labels[index];
            _tiles.Bind(tile, "_TabIndex", index); _tiles.Bind(tile, "_index", index);
            // These are the native tabline's generated tile traits.
            _tiles.Bind(tile, "x", index * 855f / labels.Length); _tiles.Bind(tile, "y", -18);
            _tiles.Bind(tile, "_x", index * 855f / labels.Length); _tiles.Bind(tile, "_y", -18);
            _tiles.Bind(tile, "width", 855f / labels.Length); _tiles.Bind(tile, "height", 50);
            _tiles.Bind(tile, "_selected", index == _state.Selection ? 1 : 0);
            var selected = index; Target(tile, labels[index], () => Select(_state.Page, selected));
        }
    }
    private IReadOnlyList<XElement> ListRows(XElement list, string template, IReadOnlyList<(string Label, Action Action, bool Equipped)> items)
    {
        var rows = new List<XElement>();
        _tiles.Bind(list, "_scrollbar_vis", 0); _tiles.Bind(list, "_highlight_y", -100); _tiles.Bind(list, "_selected_height", 48);
        foreach (var scrollbar in list.Descendants().Where(tile => (string?)tile.Attribute("name") == "lb_scrollbar")) _tiles.Bind(scrollbar, "visible", 0);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, items.Count - 1));
        var y = 0f;
        var available = _tiles.Number(list, "height");
        foreach (var (item, index) in items.Skip(_offset).Take(8).Select((item, index) => (item, index)))
        {
            var tile = new XElement(_templates[template]); tile.SetAttributeValue("name", "PipBoyRow" + index); list.Add(tile);
            _tiles.Text[tile] = item.Label;
            _tiles.Bind(tile, "listindex", index); _tiles.Bind(tile, "height", 48); _tiles.Bind(tile, "_y", y);
            _tiles.Bind(tile, "y", y); _tiles.Bind(tile, "user0", 0); _tiles.Bind(tile, "user1", -1);
            _tiles.Bind(tile, "_selected", item.Equipped ? 1 : 0);
            foreach (var text in tile.Descendants("text").Where(text => (string?)text.Attribute("name") == "ListItemText")) _tiles.Text[text] = item.Label;
            var height = Math.Max(48, tile.DescendantsAndSelf("text").Select(text => _tiles.Number(text, "height") + 8).DefaultIfEmpty(48).Max());
            if (available > 0 && y > 0 && y + height > available) { tile.Remove(); break; }
            _tiles.Bind(tile, "height", height); y += height;
            foreach (var marker in tile.Descendants().Where(marker => ((string?)marker.Attribute("name"))?.EndsWith("ItemMarker", StringComparison.Ordinal) == true))
                _tiles.Bind(marker, "visible", item.Equipped ? 1 : 0);
            Target(tile, item.Label, item.Action);
            rows.Add(tile);
        }
        return rows;
    }
    private string NameOf(FalloutFormKey form)
    {
        var record = _records.GetEffective(form);
        return FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span);
    }
    private void BuildStats()
    {
        var vitals = _vitals(); var root = _tiles.Root;
        _tiles.Bind(root, "user0", _state.Selection);
        _tiles.BindText(root, "user5", $"{vitals.HitPoints}/{vitals.MaximumHitPoints}");
        _tiles.BindText(root, "user6", $"{vitals.ActionPoints}/{vitals.MaximumActionPoints}");
        _tiles.BindText(root, "user7", $"{vitals.ExperiencePoints}/{vitals.NextLevelExperiencePoints}");
        _tiles.BindText(root, "user8", vitals.Level.ToString(CultureInfo.InvariantCulture));
        Text("stats_title", Setting("sStats")); Text("stats_player_name", _playerName);
        string[] tabs = ["status", "SPECIAL", "skills", "perks", "general"];
        foreach (var (tab, index) in tabs.Select((tab, index) => (tab, index)))
        {
            var tile = Tile("stats_tailline_" + tab); Target(tile, _tiles.String(tile), () => Select(FalloutPipBoyPage.Stats, index));
        }
        Hide("stats_icon", "stats_description_scrollbar");
        Bind("stats_description_rect", "wheelmoved", 0);
        Bind("stats_description_scrollbar", "_current_value", 0);
        if (_state.Selection == 1)
        {
            var values = _actorValue is null ? _special().Values.Select(value => (float)value).ToArray() :
                FalloutNativeVigorResolver.AttributeNames.Select(_actorValue).ToArray();
            var list = Tile("stats_special_container");
            var attributes = FalloutNativeVigorResolver.AttributeNames.Select(name => FalloutDialogueTopic.Find(_records, "AVIF", "AV" + name)).ToArray();
            var rows = ListRows(list, "stats_list_template", attributes.Select((attribute, index) =>
                (NameOf(attribute.FormKey), (Action)(() => { _selectedAttribute = index; Refresh(); }), index == _selectedAttribute)).ToArray());
            for (var index = 0; index < rows.Count; index++)
            {
                _tiles.Bind(rows[index], "user1", values[index + _offset]);
                _tiles.BindText(rows[index], "user1", values[index + _offset].ToString(CultureInfo.InvariantCulture));
            }
            StatDetail(attributes[_selectedAttribute].FormKey);
        }
        else if (_state.Selection == 0)
        {
            Bind("stats_CND_button", "_x", 0); Bind("stats_CND_button", "_y", 0);
            // The source body illustration can be presented without inventing
            // health for unbound limb/effect pools. Their meters stay unbound.
            foreach (var (part, art) in new[] { ("head", "head"), ("face", "face_00"), ("torso", "torso"),
                ("leftarm", "left_arm"), ("rightarm", "right_arm"), ("leftleg", "left_leg"), ("rightleg", "right_leg") })
            {
                _tiles.SetFilename(Tile("stats_player_" + part), $"interface/stats/{art}.dds");
                Bind("stats_player_" + part, "target", 0);
                if (part == "face") continue;
                Hide("stats_player_" + part + "_pct", "stats_player_" + part + "_crippled");
            }
            Hide("stats_stimpak_button", "stats_healing_mode", "stats_drbag_button", "stats_H20_button", "stats_FOD_button", "stats_SLP_button");
            foreach (var (button, label) in new[] { ("CND", "CND"), ("RAD", "RAD"), ("EFF", "EFF") })
                Target(Tile("stats_" + button + "_button"), label, () =>
                {
                    if (button == "CND") return;
                    throw new NotSupportedException($"Player {label} state is not connected yet.");
                });
            AddText("Limb condition: unavailable", new(75, 560), new(740, 40));
        }
        else if (_state.Selection is 2 or 3)
        {
            var forms = _state.Selection == 2 ? _skills.Select(skill => _records.RuntimeFormKey(skill.RuntimeFormId)).ToArray() :
                _traits.Select(trait => _records.RuntimeFormKey(trait.RuntimeFormId)).ToArray();
            _selectedDetail = forms.Contains(_selectedDetail ?? default) ? _selectedDetail : forms.Select(form => (FalloutFormKey?)form).FirstOrDefault();
            var list = Tile(_state.Selection == 2 ? "stats_skills_container" : "stats_perks_container");
            var rows = ListRows(list, "stats_list_template", forms.Select(form =>
                (NameOf(form), (Action)(() => { _selectedDetail = form; Refresh(); }), form == _selectedDetail)).ToArray());
            if (_state.Selection == 2)
                for (var index = 0; index < rows.Count; index++)
                {
                    var value = _actorValue?.Invoke(FalloutPlayerSkills.SkillName(_skills[index + _offset].EditorId));
                    if (value is { } total) _tiles.Bind(rows[index], "user1", total);
                    _tiles.BindText(rows[index], "user1", value?.ToString("0", CultureInfo.InvariantCulture) ?? "--");
                }
            if (_selectedDetail is { } selected) StatDetail(selected);
            else { Hide("stats_icon_separator", "stats_description_rect"); AddText("No perks selected.", new(100, 180), new(720, 90)); }
            if (_state.Selection == 2 && _actorValue is null) AddText("Skill totals: unavailable", new(75, 590), new(740, 40));
        }
        else
        {
            Hide("stats_skills_container", "stats_perks_container", "stats_genrep_container", "stats_icon_separator", "stats_description_rect");
            AddText("This player-state page is not connected yet.", new(100, 200), new(720, 150));
        }
    }
    private void StatDetail(FalloutFormKey form)
    {
        var fields = _records.GetEffective(form).ReadSubrecords().ToArray();
        var icon = fields.SingleOrDefault(field => field.Signature == "ICON").Data;
        if (!icon.IsEmpty) { _tiles.SetFilename(Tile("stats_icon"), FalloutDialogueTopic.Text(icon.Span)); Bind("stats_icon", "visible", 1); }
        Text("stats_description", FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "DESC").Data.Span));
    }
    private void AddText(string value, Vector2 position, Vector2 size)
    {
        var tile = new XElement("text", new XAttribute("name", "RuntimeText" + _tiles.Root.Descendants().Count()),
            new XElement("font", 2), new XElement("string", value), new XElement("x", position.X), new XElement("y", position.Y),
            new XElement("wrapwidth", size.X), new XElement("systemcolor", "entity_pipboy"));
        _tiles.Root.Add(tile);
    }
    private void BuildItems()
    {
        Tabs("IM_Tabline", ["Weapons", "Apparel", "Aid", "Misc", "Ammo"]);
        Hide("IM_HotKeyWheel", "IM_ItemInfoRect", "IM_EquipItemMarker", "IM_RepairButton", "IM_HotkeyButton", "IM_ModButton", "IM_DropButton", "IM_CancelButton");
        var vitals = _vitals();
        _tiles.BindText(Tile("IM_Headline_PlayerHPInfo"), "_Value", $"{vitals.HitPoints}/{vitals.MaximumHitPoints}");
        _tiles.BindText(Tile("IM_Headline_PlayerWGInfo"), "_Value", $"{_state.Items.Sum(item => (item.Weight ?? 0) * item.Count):0.0}");
        Text("IM_Headline_PlayerDRInfo", ""); Text("IM_Headline_PlayerDTInfo", "");
        var caps = _state.Items.Where(item => item.EditorId.Equals("Caps001", StringComparison.OrdinalIgnoreCase)).Sum(item => item.Count);
        _tiles.BindText(Tile("IM_Headline_PlayerCapsInfo"), "_Value", caps.ToString(CultureInfo.InvariantCulture));
        var items = _state.Items.Where(item => FalloutInventoryAccess.CanTransfer(_records.GetEffective(item.FormKey), false))
            .Where(item => _state.Selection switch
            {
                0 => item.RecordType == "WEAP",
                1 => item.RecordType == "ARMO",
                2 => item.RecordType == "ALCH",
                4 => item.RecordType == "AMMO",
                _ => item.RecordType is not ("WEAP" or "ARMO" or "ALCH" or "AMMO")
            })
            .OrderBy(item => NameOf(item.FormKey), StringComparer.CurrentCultureIgnoreCase).ToArray();
        _selectedItem = items.FirstOrDefault(item => item.FormKey == _selectedItem?.FormKey) ?? items.FirstOrDefault();
        ListRows(Tile("IM_InventoryList"), "IM_InventoryListTemplate", items.Select(item =>
            (NameOf(item.FormKey) + (item.Count > 1 ? $" ({item.Count})" : ""), (Action)(() =>
            {
                _selectedItem = item;
                if (item.RecordType is "ARMO" or "WEAP") ToggleEquipment(item);
                Refresh();
            }),
                _inventory.Equipped.Contains(item.RuntimeFormId))).ToArray());
        var equip = Tile("IM_EquipButton");
        _tiles.Bind(equip, "visible", _selectedItem?.RecordType is "ARMO" or "WEAP" ? 1 : 0);
        if (_selectedItem is { } selected)
        {
            var fields = _records.GetEffective(selected.FormKey).ReadSubrecords().ToArray();
            var icon = fields.SingleOrDefault(field => field.Signature == "ICON").Data;
            if (!icon.IsEmpty) { _tiles.SetFilename(Tile("IM_ItemIcon"), FalloutDialogueTopic.Text(icon.Span)); Bind("IM_ItemIcon", "visible", 1); }
            AddText($"{NameOf(selected.FormKey)}\nWG {selected.Weight:0.0}   VAL {selected.Value}", new(470, 440), new(400, 140));
            var equipped = _inventory.Equipped.Contains(selected.RuntimeFormId);
            _tiles.Text[equip] = Setting(equipped ? "sInventoryUnequip" : "sInventoryEquip");
            Target(equip, equipped ? "Unequip" : "Equip", () =>
            {
                ToggleEquipment(selected);
                Refresh();
            });
        }
    }
    private void ToggleEquipment(FalloutCampaignItem item)
    {
        if (_inventory.Equipped.Contains(item.RuntimeFormId)) _inventory.Unequip(_records, item.FormKey);
        else _inventory.Equip(_records, item.FormKey);
    }
    private void BuildData()
    {
        Tabs("MM_Tabline", ["Local Map", "World Map", "Quests", "Notes", "Radio"]);
        Hide("MM_ButtonA", "MM_ButtonX", "MM_ButtonY", "MM_Highlight_ClipWindow");
        Text("MM_Headline_LocationInfo", _world is { } world ? NameOf(world) : ""); Text("MM_Headline_TimeDateInfo", "");
        if (_state.Selection == 1)
        {
            Hide("MM_WorldMap_ParentImage", "MM_WorldMapCursor");
            if (_world is not { } sourceWorld) throw new NotSupportedException("Interior world-map exit routing is not connected yet.");
            var clip = Tile("MM_WorldMap_ClipWindow");
            _map = new(_records, _references, sourceWorld, _player.X, _player.Y, _heading, _tiles.TileColor(clip))
            { Position = _tiles.Position(clip), Size = new(_tiles.Number(clip, "width"), _tiles.Number(clip, "height")) };
            _map.Hovered += name => { Text("MM_Headline_LocationInfo", name.Length == 0 ? NameOf(sourceWorld) : name); QueueRedraw(); };
            AddChild(_map); _map.CenterPlayer();
        }
        else if (_state.Selection == 2)
        {
            Hide("MM_TextScrollbar");
            var quests = _quests.Capture().Where(quest => quest.Objectives?.Any(objective => objective.Displayed) == true)
                .OrderBy(quest => quest.Completed).ThenBy(quest => NameOf(quest.Quest)).ToArray();
            ListRows(Tile("MM_QuestsList"), "MM_ListMarkerTemplate", quests.Select(quest =>
                (NameOf(quest.Quest), (Action)(() => { _quests.ForceActive(quest.Quest); Refresh(); }), quest.Active)).ToArray());
            var active = quests.FirstOrDefault(quest => quest.Active) ?? quests.FirstOrDefault();
            Text("MM_DataText", active is null ? "" : string.Join("\n\n", active.Objectives!.Where(objective => objective.Displayed)
                .Select(objective => (objective.Completed ? "[✓] " : "[ ] ") + _quests.ObjectiveText(active.Quest, objective.Index))));
        }
        else
        {
            Hide("MM_LocalMap_ClipWindow", "MM_NotesList", "MM_RadioStationList", "MM_DataRect", "MM_WaveformRect");
            AddText(_state.Selection == 0 ? "Local map rendering is not connected yet." :
                _state.Selection == 3 ? "Notes playback is not connected yet." : "Radio tuning is not connected yet.", new(100, 200), new(720, 150));
        }
    }
    private bool VisibleTile(XElement tile) => tile.AncestorsAndSelf().All(parent => parent.Attribute("name") is null || _tiles.Number(parent, "visible") != 0);
    private void Layout()
    {
        foreach (var (tile, button) in _buttons)
        {
            button.Visible = VisibleTile(tile); if (!button.Visible) continue;
            button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
        }
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp && _map is null)
        { _offset += mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1; Refresh(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Draw()
    {
        if (Error is null)
        {
            try { _tiles.Draw(this); }
            catch (Exception error) { Fail(error); }
        }
        if (Error is not null)
        {
            var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
            var font = NativeBitmapFontAsset.Read(settings, 2);
            DrawRect(new(45, 90, 880, 160), new Color(0.18f, 0, 0, 1));
            font.Draw(this, new(65, 110), "PIP-BOY: UNBOUND SOURCE", Colors.Red, font.Font.TileBaseline);
            foreach (var (line, index) in Error.Chunk(66).Select((line, index) => (new string(line), index)))
                font.Draw(this, new(65, 150 + index * 30), line, Colors.White, font.Font.TileBaseline);
        }
    }
}
