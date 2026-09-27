using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>A transient view of the shared inventory. Selection never mutates item state.</summary>
internal sealed partial class NativeCombatWheel : Control
{
    private sealed record Entry(FalloutFormKey Form, string Name, int Count, bool Equipped);
    private const int Slots = 8;
    private readonly Entry[] _entries;
    private readonly bool _consumables, _xr;
    private readonly Action<FalloutFormKey> _apply;
    private readonly Action _close;
    private readonly Label[] _labels = new Label[Slots];
    private Label _title = null!, _center = null!, _hint = null!;
    private int _page, _selected = -1;
    private bool _closed;
    private string? _error;
    private Vector2 Center => Size * .5f;
    private float Radius => Math.Min(Size.X * .31f, Size.Y * .36f);
    private int Pages => Math.Max(1, (_entries.Length + Slots - 1) / Slots);
    private Entry? Selected => _selected >= 0 && _page * Slots + _selected < _entries.Length
        ? _entries[_page * Slots + _selected] : null;
    internal object State => new
    {
        consumables = _consumables,
        page = _page,
        pages = Pages,
        selected = Selected?.Form.ToString(),
        count = _entries.Length,
        entries = _entries.Skip(_page * Slots).Take(Slots).Select((entry, slot) => new
        { slot, form = entry.Form.ToString(), name = entry.Name, count = entry.Count, equipped = entry.Equipped }).ToArray(),
        error = _error,
        closed = _closed
    };

    internal NativeCombatWheel(FalloutPluginStack records, FalloutPlayerInventory inventory, bool consumables,
        bool xr, Action<FalloutFormKey> apply, Action close)
    {
        Name = consumables ? "ConsumableWheel" : "WeaponWheel";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        _consumables = consumables; _xr = xr; _apply = apply; _close = close;
        _entries = inventory.Items.Where(item => item.RecordType == (consumables ? "ALCH" : "WEAP"))
            .Select(item =>
            {
                var full = records.GetEffective(item.FormKey).ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
                return new Entry(item.FormKey, full.IsEmpty ? item.EditorId : FalloutDialogueTopic.Text(full.Span),
                    item.Count, inventory.Equipped.Contains(item.RuntimeFormId));
            }).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Form.ToString(), StringComparer.Ordinal).ToArray();
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _title = Text(26); _center = Text(20); _hint = Text(17);
        for (var index = 0; index < Slots; index++) _labels[index] = Text(17);
        Resized += Refresh;
        Refresh();
    }

    private Label Text(int fontSize)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(1, .86f, .58f));
        AddChild(label); return label;
    }

    internal void SelectDirection(Vector2 direction)
    {
        if (_closed || !direction.IsFinite()) return;
        var selected = direction.Length() < .3f ? -1 :
            Mathf.PosMod((int)MathF.Floor((MathF.Atan2(direction.Y, direction.X) + MathF.PI / 2 + MathF.PI / Slots) / MathF.Tau * Slots), Slots);
        if (_selected == selected) return;
        _selected = selected; _error = null; Refresh();
    }

    internal void ChangePage(int delta)
    {
        if (_closed) return;
        _page = Mathf.PosMod(_page + delta, Pages); _selected = -1; _error = null; Refresh();
    }

    internal void Finish(bool accept)
    {
        if (_closed) return;
        var entry = accept ? Selected : null;
        try
        {
            if (entry is not null) _apply(entry.Form);
            _closed = true;
            GD.Print($"OPENNV_COMBAT_WHEEL_CLOSE kind={(_consumables ? "aid" : "weapon")} selected={entry?.Form} accepted={entry is not null}");
            _close();
        }
        catch (Exception error)
        {
            _error = error.Message;
            GD.PushError($"OPENNV_COMBAT_WHEEL_ACTION_FAILED form={entry?.Form} {error.Message}");
            Refresh();
        }
    }

    public override void _Input(InputEvent input)
    {
        if (_closed) return;
        if (input is InputEventMouseMotion motion && !_xr)
            SelectDirection((motion.Position - Center) / Math.Max(1, Radius * .55f));
        else if (input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left) Finish(true);
            else if (mouse.ButtonIndex == MouseButton.Right) Finish(false);
            else if (mouse.ButtonIndex == MouseButton.WheelDown) ChangePage(1);
            else if (mouse.ButtonIndex == MouseButton.WheelUp) ChangePage(-1);
            else return;
        }
        else if (input is InputEventKey { Echo: false } key)
        {
            if (!_xr && !key.Pressed && key.PhysicalKeycode == (_consumables ? Key.H : Key.Q)) Finish(true);
            else if (key.Pressed && key.PhysicalKeycode == Key.Escape) Finish(false);
            else if (key.Pressed && key.PhysicalKeycode is Key.Enter or Key.KpEnter) Finish(true);
            else if (key.Pressed && key.PhysicalKeycode is Key.Pagedown or Key.Pageup)
                ChangePage(key.PhysicalKeycode == Key.Pagedown ? 1 : -1);
            else if (key.Pressed && key.PhysicalKeycode is >= Key.Key1 and <= Key.Key8)
            { _selected = (int)(key.PhysicalKeycode - Key.Key1); Refresh(); }
            else return;
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        if (!_xr && what == NotificationWMWindowFocusOut && !_closed) Finish(false);
    }

    private void Refresh()
    {
        if (_title is null) return;
        var radius = Radius;
        _title.Text = $"{(_consumables ? "CONSUMABLES" : "WEAPONS")}   {_page + 1}/{Pages}";
        _title.Position = new(Center.X - radius, Center.Y - radius - 65); _title.Size = new(radius * 2, 48);
        _center.Text = _error is not null ? "Item unavailable\nChoose another item" : Selected is { } entry
            ? $"{entry.Name}\n{(_consumables ? "Use one" : entry.Equipped ? "Equipped" : "Equip")}" :
            _entries.Length == 0 ? "No items" : "Cancel";
        _center.Position = Center - new Vector2(radius * .34f, 70); _center.Size = new(radius * .68f, 140);
        _hint.Text = _xr ? "Right stick: select  •  A/B: page  •  Release: confirm  •  Menu: cancel" :
            $"Hold {(_consumables ? "H" : "Q")} • Aim or 1–8 • Release: confirm • Center / Esc: cancel\nScroll / PgUp / PgDn: page";
        _hint.Position = new(20, Center.Y + radius + 20); _hint.Size = new(Size.X - 40, 60);
        for (var index = 0; index < Slots; index++)
        {
            var offset = new Vector2(MathF.Sin(index * MathF.Tau / Slots), -MathF.Cos(index * MathF.Tau / Slots)) * radius * .72f;
            var label = _labels[index]; var itemIndex = _page * Slots + index;
            label.Text = itemIndex < _entries.Length ? $"{_entries[itemIndex].Name}\n×{_entries[itemIndex].Count}{(_entries[itemIndex].Equipped ? "  •" : "")}" : "—";
            label.Size = new(radius * .56f, 90); label.Position = Center + offset - label.Size * .5f;
        }
        SetMeta("opennv_combat_wheel", System.Text.Json.JsonSerializer.Serialize(State));
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero, Size), new Color(.015f, .018f, .014f, .78f));
        var radius = Radius;
        for (var slot = 0; slot < Slots; slot++)
        {
            var points = new List<Vector2>();
            var start = slot * MathF.Tau / Slots - MathF.PI / 2 - MathF.PI / Slots + .015f;
            var end = start + MathF.Tau / Slots - .03f;
            for (var step = 0; step <= 16; step++) points.Add(Center + Vector2.FromAngle(Mathf.Lerp(start, end, step / 16f)) * radius);
            for (var step = 16; step >= 0; step--) points.Add(Center + Vector2.FromAngle(Mathf.Lerp(start, end, step / 16f)) * radius * .4f);
            DrawColoredPolygon(points.ToArray(), slot == _selected && Selected is not null ?
                new Color(.43f, .28f, .08f, .98f) : new Color(.09f, .11f, .08f, .96f));
        }
    }
}
