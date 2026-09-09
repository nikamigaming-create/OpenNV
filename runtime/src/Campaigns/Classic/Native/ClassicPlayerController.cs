using OpenNV.Runtime.Campaigns.Classic.Native;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Fallout1.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Ordinary pointer/keyboard adapter and player presentation; state lives in ClassicPlayerSession.</summary>
internal sealed partial class ClassicPlayerController : Node3D
{
    private ClassicWorldPreview _world = null!;
    private ClassicArtCache _art = null!;
    private ClassicPlayerAnimation _animation = null!;
    private ClassicPlayerBody? _body;
    private Sprite3D? _sprite;
    private Node3D _position = null!;
    private MeshInstance3D _hover = null!;
    private MeshInstance3D _selection = null!;
    private string _savePath = "";
    private bool _saveRequested;
    private bool _menuRequested;
    private ClassicWorldPick? _pendingLoot;
    internal ClassicPlayerSession Player { get; private set; } = null!;
    internal ClassicPlayerHud Hud { get; private set; } = null!;
    internal Vector3 PlayerPosition => _position.Position;
    internal ClassicPlayerBody? Body => _body;
    internal event Action<ClassicMapExit>? ExitRequested;
    internal event Action? MenuRequested;

    internal void Configure(IFalloutClassicOwnedSource source, ClassicWorldPreview world, ClassicPlayerSession player,
        string savePath, string? appearanceRoot)
    {
        Name = "ClassicPlayerController"; _world = world; Player = player; _savePath = savePath;
        _art = new(path => source.Read(path, out _)); _animation = new(path => source.Read(path, out _), player.Choice.Character.Female, player.Choice.Campaign, player.Inventory);
        _position = new Node3D { Name = "ClassicPlayerPosition" }; AddChild(_position);
        _sprite = new Sprite3D
        {
            Name = "ClassicOriginalPlayerArt",
            PixelSize = 1 / world.Policy.PixelsPerMeter,
            Layers = ClassicWorldPreview.SpriteLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest
        };
        _position.AddChild(_sprite);
        try
        {
            _body = world.PlayerAnalog(_animation.IdlePath, player.Choice, player.Inventory.Held);
            if (_body is not null) { _position.AddChild(_body); world.RegisterPair(_sprite, _body); }
            else if (player.Choice.Appearance is not null) throw new NotSupportedException("Custom player body has no source-art family binding.");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
        {
            if (player.Choice.Appearance is not null) throw;
            world.ReportPresentationFailure("player humanoid: " + error.Message);
        }
        world.PlayerModelMissing(_body is null);
        _hover = new MeshInstance3D
        {
            Name = "ClassicMoveTarget",
            Visible = false,
            Mesh = ClassicHexBlockout.HexRing(0.96f, 0.86f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 1, 0.3f, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        AddChild(_hover);
        _selection = new MeshInstance3D
        {
            Name = "ClassicPlayerHex",
            Mesh = ClassicHexBlockout.HexRing(0.96f, 0.90f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.96f, 0.75f, 0.15f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            }
        };
        AddChild(_selection);
        var layer = new CanvasLayer { Layer = 5 }; AddChild(layer); Hud = new ClassicPlayerHud(); layer.AddChild(Hud); Hud.Configure(_art, player, world);
        Hud.FocusRequested += Focus; Hud.MenuRequested += Menu; player.Arrived += Arrived;
        Hud.InventoryRequested += () => OpenInventory();
        player.Inventory.Changed += RefreshItems; player.Inventory.EquipmentChanged += RefreshEquipment; RefreshItems();
        player.Doors.FrameChanged += RefreshDoor;
        foreach (var serial in player.Doors.Serials) RefreshDoor(serial);
        foreach (var failure in player.Doors.Failures) _world.ReportPresentationFailure("Source door event: " + failure);
        world.Caption.Text = $"FALLOUT / {world.MapName} / {player.Choice.Character.Name}\n" +
            "Click floor: walk · Shift-click door: examine · Space: stop · F5: save · C: character · I: inventory · Home: player\n" +
            "Combat, quests and Pip-Boy are being restored.";
        world.Caption.Visible = false;
        world.ApplyCampaignInitialization(player);
        _animation.Tick(player, 0); Publish(); Focus();
    }

    private void Focus()
    {
        _world.Camera.StopShot();
        _world.Camera.SubjectHeight = 1.875f;
        _world.Camera.Focus = Fo1HexMath.Center(Player.Tile) + Vector3.Up * 0.7f;
    }
    private void Arrived()
    {
        if (_pendingLoot is { } host) { _pendingLoot = null; Loot(host); return; }
        if (_menuRequested) { _menuRequested = false; MenuRequested?.Invoke(); return; }
        try
        {
            if (Player.ExitAtPlayer() is { } exit) { ExitRequested?.Invoke(exit); return; }
        }
        catch (Exception error) when (error is InvalidDataException or FileNotFoundException or NotSupportedException)
        { Hud.Message(error.Message); }
        if (_saveRequested) Save();
    }

    private void RefreshItems()
    {
        try { _world.RefreshItems(Player, _art); }
        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
        { _world.ReportPresentationFailure("World item presentation: " + error.Message); }
    }

    private void RefreshDoor(int serial)
    {
        try { _world.RefreshDoor(Player, _art, serial); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
        { _world.ReportPresentationFailure($"Door {serial}: {error.Message}"); }
    }

    private void RefreshEquipment()
    {
        if (_sprite is null) return;
        if (_body is not null) { _position.RemoveChild(_body); _body.QueueFree(); _body = null; }
        _world.RemovePair(_sprite);
        try
        {
            _animation = new(path => _art.Read(path), Player.Choice.Character.Female, Player.Choice.Campaign, Player.Inventory);
            _animation.Tick(Player, 0);
            _body = _world.PlayerAnalog(_animation.IdlePath, Player.Choice, Player.Inventory.Held);
            if (_body is not null) { _position.AddChild(_body); _world.RegisterPair(_sprite, _body); }
            else _world.ReportPresentationFailure($"Player equipment has no 3D binding: {_animation.IdlePath}; pid={Player.Inventory.Held?.Object.Pid}");
        }
        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
        { _world.ReportPresentationFailure("Player equipment: " + error.Message); Hud.Message(error.Message); }
        _world.PlayerModelMissing(_body is null); Publish();
    }

    private void OpenInventory(int? host = null, string? containerId = null)
    {
        try
        {
            if (host is { } serial) Player.Inventory.CheckAccess(serial);
            if (containerId is not null) Player.Inventory.CheckContainerAccess(containerId);
            var layer = new CanvasLayer { Layer = 8, ProcessMode = ProcessModeEnum.Always }; AddChild(layer);
            var screen = new ClassicInventoryScreen(); layer.AddChild(screen);
            try { screen.Configure(_art, Player, _world, host, containerId); screen.TreeExiting += layer.QueueFree; }
            catch { layer.QueueFree(); throw; }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { Hud.Message(error.Message); }
    }

    private void Loot(ClassicWorldPick target)
    {
        try
        {
            if (target.Serial is { } door && ClassicDoorWorld.IsDoor(Player.Level.Objects.TopLevelObjects.Single(row => row.Serial == door)))
            {
                Player.Doors.Use(door);
                if (!PublishMessages()) Hud.Message(Player.Doors.Pose(door).Direction switch { 1 => "Opening door.", -1 => "Closing door.", _ => "The door did not move." });
                return;
            }
            if (target.ItemId is { } id)
            {
                var item = Player.Inventory.GroundItems.Single(row => row.Id == id);
                if (item.Definition.Subtype == 1) { OpenInventory(containerId: id); return; }
                Player.Inventory.TakeGround(id); Hud.Message($"Taken: {item.Definition.Name}\n{item.Amount}");
            }
            else OpenInventory(target.Serial);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException) { Hud.Message(error.Message); }
    }

    private void Interact(ClassicWorldPick target)
    {
        var tile = target.ItemId is { } id ? Player.Inventory.GroundItems.Single(row => row.Id == id).Location.Tile!.Value : Player.Level.Objects.TopLevelObjects.Single(row => row.Serial == target.Serial).Tile;
        if (!Player.Moving && ClassicHexGrid.Distance(Player.Tile, tile) <= 1) { Loot(target); return; }
        var from = Player.Moving ? Player.NextTile : Player.Tile;
        var paths = ClassicHexGrid.Neighbors(tile).Append(tile).Where(Player.Walkable.Contains)
            .Select(tile => (Tile: tile, Path: ClassicHexGrid.Path(from, tile, Player.Walkable)))
            .Where(row => row.Path.Length > 0 || row.Tile == from).OrderBy(row => row.Path.Length).ToArray();
        if (paths.Length == 0) { Hud.Message("That object cannot be reached."); return; }
        _pendingLoot = target; Player.RequestMove(paths[0].Tile);
        if (!Player.Moving) { _pendingLoot = null; Loot(target); }
    }
    private void Menu()
    {
        if (Player.Moving) { _menuRequested = true; Player.Stop(); return; }
        MenuRequested?.Invoke();
    }
    internal void Save()
    {
        if (Player.Moving) { _pendingLoot = null; _saveRequested = true; Player.Stop(); Hud.Message("Stopping at next hex to save."); return; }
        try { Player.Save(_savePath); _saveRequested = false; Hud.Message("Game saved.\n" + Player.Choice.Character.Name); }
        catch (Exception error) { _saveRequested = false; Hud.Message("Save failed. " + error.Message); GD.PushError($"OPENNV_CLASSIC_SAVE_FAILED {error}"); }
    }

    public override void _Process(double delta)
    {
        if (Player is null) return;
        Player.Doors.Advance(delta);
        PublishMessages();
        _animation.Tick(Player, delta); Publish();
    }

    private bool PublishMessages()
    {
        var messages = Player.Doors.TakeMessages();
        foreach (var message in messages) Hud.Message(message);
        return messages.Length != 0;
    }

    private void Publish()
    {
        var start = Fo1HexMath.Center(Player.Tile); var end = Fo1HexMath.Center(Player.NextTile);
        _position.Position = start.Lerp(end, (float)Player.StepFraction) + Vector3.Up * 0.02f;
        _world.RevealPlayer(_position.Position);
        _selection.Position = start + Vector3.Up * 0.04f;
        var neighbor = Fo1HexMath.TileInDirection(Player.Tile, Player.Rotation);
        var facing = neighbor < 0 ? Vector3.Forward : Fo1HexMath.Center(neighbor) - start;
        _body?.Publish(_animation.Moving, _animation.Phase, facing);
        if (_sprite is not null)
        {
            // Orient source sprites in screen space even when the user orbits
            // the 3D map. Stored source rotation still owns the gameplay path.
            var projected = _world.Camera.UnprojectPosition(_position.Position + facing) - _world.Camera.UnprojectPosition(_position.Position);
            Vector2[] directions = [new(16, -12), new(32, 0), new(16, 12), new(-16, 12), new(-32, 0), new(-16, -12)];
            var rotation = Enumerable.Range(0, 6).MaxBy(index => directions[index].Normalized().Dot(projected.Normalized()));
            var image = _art.Frame(_animation.Moving ? _animation.WalkPath : _animation.IdlePath, rotation, _animation.FrameIndex);
            _sprite.Texture = image.Texture;
            _sprite.Offset = new(image.Frame.DirectionX, image.Frame.Height / 2f - image.Frame.DirectionY);
        }
    }

    private int PointerTile(Vector2 point)
    {
        var origin = _world.Camera.ProjectRayOrigin(point); var ray = _world.Camera.ProjectRayNormal(point);
        if (ray.Y >= -0.00001f) return -1;
        var distance = -origin.Y / ray.Y;
        return distance < 0 ? -1 : Fo1HexMath.NearestTile(origin + ray * distance);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (Player is null) return;
        if (input is InputEventMouseMotion motion)
        {
            var tile = PointerTile(motion.Position); _hover.Visible = Player.Walkable.Contains(tile);
            if (_hover.Visible) _hover.Position = Fo1HexMath.Center(tile) + Vector3.Up * 0.025f;
        }
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var tile = PointerTile(click.Position);
            var picked = ClassicWorldPicking.Item(_world, Player, click.Position);
            if (picked is null && !_world.ShowModels)
            {
                var ground = Player.Inventory.GroundItems.FirstOrDefault(row => row.Location.Tile == tile);
                if (ground is not null) picked = new(ItemId: ground.Id);
                else
                {
                    var host = Player.Level.Objects.TopLevelObjects.FirstOrDefault(row => row.Tile == tile && row.Elevation == Player.Elevation &&
                        (ClassicInventory.IsLootHost(row) || ClassicDoorWorld.IsDoor(row)) && (row.Flags & 1) == 0 && !Player.Inventory.Taken(Player.MapPath, row.Serial));
                    if (host is not null) picked = new(Serial: host.Serial);
                }
            }
            if (click.ShiftPressed && picked?.Serial is { } door && ClassicDoorWorld.IsDoor(Player.Level.Objects.TopLevelObjects.Single(row => row.Serial == door)))
            {
                try { Player.Doors.Examine(door); PublishMessages(); }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException) { Hud.Message(error.Message); }
            }
            else if (picked is not null) Interact(picked);
            else { _pendingLoot = null; Player.RequestMove(tile); }
            GetViewport().SetInputAsHandled();
        }
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Space: _pendingLoot = null; Player.Stop(); break;
            case Key.F5: Save(); break;
            case Key.Home: Focus(); break;
            case Key.C: Hud.ShowCharacter(); break;
            case Key.I: OpenInventory(); break;
            case Key.B: Player.Inventory.SwitchHand(); break;
            case Key.R:
                try
                {
                    var held = Player.Inventory.Held ?? throw new InvalidOperationException("Equip a weapon first.");
                    Hud.Message($"Reloaded {Player.Inventory.Reload(held.Id)} rounds.");
                }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { Hud.Message(error.Message); }
                break;
            case Key.Escape: Menu(); break;
            case Key.F1: _world.Caption.Visible = !_world.Caption.Visible; break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    { if (Player is not null) { Player.Arrived -= Arrived; Player.Inventory.Changed -= RefreshItems; Player.Inventory.EquipmentChanged -= RefreshEquipment; Player.Doors.FrameChanged -= RefreshDoor; } }
}
