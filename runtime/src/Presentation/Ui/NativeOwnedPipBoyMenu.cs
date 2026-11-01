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
    private readonly Action<FalloutFormKey>? _useAid;
    private readonly FalloutRadioStations? _radio;
    private long _radioRevision = -1;
    private long _noteRevision = -1;
    private long _pipBoyRevision = -1;
    private readonly string _playerName;
    private readonly IReadOnlyList<FalloutNativeSkillIdentity> _skills, _tags;
    private readonly IReadOnlyList<FalloutNativeTraitIdentity> _traits;
    private FalloutFormKey? _world;
    private Vector3 _player;
    private float _heading;
    private NativeOwnedMenuTree _tiles = null!;
    private readonly List<(XElement Tile, BaseButton Button)> _buttons = [];
    private readonly Dictionary<FalloutPipBoyPage, XElement> _declarations = [];
    private (FalloutFormKey World, NativeOwnedWorldMap.View View)? _mapView;
    internal string? ActionError { get; private set; }
    private readonly Dictionary<string, XElement> _templates = [];
    private readonly Dictionary<string, XElement> _namedTiles = new(StringComparer.Ordinal);
    private double _lastRefreshMilliseconds, _maximumRefreshMilliseconds;
    private NativeOwnedWorldMap? _map;
    private Control? _mapForeground;
    private int _offset;
    private int _statusMode;
    private float _textOffset;
    private long _displayedInventoryRevision = -1;
    private IReadOnlyList<FalloutFormKey> CollectedNotes => _references.PlayerNotes.Notes;
    private FalloutMapMarker? _travelTarget;
    internal Func<FalloutFormKey, string?>? FastTravel { get; init; }
    internal Func<Color, Control>? LocalMap { get; init; }
    internal Func<FalloutIngestiblesSnapshot>? ActiveEffects { get; init; }
    internal Func<string>? DateTimeText { get; init; }
    internal Func<FalloutFormKey?>? CurrentLocation { get; init; }
    internal int SelectedTab => _state.Selection;
    internal float RadiationRads => _vitals().RadiationRads;
    internal Action<FalloutNote>? PlayNote { get; init; }
    private FalloutCampaignItem? _selectedItem;
    private int _selectedVariant;
    private int _selectedAttribute;
    private FalloutFormKey? _selectedDetail;
    private FalloutBodyPartData? _playerBodyParts;
    private (int Current, int Maximum) _displayedHealth;
    internal string? Error { get; private set; }
    internal event Action? PageChanged;
    internal void RefreshWorld(FalloutFormKey? world, Vector3 player, float heading)
    {
        SaveMapView(); _map = null;
        _world = world; _player = player; _heading = heading;
        Select(_state.Page, _state.Selection);
    }
    internal object State => new
    {
        open = _state.Open,
        page = _state.Page.ToString(),
        selection = _state.Selection,
        items = _state.Items.Count,
        limbs = PlayerLimbObservation,
        tags = _tags.Select(tag => tag.RuntimeFormId).ToArray(),
        map = _map?.State,
        localMap = GetChildren().OfType<NativeOwnedLocalMap>().SingleOrDefault()?.State,
        radio = _radio?.State,
        error = Error,
        actionError = ActionError,
        listOffset = _offset,
        refreshMilliseconds = _lastRefreshMilliseconds,
        maximumRefreshMilliseconds = _maximumRefreshMilliseconds,
        selectedNote = _state.SelectedNote?.ToString(),
        unbound = "limb-movement-and-effect-rules,skill-advancement,constant-ability-effect-list,complete-general-statistics,radio-broadcast-playback,fast-travel-time-and-followers,scripted-and-addictive-aid-effects,note-removal-console-output"
    };

    internal NativeOwnedPipBoyMenu(FalloutPluginStack records, FalloutPipBoyState state, FalloutPlayerInventory inventory,
        FalloutQuestState quests, FalloutReferenceWorld references, Func<GameplayVitals> vitals,
        Func<FalloutNativeSpecialState> special, string playerName, IReadOnlyList<FalloutNativeSkillIdentity> skills,
        IReadOnlyList<FalloutNativeSkillIdentity> tags, IReadOnlyList<FalloutNativeTraitIdentity> traits,
        FalloutFormKey? world, Vector3 sourcePlayer, float heading, Func<string, float>? actorValue = null,
        Action<FalloutFormKey>? useAid = null, FalloutRadioStations? radio = null)
    {
        Name = "OwnedPipBoyMenu"; ProcessMode = ProcessModeEnum.Always; Size = new(1280, 960);
        _records = records; _state = state; _inventory = inventory; _quests = quests; _references = references;
        _vitals = vitals; _special = special; _playerName = playerName; _world = world; _player = sourcePlayer; _heading = heading;
        _skills = skills; _tags = tags; _traits = traits;
        _actorValue = actorValue;
        _useAid = useAid;
        _radio = radio;
        _state.AttachNoteSelection(this);
    }
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) _state.RetireNoteSelection(this);
    }
    public override void _Ready() => Refresh();
    public override void _Process(double delta)
    {
        if (_tiles is not null && Error is null && (_noteRevision != _references.PlayerNotes.Revision || _pipBoyRevision != _state.Revision))
        { Refresh(); return; }
        if (_tiles is not null && Error is null && _state.Page == FalloutPipBoyPage.Data && _state.Selection == 4 && _radioRevision != _radio?.Revision)
        { Refresh(); return; }
        if (_tiles is not null && Error is null && _state.Page == FalloutPipBoyPage.Stats && _displayedInventoryRevision != _inventory.Revision)
        { Refresh(); return; }
        if (_tiles is null || Error is not null || _state.Page != FalloutPipBoyPage.Items) return;
        if (_displayedInventoryRevision != _inventory.Revision) { Refresh(); return; }
        var vitals = _vitals();
        if (_displayedHealth == (vitals.HitPoints, vitals.MaximumHitPoints)) return;
        _displayedHealth = (vitals.HitPoints, vitals.MaximumHitPoints);
        _tiles.BindText(Tile("IM_Headline_PlayerHPInfo"), "_Value", $"{vitals.HitPoints}/{vitals.MaximumHitPoints}");
        QueueRedraw();
    }
    private XElement Tile(string name)
    {
        if (_namedTiles.TryGetValue(name, out var tile)) return tile;
        tile = _tiles.Root.DescendantsAndSelf().SingleOrDefault(tile => (string?)tile.Attribute("name") == name)
            ?? throw new InvalidDataException($"Pip-Boy source tile is absent: {name}.");
        _namedTiles.Add(name, tile); return tile;
    }
    private string Setting(string name) => FalloutGameSettingStrings.Read(_records, name);
    private void Bind(string name, string trait, float value) => _tiles.Bind(Tile(name), trait, value);
    private void Text(string name, string value) => _tiles.Text[Tile(name)] = value;
    private void Hide(params string[] names) { foreach (var name in names) Bind(name, "visible", 0); }
    internal void Select(FalloutPipBoyPage page) => Select(page, _state.SelectionFor(page));
    internal void Select(FalloutPipBoyPage page, int index)
    {
        ActionError = null; RemoveMeta("opennv_pipboy_action_error");
        _state.Select(page, index); _offset = 0; _textOffset = 0; _selectedItem = null; _selectedVariant = 0; _inventoryAction = null; _travelTarget = null; Refresh(); PageChanged?.Invoke();
    }
    private void Refresh()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        SaveMapView();
        Error = null;
        RemoveMeta("opennv_pipboy_error");
        _displayedInventoryRevision = _inventory.Revision;
        _noteRevision = _references.PlayerNotes.Revision;
        _pipBoyRevision = _state.Revision;
        SetMeta("opennv_page", (int)_state.Page);
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _buttons.Clear(); _statsAidKeys.Clear(); _templates.Clear(); _namedTiles.Clear(); _map = null; _mapForeground = null;
        try
        {
            var path = _state.Page switch { FalloutPipBoyPage.Stats => "stats", FalloutPipBoyPage.Items => "inventory", _ => "map" };
            if (!_declarations.TryGetValue(_state.Page, out var declaration))
                _declarations.Add(_state.Page, declaration = FalloutMenuXml.Expand(FalloutMenuXml.Read($"menus/main/{path}_menu.xml")).Elements("menu").Single());
            var root = new XElement(declaration);
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
        _lastRefreshMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _maximumRefreshMilliseconds = Math.Max(_maximumRefreshMilliseconds, _lastRefreshMilliseconds);
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
        button.Pressed += () => RunAction(action);
        button.MouseEntered += () => { _tiles.Bind(tile, "mouseover", 1); QueueRedraw(); };
        button.MouseExited += () => { _tiles.Bind(tile, "mouseover", 0); QueueRedraw(); };
        AddChild(button); _buttons.Add((tile, button));
    }
    private void Tabs(string parentName, string[] labels)
    {
        var parent = Tile(parentName); _tiles.Bind(parent, "_CurrentTab", _state.Selection);
        _tiles.Bind(parent, "_NumberOfTabs", labels.Length);
        var tabs = new List<XElement>();
        for (var index = 0; index < labels.Length; index++)
        {
            var tile = new XElement(_templates["TabButtonTemplate"]); tile.SetAttributeValue("name", "PageTab" + index); parent.Add(tile);
            _tiles.Text[tile] = labels[index];
            _tiles.Bind(tile, "_TabIndex", index); _tiles.Bind(tile, "_index", index);
            _tiles.Bind(tile, "listindex", index);
            _tiles.Bind(tile, "_selected", index == _state.Selection ? 1 : 0);
            tabs.Add(tile);
            var selected = index; Target(tile, labels[index], () => Select(_state.Page, selected));
        }
        // Source templates own text extent, padding and vertical placement.
        // The runtime distributes the remaining width between and around tabs.
        var widths = tabs.Select(tile => (int)_tiles.Number(tile, "width")).ToArray();
        var gap = (int)((_tiles.Number(parent, "width") - widths.Sum()) / (tabs.Count + 1));
        _tiles.Bind(parent, "_LeftLineLength", gap);
        var x = gap;
        for (var index = 0; index < tabs.Count; index++)
        {
            _tiles.Bind(tabs[index], "_x", x);
            x += widths[index] + gap;
        }
    }
    private IReadOnlyList<XElement> ListRows(XElement list, string template, IReadOnlyList<(string Label, Action Action, bool Equipped)> items)
    {
        var rows = new List<XElement>();
        _tiles.Bind(list, "_highlight_y", -100); _tiles.Bind(list, "_selected_height", 48);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, items.Count - 1));
        var y = 0f;
        var available = _tiles.Number(list, "height");
        // Retail initializes list height from the authored visible row count.
        // Leaving an empty height at zero clips every Stats row away.
        if (available <= 0)
        {
            var visibleRows = Math.Max(1, _tiles.Number(list, "_number_of_visible_items"));
            available = Math.Min(480, visibleRows * 48);
            _tiles.Bind(list, "height", available);
        }
        _tiles.Bind(list, "_num_filtered", items.Count);
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
        BindListNavigation(list, items.Count, rows.Count);
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
        Text("stats_title", Setting("sStats"));
        Text("stats_player_name", $"{_playerName} - {Setting("sMenuDisplayLevelString")} {vitals.Level.ToString(CultureInfo.InvariantCulture)}");
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
            Bind("stats_status_container", "user0", _statusMode);
            Bind("stats_CND_button", "_x", 0); Bind("stats_CND_button", "_y", 0);
            foreach (var (part, art) in new[] { ("head", "head"), ("face", "face_00"), ("torso", "torso"),
                ("leftarm", "left_arm"), ("rightarm", "right_arm"), ("leftleg", "left_leg"), ("rightleg", "right_leg") })
            {
                _tiles.SetFilename(Tile("stats_player_" + part), $"interface/stats/{art}.dds");
                Bind("stats_player_" + part, "target", 0);
                if (part == "face") continue;
                var sourceParts = PlayerBodyParts.Parts
                    .Where(candidate => PlayerBodyRegion(candidate) == part && candidate.HealthPercent > 0)
                    .ToArray();
                if (sourceParts.Length != 1) continue;
                var bodyPart = sourceParts[0];
                var crippleThreshold = vitals.MaximumHitPoints * bodyPart.HealthPercent / 100.0f;
                if (!float.IsFinite(crippleThreshold) || crippleThreshold <= 0) continue;
                var damage = vitals.LimbDamage?.GetValueOrDefault(bodyPart.Type) ?? 0;
                var remaining = Math.Clamp((crippleThreshold - damage) / crippleThreshold, 0, 1);
                Bind("stats_player_" + part + "_pct_meter", "_Value", remaining);
                Bind("stats_player_" + part + "_crippled", "visible", damage >= crippleThreshold ? 1 : 0);
            }
            Hide("stats_healing_mode", "stats_H20_button", "stats_FOD_button", "stats_SLP_button");
            BindStatsAid();
            foreach (var (button, label) in new[] { ("CND", "CND"), ("RAD", "RAD"), ("EFF", "EFF") })
                Target(Tile("stats_" + button + "_button"), label, () =>
                {
                    _statusMode = button == "CND" ? 0 : button == "RAD" ? 1 : 2;
                    Refresh();
                });
            if (_statusMode == 1)
                AddText($"Radiation: {vitals.RadiationRads:0} RADS\nResistance: {_actorValue?.Invoke("RadResist"):0}%", new(120, 240), new(640, 160));
            else if (_statusMode == 2)
            {
                var effects = ActiveEffects?.Invoke().Effects ?? [];
                AddText(effects.Count == 0 ? "No active effects." : string.Join("\n", effects.Skip(_offset).Take(8)
                    .Select(effect => $"{NameOf(effect.Item)}  {effect.Magnitude:+0.##;-0.##;0}  {Math.Ceiling(effect.Duration - effect.Elapsed):0}s")),
                    new(120, 240), new(680, 350));
            }
        }
        else if (_state.Selection is 2 or 3)
        {
            var forms = _state.Selection == 2 ? _skills.Select(skill => _records.RuntimeFormKey(skill.RuntimeFormId)).ToArray() :
                _traits.Select(trait => _records.RuntimeFormKey(trait.RuntimeFormId))
                    .Concat(_references.AcquiredPerks(_records.RuntimeFormKey(0x14))).Distinct().ToArray();
            _selectedDetail = forms.Contains(_selectedDetail ?? default) ? _selectedDetail : forms.Select(form => (FalloutFormKey?)form).FirstOrDefault();
            var list = Tile(_state.Selection == 2 ? "stats_skills_container" : "stats_perks_container");
            var rows = ListRows(list, "stats_list_template", forms.Select(form =>
                (NameOf(form), (Action)(() => { _selectedDetail = form; Refresh(); }), form == _selectedDetail)).ToArray());
            if (_state.Selection == 2)
                for (var index = 0; index < rows.Count; index++)
                {
                    var value = _actorValue?.Invoke(FalloutPlayerSkills.SkillName(_records, _skills[index + _offset]));
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
            var quests = _quests.Capture();
            var markers = FalloutWorldMap.SourceMarkers(_records);
            AddText($"Level  {vitals.Level}\nExperience  {vitals.ExperiencePoints}\nQuests completed  {quests.Count(quest => quest.Completed)}\n" +
                $"Locations discovered  {markers.Count(marker => _references.MapMarkerState(marker.Marker).CanTravel)}\n" +
                $"Notes collected  {(_references.PlayerNotes.Known ? CollectedNotes.Count.ToString(CultureInfo.InvariantCulture) : "unknown")}\n" +
                $"Items carried  {_state.Items.Sum(item => item.Count)}", new(100, 155), new(720, 400));
        }
    }
    private void StatDetail(FalloutFormKey form)
    {
        var fields = _records.GetEffective(form).ReadSubrecords().ToArray();
        var icon = fields.SingleOrDefault(field => field.Signature == "ICON").Data;
        if (!icon.IsEmpty) { _tiles.SetFilename(Tile("stats_icon"), FalloutDialogueTopic.Text(icon.Span)); Bind("stats_icon", "visible", 1); }
        var description = fields.SingleOrDefault(field => field.Signature == "DESC").Data;
        Text("stats_description", description.IsEmpty ? "" : FalloutDialogueTopic.Text(description.Span));
    }

    private FalloutBodyPartData PlayerBodyParts => _playerBodyParts ??=
        FalloutBodyPartData.Read(_records.GetEffective(_records.RuntimeFormKey(0x1d)));

    private object PlayerLimbObservation
    {
        get
        {
            var vitals = _vitals();
            return PlayerBodyParts.Parts.Select(part =>
            {
                var threshold = vitals.MaximumHitPoints * part.HealthPercent / 100.0f;
                var damage = vitals.LimbDamage?.GetValueOrDefault(part.Type) ?? 0;
                var crippled = threshold > 0 && damage >= threshold;
                return new
                {
                    part.Type,
                    part.Name,
                    part.HealthPercent,
                    threshold,
                    damage,
                    remainingPercent = threshold > 0 ? Math.Clamp((threshold - damage) / threshold, 0, 1) * 100 : (float?)null,
                    crippled
                };
            }).ToArray();
        }
    }

    private static string? PlayerBodyRegion(FalloutBodyPart part)
    {
        var name = Normalize(part.Name);
        var node = Normalize(part.Node);
        if (name.Contains("head", StringComparison.Ordinal) || node.Contains("head", StringComparison.Ordinal)) return "head";
        if (name.Contains("torso", StringComparison.Ordinal) || name.Contains("spine", StringComparison.Ordinal) ||
            node.Contains("spine", StringComparison.Ordinal) || node.Contains("pelvis", StringComparison.Ordinal)) return "torso";
        var left = name.Contains("left", StringComparison.Ordinal) || node.Contains("bip01l", StringComparison.Ordinal) ||
            name.StartsWith("larm", StringComparison.Ordinal) || name.StartsWith("lhand", StringComparison.Ordinal) ||
            name.StartsWith("lleg", StringComparison.Ordinal);
        var right = name.Contains("right", StringComparison.Ordinal) || node.Contains("bip01r", StringComparison.Ordinal) ||
            name.StartsWith("rarm", StringComparison.Ordinal) || name.StartsWith("rhand", StringComparison.Ordinal) ||
            name.StartsWith("rleg", StringComparison.Ordinal);
        var arm = name.Contains("arm", StringComparison.Ordinal) || name.Contains("hand", StringComparison.Ordinal) ||
            node.Contains("arm", StringComparison.Ordinal) || node.Contains("hand", StringComparison.Ordinal) ||
            node.Contains("clavicle", StringComparison.Ordinal);
        var leg = name.Contains("leg", StringComparison.Ordinal) || name.Contains("thigh", StringComparison.Ordinal) ||
            name.Contains("calf", StringComparison.Ordinal) || name.Contains("foot", StringComparison.Ordinal) ||
            node.Contains("thigh", StringComparison.Ordinal) || node.Contains("calf", StringComparison.Ordinal) ||
            node.Contains("foot", StringComparison.Ordinal);
        if (arm && left) return "leftarm";
        if (arm && right) return "rightarm";
        if (leg && left) return "leftleg";
        if (leg && right) return "rightleg";
        return null;

        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
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
        _displayedHealth = (vitals.HitPoints, vitals.MaximumHitPoints);
        _tiles.BindText(Tile("IM_Headline_PlayerWGInfo"), "_Value", $"{_state.Items.Sum(item => _inventory.Instances(item.FormKey).Sum(instance =>
            (item.RecordType == "WEAP" ? FalloutWeaponModifications.Weight(_records, item.FormKey, instance.Extra.WeaponMods, item.Weight ?? 0) : item.Weight ?? 0) * instance.Extra.Count)):0.0}");
        Text("IM_Headline_PlayerDRInfo", ""); Text("IM_Headline_PlayerDTInfo", "");
        var caps = _state.Items.Where(item => item.EditorId.Equals("Caps001", StringComparison.OrdinalIgnoreCase)).Sum(item => item.Count);
        _tiles.BindText(Tile("IM_Headline_PlayerCapsInfo"), "_Value", caps.ToString(CultureInfo.InvariantCulture));
        if (BuildInventoryActionList()) return;
        foreach (var name in new[] { "IM_RepairButton", "IM_ModButton" })
        { Bind(name, "visible", 1); Bind(name, "target", 0); }
        var items = _state.Items.Where(item => _records.QuestObjects.IsQuestObject(item.FormKey) ||
                FalloutInventoryAccess.CanTransfer(_records, _records.GetEffective(item.FormKey), false))
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
        if (_selectedItem is { } chosen) _selectedVariant = Math.Clamp(_selectedVariant, 0, _inventory.Instances(chosen.FormKey).Count - 1);
        ListRows(Tile("IM_InventoryList"), "IM_InventoryListTemplate", items.SelectMany(item => _inventory.Instances(item.FormKey).Select(instance =>
            (InventoryInstanceLabel(item, instance.Extra), (Action)(() =>
            {
                _selectedItem = item;
                _selectedVariant = instance.Variant;
                Refresh();
            }),
                _inventory.Equipped.Contains(item.RuntimeFormId) && instance.Variant == 0))).ToArray());
        var equip = Tile("IM_EquipButton");
        _tiles.Bind(equip, "visible", _selectedItem?.RecordType is "ARMO" or "WEAP" or "ALCH" or "INGR" ? 1 : 0);
        if (_selectedItem is { } selected)
        {
            var fields = _records.GetEffective(selected.FormKey).ReadSubrecords().ToArray();
            var icon = fields.SingleOrDefault(field => field.Signature == "ICON").Data;
            if (!icon.IsEmpty) { _tiles.SetFilename(Tile("IM_ItemIcon"), FalloutDialogueTopic.Text(icon.Span)); Bind("IM_ItemIcon", "visible", 1); }
            var condition = selected.RecordType is "ARMO" or "WEAP"
                ? $"\nCND {ConditionSummary(selected)}"
                : "";
            AddText($"{NameOf(selected.FormKey)}\nWG {selected.Weight:0.0}   VAL {selected.Value}{condition}",
                new(470, 440), new(400, 160));
            if (_records.QuestObjects.IsQuestObject(selected.FormKey))
                AddText("Quest item", new(470, 605), new(400, 40));
            var equipped = _inventory.Equipped.Contains(selected.RuntimeFormId) && _selectedVariant == 0;
            var aid = selected.RecordType is "ALCH" or "INGR";
            _tiles.Text[equip] = Setting(aid ? "sInventoryUse" : equipped ? "sInventoryUnequip" : "sInventoryEquip");
            Target(equip, aid ? "Use" : equipped ? "Unequip" : "Equip", () =>
            {
                if (aid) (_useAid ?? throw new NotSupportedException("Aid use has no gameplay owner."))(selected.FormKey);
                else ToggleEquipment(selected);
                Refresh();
            });
            BindInventoryActions(selected);
        }
    }

    private static string ConditionSummary(FalloutCampaignItem item)
    {
        var variants = item.Variants ?? [new FalloutItemVariant(item.Count)];
        if (variants.Any(variant => variant.Count <= 0) || variants.Sum(variant => variant.Count) != item.Count)
            throw new InvalidDataException("Inventory item condition stacks differ from its count.");
        var groups = variants
            .GroupBy(variant => variant.Condition ?? 1)
            .OrderBy(group => group.Key)
            .ToArray();
        if (groups.Length == 0 || groups.Any(group => !float.IsFinite(group.Key) || group.Key is < 0 or > 1))
            throw new InvalidDataException("Inventory item condition is outside 0..1.");

        static string Percent(float condition) =>
            (condition * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        if (groups.Length == 1)
            return groups[0].Key == 0 ? "0%  BROKEN" : Percent(groups[0].Key);
        var summary = groups.Length > 3
            ? $"{Percent(groups[0].Key)}–{Percent(groups[^1].Key)} across {groups.Length} stacks"
            : string.Join(" / ", groups.Select(group =>
                $"{Percent(group.Key)}{(group.Key == 0 ? " BROKEN" : "")} ×{group.Sum(variant => variant.Count)}"));
        return groups[0].Key == 0 && groups.Length > 3 ? summary + " (includes broken)" : summary;
    }

    private void ToggleEquipment(FalloutCampaignItem item)
    {
        if (_inventory.Equipped.Contains(item.RuntimeFormId) && _selectedVariant == 0) _inventory.Unequip(_records, item.FormKey);
        else
        {
            _inventory.SelectInstance(item.FormKey, _selectedVariant, _inventory.Revision);
            _selectedVariant = 0;
            _inventory.Equip(_records, item.FormKey);
        }
    }
    private void BuildData()
    {
        Tabs("MM_Tabline", [Setting("sLocalMapTabText"), Setting("sWorldMapTabText"), Setting("sQuestsTabText"), Setting("sMiscTabText"), Setting("sCommsTabText")]);
        Hide("MM_ButtonA", "MM_ButtonX", "MM_ButtonY", "MM_Highlight_ClipWindow");
        var locationName = (CurrentLocation?.Invoke() ?? _world) is { } location ? NameOf(location) : "";
        Text("MM_Headline_LocationInfo", locationName); Text("MM_Headline_TimeDateInfo", DateTimeText?.Invoke() ?? "");
        if (_state.Selection == 1)
        {
            Hide("MM_WorldMap_ParentImage", "MM_WorldMapCursor");
            if (_world is not { } sourceWorld) throw new NotSupportedException("Interior world-map exit routing is not connected yet.");
            var clip = Tile("MM_WorldMap_ClipWindow");
            _map = new(_records, _references, sourceWorld, _player.X, _player.Y, _heading, _tiles.TileColor(clip))
            { Position = _tiles.Position(clip), Size = new(_tiles.Number(clip, "width"), _tiles.Number(clip, "height")) };
            _map.Selected += marker => { _travelTarget = marker; Refresh(); };
            if (_travelTarget is { } target)
            {
                Bind("MM_ButtonA", "visible", 1);
                Text("MM_ButtonA", "Travel: " + target.Name);
                Target(Tile("MM_ButtonA"), "Fast travel to " + target.Name,
                    () =>
                    {
                        var refusal = (FastTravel ?? throw new InvalidOperationException("Fast travel is unavailable."))(target.Reference);
                        if (refusal is not null) ShowNotice(refusal);
                    });
            }
            _map.Hovered += name => { Text("MM_Headline_LocationInfo", name.Length == 0 ? locationName : name); QueueRedraw(); };
            AddMapCanvas(_map);
            if (_mapView is { } view && view.World == sourceWorld) _map.RestoreView(view.View);
            else _map.CenterPlayer();
            Bind("MM_ButtonX", "visible", 1); Text("MM_ButtonX", "Zoom out");
            Target(Tile("MM_ButtonX"), "Zoom out", () => _map.ZoomAt(_map.Size / 2, -1));
            Bind("MM_ButtonY", "visible", 1); Text("MM_ButtonY", "Zoom in");
            Target(Tile("MM_ButtonY"), "Zoom in", () => _map.ZoomAt(_map.Size / 2, 1));
        }
        else if (_state.Selection == 0)
        {
            Hide("MM_LocalMap_ClipWindow", "MM_NotesList", "MM_RadioStationList", "MM_DataRect", "MM_WaveformRect");
            var clip = Tile("MM_LocalMap_ClipWindow");
            var map = (LocalMap ?? throw new InvalidOperationException("Local map is unavailable."))(_tiles.TileColor(clip));
            map.Position = _tiles.Position(clip);
            map.Size = new(_tiles.Number(clip, "width"), _tiles.Number(clip, "height"));
            AddMapCanvas(map);
            if (map is NativeOwnedLocalMap localMap)
            {
                if (localMap.Error is { } error)
                    throw new InvalidOperationException($"Local map is unbound: {error}");
                Bind("MM_ButtonX", "visible", 1); Text("MM_ButtonX", "Zoom out");
                Target(Tile("MM_ButtonX"), "Zoom out", () => localMap.Zoom(-1));
                Bind("MM_ButtonY", "visible", 1); Text("MM_ButtonY", "Zoom in");
                Target(Tile("MM_ButtonY"), "Zoom in", () => localMap.Zoom(1));
            }
        }
        else if (_state.Selection == 2)
        {
            Hide("MM_TextScrollbar");
            Bind("MM_DataRect", "visible", 1); Bind("MM_DataRect", "_ItemType", 1);
            Hide("MM_DataTextRect");
            Bind("MM_QuestObjectivesList", "visible", 1);
            var quests = _quests.Capture().Where(quest => quest.Objectives?.Any(objective => objective.Displayed) == true)
                .OrderBy(quest => quest.Completed).ThenBy(quest => NameOf(quest.Quest)).ToArray();
            var questRows = ListRows(Tile("MM_QuestsList"), "MM_ListMarkerTemplate", quests.Select(quest =>
                (NameOf(quest.Quest), (Action)(() => { _textOffset = 0; _quests.ForceActive(quest.Quest); Refresh(); }), quest.Active)).ToArray());
            for (var index = 0; index < questRows.Count; index++)
                _tiles.Bind(questRows[index], "_TextAlpha", quests[index + _offset].Completed ? 128 : 255);
            var active = quests.FirstOrDefault(quest => quest.Active) ?? quests.FirstOrDefault();
            DescribeQuest(active);
        }
        else if (_state.Selection == 4)
        {
            var radio = _radio ?? throw new NotSupportedException("Pip-Boy radio list has no shared station owner.");
            _radioRevision = radio.Revision;
            Hide("MM_LocalMap_ClipWindow", "MM_NotesList", "MM_DataRect", "MM_WaveformRect");
            ListRows(Tile("MM_RadioStationList"), "MM_ListMarkerTemplate", radio.Available
                .OrderBy(station => NameOf(station.Base), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(station => _records.RuntimeFormId(station.Reference)).Select(station =>
                (NameOf(station.Base), (Action)(() =>
                {
                    if (_references.PipBoyRadio.CurrentStation == station.Reference) _references.PipBoyRadio.Off();
                    else radio.SelectPipBoy(station.Reference);
                    Refresh();
                }),
                    _references.PipBoyRadio.CurrentStation == station.Reference)).ToArray());
        }
        else if (_state.Selection == 3) BuildNotes();
        if (_state.Selection == 3)
        {
            var maximum = Math.Max(0, _tiles.Number(Tile("MM_DataText"), "height") - _tiles.Number(Tile("MM_DataTextRect"), "height"));
            _textOffset = Math.Clamp(_textOffset, 0, maximum);
            Bind("MM_TextScrollbar", "_current_value", _textOffset / 20);
        }
    }
    private void AddMapCanvas(Control map)
    {
        // Paint the source background, then the map, then the original menu
        // tiles. Input targets must also remain above the map's drag surface.
        AddChild(map); MoveChild(map, 0);
        var foreground = new Control { Name = "MapMenuForeground", MouseFilter = MouseFilterEnum.Ignore };
        var background = Tile("MM_Background");
        foreground.Draw += () =>
        {
            if (Error is not null) return;
            try { _tiles.Draw(foreground, tile => tile != background); }
            catch (Exception error) { Fail(error); QueueRedraw(); }
        };
        AddChild(foreground); MoveChild(foreground, 1); _mapForeground = foreground;
    }

    private void ShowNotice(string text)
    {
        NativeOwnedMessageMenu? message = null;
        message = new(new(default, "", text, true, [Setting("sOK")]), _records,
            _ => message!.QueueFree(), Fail)
        { Size = Size, MouseFilter = MouseFilterEnum.Stop };
        AddChild(message);
    }
    private void BuildNotes()
    {
        Hide("MM_LocalMap_ClipWindow", "MM_RadioStationList", "MM_WaveformRect", "MM_TextScrollbar");
        if (!_references.PlayerNotes.Known)
        {
            _state.SelectNote(null);
            ListRows(Tile("MM_NotesList"), "MM_ListMarkerTemplate", []);
            Bind("MM_DataRect", "visible", 1);
            Bind("MM_DataRect", "_ItemType", 1);
            Text("MM_DataText", "Collected-note history is unavailable in this save.");
            return;
        }
        var notes = CollectedNotes.Select(form => FalloutNote.Read(_records, form))
            .OrderBy(note => note.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var selected = notes.FirstOrDefault(note => note.Record.FormKey == _state.SelectedNote);
        if (!_state.NoteSelectionInitialized)
        {
            selected = notes.FirstOrDefault();
            _state.SelectNote(selected?.Record.FormKey);
        }
        Bind("MM_DataRect", "visible", 1);
        Bind("MM_DataRect", "_ItemType", selected?.Kind == FalloutNoteKind.Image ? 2 : 1);
        ListRows(Tile("MM_NotesList"), "MM_ListMarkerTemplate", notes.Select(note =>
            (note.Name, (Action)(() => { _state.SelectNote(note.Record.FormKey); _textOffset = 0; Refresh(); }), note.Record.FormKey == _state.SelectedNote)).ToArray());
        Text("MM_DataText", selected?.Kind == FalloutNoteKind.Text ? selected.RequireText() : selected?.Name ?? (notes.Length == 0 ? "No notes collected." : ""));
        if (selected?.Kind == FalloutNoteKind.Image && selected.Texture is { } texture)
        {
            _tiles.SetFilename(Tile("MM_DataImage"), texture);
        }
        else if (selected?.Kind is FalloutNoteKind.Sound or FalloutNoteKind.Voice)
        {
            Bind("MM_ButtonA", "visible", 1); Text("MM_ButtonA", "Play");
            Target(Tile("MM_ButtonA"), "Play note", () => (PlayNote ?? throw new InvalidOperationException("Note audio is unavailable."))(selected));
        }
    }
    private bool VisibleTile(XElement tile) => tile.AncestorsAndSelf().All(parent => parent.Attribute("name") is null || _tiles.Number(parent, "visible") != 0);
    private void Layout()
    {
        foreach (var (tile, button) in _buttons)
        {
            button.Visible = VisibleTile(tile); if (!button.Visible) continue;
            button.Position = _tiles.Position(tile); button.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
            button.Visible = button.Size.X > 0 && button.Size.Y > 0;
        }
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Left or Key.Right } tabKey)
        {
            var step = tabKey.PhysicalKeycode == Key.Right ? 1 : -1;
            Select(_state.Page, (_state.Selection + step + 5) % 5);
            GetViewport().SetInputAsHandled(); return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F1 or Key.F2 or Key.F3 } key)
        {
            Select((FalloutPipBoyPage)(key.PhysicalKeycode - Key.F1));
            GetViewport().SetInputAsHandled(); return;
        }
        // A failed page can still switch tabs or close through its device.
        // Do not interpret scrolling against a partially constructed tile tree.
        if (_tiles is null || Error is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false } aidKey && HandleStatsAidKey(aidKey))
        {
            GetViewport().SetInputAsHandled(); return;
        }
        if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp && _map is null && !(_state.Page == FalloutPipBoyPage.Data && _state.Selection == 0))
        {
            var step = mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
            if (_state.Page == FalloutPipBoyPage.Data && _state.Selection is 2 or 3 &&
                new Rect2(_tiles.Position(Tile("MM_DataTextRect")), new(_tiles.Number(Tile("MM_DataTextRect"), "width"),
                    _tiles.Number(Tile("MM_DataTextRect"), "height"))).HasPoint(GetLocalMousePosition()))
                _textOffset += step * 40;
            else _offset += step;
            Refresh(); GetViewport().SetInputAsHandled();
        }
    }
    public override void _Draw()
    {
        if (Error is null)
        {
            try
            {
                if (_mapForeground is null) _tiles.Draw(this);
                else
                {
                    var background = Tile("MM_Background");
                    _tiles.Draw(this, tile => tile == background);
                    _mapForeground.QueueRedraw();
                }
            }
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
