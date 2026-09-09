using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Classic.Native;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Browse the installation's maps while campaign execution is being restored.</summary>
internal sealed partial class ClassicWorldBrowser : Node
{
    private ClassicMapCatalog _source = null!;
    private string _campaign = "fallout-1";
    private ClassicRetailRandomContract? _initializationContract;
    private IReadOnlyList<string> _maps = [];
    private ClassicWorldPreview? _world;
    private OptionButton _map = null!, _elevation = null!;
    private Label _error = null!;
    private string _selectedMap = "";
    private string _savePath = "";
    private string? _appearanceRoot;
    private ClassicOwnedWorldAssets? _worldAssets;
    private ClassicAuthoredScenery? _authoredScenery;
    private ClassicCharacterDraft? _choice;
    private Button _start = null!, _continue = null!, _browse = null!, _choose = null!, _studio = null!;
    private Control _inspection = null!;
    private Control _panel = null!;
    private Button _resume = null!, _save = null!;
    private bool _menuOpen;
    internal ClassicPlayerController? PlayerController { get; private set; }

    internal void Configure(IFalloutClassicOwnedSource source, string savePath, string? appearanceRoot, string campaign = "fallout-1",
        string? fallout3WorldRoot = null)
    {
        _campaign = campaign; _source = new ClassicMapCatalog(source, campaign);
        if (campaign == "fallout-2")
        {
            using var contract = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://config/classic-retail-random-fo2-1.02-v1.json"));
            _initializationContract = ClassicRetailRandomContract.Parse(contract.RootElement);
        }
        _savePath = ClassicPlayerSession.SavePathFor(savePath); _appearanceRoot = appearanceRoot;
        _worldAssets = string.IsNullOrWhiteSpace(appearanceRoot) && string.IsNullOrWhiteSpace(fallout3WorldRoot)
            ? null : new ClassicOwnedWorldAssets(appearanceRoot, fallout3WorldRoot);
        _authoredScenery = new ClassicAuthoredScenery();
        _maps = _source.Maps;
        if (_maps.Count == 0) throw new InvalidDataException("The owned installation contains no maps.");
        var ui = new CanvasLayer(); AddChild(ui);
        var panel = new PanelContainer { Position = new(20, 155) }; _panel = panel; ui.AddChild(panel);
        var column = new VBoxContainer(); panel.AddChild(column);
        var row = new HBoxContainer(); _inspection = row; column.AddChild(row);
        _map = new OptionButton { Name = "OwnedMapChoice", CustomMinimumSize = new(230, 34) };
        foreach (var map in _maps) _map.AddItem(_source.DisplayName(map));
        row.AddChild(_map);
        _elevation = new OptionButton { Name = "OwnedElevationChoice" }; row.AddChild(_elevation);
        var frame = new Button { Text = "View whole level", TooltipText = "Frame this map floor and its scenery." };
        row.AddChild(frame); frame.Pressed += () => _world?.FrameLevel();
        var character = _studio = new Button
        {
            Text = "Reflectron 2.0",
            Name = "ClassicCharacterStudio",
            Disabled = string.IsNullOrWhiteSpace(appearanceRoot),
            TooltipText = string.IsNullOrWhiteSpace(appearanceRoot) ? "Register the owned New Vegas installation to use its live character renderer." : "Create and save a custom character appearance."
        };
        row.AddChild(character);
        var choose = _choose = new Button { Name = "ClassicCharacterSelection", Text = "Choose character" }; row.AddChild(choose);
        choose.Pressed += () =>
        {
            var picker = new ClassicCharacterPicker(); AddChild(picker);
            picker.Selected += choice => { _choice = choice; RefreshActions(); };
            try { picker.Configure(_campaign, source.ProfileId, path => _source.Read(path, out _), savePath, appearanceRoot); }
            catch (Exception error) { RemoveChild(picker); picker.Free(); _error.Text = error.Message; _error.Visible = true; }
        };
        character.Pressed += () =>
        {
            var studio = new ClassicCharacterStudio(); AddChild(studio);
            try { studio.Configure(_campaign, source.ProfileId, appearanceRoot!, savePath); }
            catch (Exception error) { RemoveChild(studio); studio.Free(); _error.Text = error.Message; _error.Visible = true; }
        };
        _error = new Label
        {
            Modulate = new Color("ffb58e"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(430, 0),
            Visible = false
        };
        column.AddChild(_error);
        var actions = new HBoxContainer(); column.AddChild(actions);
        _resume = new Button { Text = "Resume", Visible = false }; actions.AddChild(_resume);
        _save = new Button { Text = "Save", Visible = false }; actions.AddChild(_save);
        _resume.Pressed += () => SetMenuOpen(false);
        _save.Pressed += () => PlayerController?.Save();
        _start = new Button { Name = "ClassicEnterWorld", Text = "Enter cave" }; actions.AddChild(_start);
        _continue = new Button { Name = "ClassicContinue", Text = "Continue" }; actions.AddChild(_continue);
        _browse = new Button { Name = "ClassicReturnToBrowser", Text = "Save and return to map browser", Visible = false }; actions.AddChild(_browse);
        _start.Pressed += () => Try(() =>
        {
            if (File.Exists(_savePath)) throw new InvalidOperationException("A game already occupies this save slot. Use Continue.");
            if (_choice is null) throw new InvalidOperationException("Choose and save a character first.");
            Enter(ClassicPlayerSession.Begin(_source, _choice, _initializationContract));
            PlayerController!.Player.Save(_savePath); RefreshActions();
        });
        _continue.Pressed += () => Try(() => Enter(ClassicPlayerSession.Restore(_source, _savePath, _initializationContract)));
        _browse.Pressed += () => Try(() =>
        {
            PlayerController!.Player.Save(_savePath);
            _world!.RemoveChild(PlayerController); PlayerController.Free(); PlayerController = null;
            _menuOpen = false;
            Select(_selectedMap, _elevation.GetSelectedId()); RefreshActions();
        });
        _map.ItemSelected += index => Select(_maps[(int)index]);
        _elevation.ItemSelected += index => Select(_selectedMap, _elevation.GetItemId((int)index));
        Select(_source.StartingMap);
        if (File.Exists(savePath + ".character.json"))
            Try(() => _choice = ClassicCharacterDraft.Read(savePath + ".character.json", _campaign, source.ProfileId,
                ClassicPremadeReader.Load(_campaign, path => _source.Read(path, out _))));
        RefreshActions();
        if (DisplayServer.GetName() != "headless" && File.Exists(_savePath))
            Try(() => Enter(ClassicPlayerSession.Restore(_source, _savePath, _initializationContract)));
    }

    private void RefreshActions()
    {
        if (_start is null) return;
        var active = PlayerController is not null;
        _inspection.Visible = !active; _start.Visible = !active; _continue.Visible = !active;
        _panel.Visible = !active || _menuOpen || _error.Visible;
        _resume.Visible = active; _save.Visible = active;
        _start.Disabled = active || _choice is null || File.Exists(_savePath);
        _start.Text = _choice is null ? "Choose character to begin" : $"Begin · {_choice.Character.Name}";
        _continue.Disabled = active || !File.Exists(_savePath); _browse.Visible = active;
        _choose.Disabled = active; _studio.Disabled = active || string.IsNullOrWhiteSpace(_appearanceRoot);
        _map.Disabled = active; _elevation.Disabled = active;
    }

    private void Enter(ClassicPlayerSession player)
    {
        if (PlayerController is not null) throw new InvalidOperationException("A classic player is already active.");
        Select(player.MapPath, player.Elevation);
        if (_selectedMap != player.MapPath || _world!.GetMeta("source_elevation").AsInt32() != player.Elevation)
            throw new InvalidOperationException("Player map or elevation could not be presented.");
        var controller = new ClassicPlayerController(); _world!.AddChild(controller);
        try { controller.Configure(_source, _world, player, _savePath, _appearanceRoot); }
        catch { _world.RemoveChild(controller); controller.Free(); throw; }
        PlayerController = controller; _menuOpen = false; RefreshActions();
        controller.ExitRequested += exit => Callable.From(() => Try(() => Transition(exit))).CallDeferred();
        controller.MenuRequested += () => SetMenuOpen(true);
        GD.Print($"OPENNV_CLASSIC_PLAYER_ENTER name={player.Choice.Character.Name} map={player.MapPath} tile={player.Tile} " +
              $"rotation={player.Rotation} appearance={player.Choice.Appearance is not null} " +
              $"scripts={(player.Initialization is null ? "unbound" : "campaign-start-header")} combat=unbound");
    }

    private void Try(Action action)
    {
        try { action(); _error.Visible = false; RefreshActions(); }
        catch (Exception error)
        { _error.Text = error.Message; _error.Visible = true; _panel.Visible = true; GD.PushError($"OPENNV_CLASSIC_PLAYER_UNBOUND {error}"); }
    }

    private void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        if (_world is not null) _world.ProcessMode = open ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        RefreshActions();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_menuOpen && input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        { SetMenuOpen(false); GetViewport().SetInputAsHandled(); }
    }

    private void Transition(ClassicMapExit exit)
    {
        if (PlayerController is null) return;
        var player = PlayerController.Player;
        var next = ClassicWorldPreview.Build(this, _source, exit.MapPath, exit.Elevation,
            sharedAssets: _worldAssets, sharedAuthored: _authoredScenery);
        ClassicPlayerController? controller = null;
        var previous = new ClassicMapExit(player.MapPath, player.Elevation, player.Tile, player.Rotation);
        try
        {
            player.ChangeLevel(exit.MapPath, exit.Elevation, exit.Tile, exit.Rotation);
            controller = new ClassicPlayerController(); next.AddChild(controller);
            controller.Configure(_source, next, player, _savePath, _appearanceRoot);
            controller.ExitRequested += destination => Callable.From(() => Try(() => Transition(destination))).CallDeferred();
            controller.MenuRequested += () => SetMenuOpen(true);
            player.Save(_savePath);
        }
        catch
        {
            if (controller is not null) { next.RemoveChild(controller); controller.Free(); }
            player.ChangeLevel(previous.MapPath, previous.Elevation, previous.Tile, previous.Rotation);
            RemoveChild(next); next.Free(); _world!.Camera.Current = true; throw;
        }
        RemoveChild(_world!); _world!.Free(); _world = next; PlayerController = controller;
        BindChrome(next);
        _selectedMap = player.MapPath;
        _map.Select(_maps.ToList().IndexOf(_selectedMap));
        _elevation.Clear();
        foreach (var level in player.Level.Map.Elevations.Keys.Order()) _elevation.AddItem(_source.ElevationName(player.MapPath, level), level);
        _elevation.Select(_elevation.GetItemIndex(player.Elevation));
        RefreshActions();
    }

    public override void _ExitTree() { _worldAssets?.Dispose(); _authoredScenery?.Dispose(); _source?.Dispose(); }

    private void BindChrome(ClassicWorldPreview world) =>
        world.PresentationChromeChanged += visible => _panel.Visible = visible && (PlayerController is null || _menuOpen || _error.Visible);

    internal void Select(string path, int? elevation = null)
    {
        // Build first, preserving the current view if an unsupported source layout is found.
        try
        {
            path = ClassicMapCatalog.Canonical(path);
            var map = _source.Load(path).Map;
            var next = ClassicWorldPreview.Build(this, _source, path, elevation,
                sharedAssets: _worldAssets, sharedAuthored: _authoredScenery);
            if (_world is not null) { RemoveChild(_world); _world.Free(); }
            _world = next;
            BindChrome(next);
            _selectedMap = path;
            _map.Select(_maps.ToList().FindIndex(candidate => candidate.Replace('\\', '/').Equals(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)));
            _elevation.Clear();
            foreach (var level in map.Elevations.Keys.Order()) _elevation.AddItem(_source.ElevationName(path, level), level);
            _elevation.Select(_elevation.GetItemIndex(elevation ?? map.EnteringElevation));
            _error.Visible = false;
        }
        catch (Exception error)
        {
            _error.Text = $"{path}: {error.Message}"; _error.Visible = true;
            GD.PushError($"OPENNV_FO1_WORLD_UNBOUND map={path} {error}");
            if (_world is null) throw;
        }
    }
}
