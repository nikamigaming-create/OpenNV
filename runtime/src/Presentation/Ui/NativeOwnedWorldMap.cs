using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Clipped, pannable map using the source WRLD image and placed markers.</summary>
internal sealed partial class NativeOwnedWorldMap : Control
{
    private readonly Texture2D _texture, _cursor;
    private readonly Color _color;
    private readonly Vector2 _player;
    private readonly float _heading;
    private readonly List<(Vector2 Point, FalloutMapMarker Marker, Texture2D Art, bool Travel)> _markers = [];
    private Vector2 _offset;
    private float _zoom = 1;
    private Vector2? _drag;
    internal event Action<string>? Hovered;
    internal object State => new
    {
        markers = _markers.Select(marker => new
        {
            reference = marker.Marker.Reference.ToString(),
            marker.Marker.Name,
            x = marker.Point.X,
            y = marker.Point.Y,
            canTravel = marker.Travel
        }).ToArray(),
        player = new[] { _player.X, _player.Y },
        zoom = _zoom
    };

    internal NativeOwnedWorldMap(FalloutPluginStack records, FalloutReferenceWorld references, FalloutFormKey world,
        float playerX, float playerY, float heading, Color color)
    {
        Name = "OwnedWorldMap"; ClipContents = true; MouseFilter = MouseFilterEnum.Stop; _color = color; _heading = heading;
        var map = FalloutWorldMap.Read(records, world);
        _texture = NativeOwnedMediaLoader.LoadTexture(map.Texture);
        _cursor = NativeOwnedMediaLoader.LoadTexture("textures/interface/icons/misc/glow_cursor.dds");
        var point = FalloutWorldMap.Position(records, world, playerX, playerY); _player = new(point.X, point.Y);
        foreach (var marker in FalloutWorldMap.SourceMarkers(records))
        {
            var state = references.MapMarkerState(marker.Marker);
            if (!state.Visible || marker.World is not { } markerWorld || FalloutWorldMap.Read(records, markerWorld).World != map.World) continue;
            var uv = FalloutWorldMap.Position(records, markerWorld, marker.X, marker.Y);
            var icon = marker.Marker.Type switch
            {
                0 => "undiscovered",
                1 => "city",
                2 => "settlement",
                3 => "encampment",
                4 => "natural_landmark",
                5 => "cave",
                6 => "factory",
                7 => "monument",
                8 => "military",
                9 => "office",
                10 => "ruins_town",
                11 => "ruins_urban",
                12 => "ruins_sewer",
                13 => "metro",
                14 => "vault",
                _ => throw new NotSupportedException($"Map marker {marker.Marker.Reference} type {marker.Marker.Type} has no icon binding."),
            };
            _markers.Add((new(uv.X, uv.Y), marker.Marker,
                NativeOwnedMediaLoader.LoadTexture($"textures/interface/icons/world map/icon_map_{icon}.dds"), state.CanTravel));
        }
        Resized += CenterPlayer;
        SetMeta("opennv_map_source", map.World.ToString() + ":MNAM/ICON;winning-XMRK/DATA");
        SetMeta("opennv_map_unbound", "fast-travel,quest-target-routing,local-map,discovery-radius");
    }
    private Vector2 Extent => _texture.GetSize() * _zoom;
    internal void CenterPlayer() { _offset = Size / 2 - _player * Extent; QueueRedraw(); }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var point = (mouse.Position - _offset) / Extent;
                _zoom = Math.Clamp(_zoom * (mouse.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f), 0.25f, 4);
                _offset = mouse.Position - point * Extent; QueueRedraw(); AcceptEvent();
            }
            if (mouse.ButtonIndex == MouseButton.Left) { _drag = mouse.Pressed ? mouse.Position : null; AcceptEvent(); }
        }
        if (input is InputEventMouseMotion motion)
        {
            if (_drag is { } previous) { _offset += motion.Position - previous; _drag = motion.Position; QueueRedraw(); }
            var selected = _markers.Where(marker => (_offset + marker.Point * Extent).DistanceTo(motion.Position) < 22)
                .OrderBy(marker => (_offset + marker.Point * Extent).DistanceSquaredTo(motion.Position)).FirstOrDefault();
            Hovered?.Invoke(selected.Marker?.Name ?? ""); AcceptEvent();
        }
    }
    public override void _Draw()
    {
        DrawTextureRect(_texture, new(_offset, Extent), false, new(_color.R, _color.G, _color.B, 0.5f));
        foreach (var marker in _markers)
        {
            // Source marker art has fourteen pixels of glow padding around its
            // 34-pixel core. Keep that padding instead of stretching the icon.
            var point = _offset + marker.Point * Extent;
            DrawTextureRect(marker.Art, new(point - Vector2.One * 31, Vector2.One * 62), false, _color);
        }
        DrawSetTransform(_offset + _player * Extent, _heading);
        DrawTextureRect(_cursor, new(-Vector2.One * 24, Vector2.One * 48), false, _color);
        DrawSetTransform(Vector2.Zero);
    }
}
