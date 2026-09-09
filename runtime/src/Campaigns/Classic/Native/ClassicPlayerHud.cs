using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Original owned interface art reading one classic player's live state.</summary>
internal sealed partial class ClassicPlayerHud : Control
{
    private ClassicArtCache _art = null!;
    private ClassicOwnedFont _font = null!;
    private ClassicPlayerSession _player = null!;
    private ClassicWorldPreview _world = null!;
    private ClassicInventoryModels _models = null!;
    private readonly List<string> _history = [];
    private int _historyOffset;
    private const int VisibleHistoryLines = 6;
    internal event Action? FocusRequested;
    internal event Action? MenuRequested;
    internal event Action? InventoryRequested;
    internal int DisplayedHitPoints => _player.HitPoints;
    internal int DisplayedArmorClass => _player.ArmorClass;

    internal void Configure(ClassicArtCache art, ClassicPlayerSession player, ClassicWorldPreview world)
    {
        Name = "ClassicPlayerHud"; _art = art; _player = player; _world = world; Size = new(640, 100);
        TextureFilter = TextureFilterEnum.Nearest;
        _font = new(art.Read("font1.aaf"), art.Read("color.pal"), 992);
        // Source IFACE.FRM positions remain in their original 640x100 canvas.
        Button("PlayerCharacter", "chaup.frm", "chadn.frm", new(526, 59), ShowCharacter, "Character");
        Button("PlayerInventory", "invbutup.frm", "invbutdn.frm", new(211, 41), () => InventoryRequested?.Invoke(), "Inventory");
        Button("PlayerPipBoy", "pipup.frm", "pipdn.frm", new(526, 78), () => Message("Pip-Boy restoration is in progress."), "Pip-Boy");
        Button("PlayerMap", "mapup.frm", "mapdn.frm", new(526, 40), () => FocusRequested?.Invoke(), "Center on player");
        Button("PlayerOptions", "optiup.frm", "optidn.frm", new(210, 62), () => MenuRequested?.Invoke(), "Menu · Escape");
        _models = new ClassicInventoryModels(); AddChild(_models); _models.Configure(_font);
        Message($"{player.Choice.Character.Name}\nClick a hex to walk.\nSpace stops. F5 saves.");
        _player.Changed += Refresh;
        _player.Inventory.Changed += RefreshInventory;
        _world.RepresentationChanged += RefreshInventory;
        GetViewport().SizeChanged += Resize; Resize();
    }

    private void Button(string name, string up, string down, Vector2 at, Action action, string tooltip)
    {
        var button = new TextureButton
        {
            Name = name,
            Position = at,
            TextureNormal = _art.Frame("art/intrface/" + up).Texture,
            TexturePressed = _art.Frame("art/intrface/" + down).Texture,
            TooltipText = tooltip
        };
        AddChild(button); button.Pressed += action;
    }

    private void Resize()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var scale = Math.Min(viewport.X / 640, Math.Max(1, viewport.Y / 480));
        Scale = new(scale, scale); Position = new((viewport.X - 640 * scale) / 2, viewport.Y - 100 * scale);
        RefreshInventory();
    }

    internal void Message(string text)
    {
        _history.AddRange(_font.Wrap(text, 167).Select(line => line.Text));
        if (_history.Count > 1000) _history.RemoveRange(0, _history.Count - 1000);
        _historyOffset = 0; QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } wheel ||
            !new Rect2(23, 24, 167, 68).HasPoint(wheel.Position) ||
            wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
        _historyOffset = Math.Clamp(_historyOffset + (wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1),
            0, Math.Max(0, _history.Count - VisibleHistoryLines));
        AcceptEvent(); QueueRedraw();
    }
    private void Refresh() => QueueRedraw();
    private void RefreshInventory()
    {
        var held = _player.Inventory.Held;
        _models.Display(held is null ? [] : [_world.InventorySlot(held, new(283, 42, 87, 41), _art, _player)], _world.ShowModels, Scale.X);
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_art is null) return;
        DrawTexture(_art.Frame("art/intrface/iface.frm").Texture, Vector2.Zero);
        DrawTexture(_art.Frame("art/intrface/sattkbup.frm").Texture, new(267, 26));
        Number(_player.HitPoints, new(473, 40)); Number(_player.ArmorClass, new(473, 75));
        // Exploration does not spend combat AP. Its combat owner will supply
        // remaining AP when restored; never drain this for ordinary walking.
        var dot = _art.Frame("art/intrface/hlgrn.frm").Texture;
        for (var index = 0; index < Math.Min(10, _player.Stats.ActionPoints); index++) DrawTexture(dot, new(316 + index * 9, 14));
        var historyStart = Math.Max(0, _history.Count - VisibleHistoryLines - _historyOffset);
        _font.Draw(this, string.Join('\n', _history.Skip(historyStart).Take(VisibleHistoryLines)), new(23, 24), 167, VisibleHistoryLines);
        var held = _player.Inventory.Held;
        var status = held is null ? _player.Status : held.Definition.Name +
            (held.Definition.Weapon is { Capacity: > 0 } weapon ? $"\n{held.Object.InstanceValues[0]}/{weapon.Capacity}" : "");
        if (held is not null && !_world.ShowModels && held.Definition.Icon is { } icon)
        {
            var texture = _art.Frame(icon).Texture;
            var scale = Math.Min(87f / texture.GetWidth(), 41f / texture.GetHeight());
            var size = texture.GetSize() * scale;
            DrawTextureRect(texture, new Rect2(new Vector2(283, 42) + (new Vector2(87, 41) - size) / 2, size), false);
        }
        _font.Draw(this, status, held is null ? new Rect2(281, 40, 160, 49) : new Rect2(374, 40, 67, 49),
            HorizontalAlignment.Center, VerticalAlignment.Center, 4);
    }

    private void Number(int value, Vector2 at)
    {
        var sheet = _art.Frame("art/intrface/numbers.frm").Texture;
        DrawTextureRectRegion(sheet, new Rect2(at, new(6, 17)), new Rect2(114, 0, 6, 17));
        var digits = Math.Clamp(value, 0, 999).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < 3; index++) DrawTextureRectRegion(sheet,
            new Rect2(at + new Vector2(6 + index * 9, 0), new(9, 17)), new Rect2((digits[index] - '0') * 9, 0, 9, 17));
    }

    internal void ShowCharacter()
    {
        var panel = new AcceptDialog { Name = "ClassicLiveCharacter", Title = _player.Choice.Character.Name, MinSize = new(460, 400), ProcessMode = ProcessModeEnum.Always };
        var profile = _player.Choice.Character;
        panel.DialogText = $"{profile.Name}  ·  Age {profile.Age}\n\n" + string.Join('\n',
            profile.Special.Select((value, index) => $"{Content.ClassicCharacterProfile.SpecialNames[index]}  {_player.Stats.Special[index]}")) +
            $"\n\nHit points {_player.HitPoints}/{_player.Stats.HitPoints}  ·  Armor class {_player.ArmorClass}\n" +
            $"Action points {_player.Stats.ActionPoints}\n\nTags: " + string.Join(", ", profile.TaggedSkills.Select(value => Content.ClassicCharacterProfile.SkillNames[value])) +
            "\nTraits: " + string.Join(", ", profile.Traits.Select(value => Content.ClassicCharacterProfile.TraitNames[value]));
        var previousPause = GetTree().Paused; GetTree().Paused = true;
        AddChild(panel); panel.TreeExiting += () => GetTree().Paused = previousPause;
        panel.CloseRequested += panel.QueueFree; panel.Confirmed += panel.QueueFree; panel.PopupCentered();
    }

    public override void _ExitTree()
    {
        if (_player is not null) { _player.Changed -= Refresh; _player.Inventory.Changed -= RefreshInventory; }
        if (_world is not null) _world.RepresentationChanged -= RefreshInventory;
        GetViewport().SizeChanged -= Resize;
    }
}
