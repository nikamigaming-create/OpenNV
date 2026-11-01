using System.Xml.Linq;
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
    private readonly Vector2 _mapSize;
    private readonly float _heading;
    private readonly FalloutNumericGameSettings _settings;
    private readonly NativeOwnedMenuTree _markerRules;
    private readonly XElement _markerParent, _markerTemplate, _cursorTemplate;
    private readonly List<(Vector2 Point, FalloutMapMarker Marker, Texture2D Art, bool Travel)> _markers = [];
    private Vector2 _offset;
    private float _zoom = 1;
    private Vector2? _drag;
    private Vector2 _press;
    private float _dragDistance;
    internal readonly record struct View(float Zoom, Vector2 Offset);
    internal View CaptureView() => new(_zoom, _offset);
    internal void RestoreView(View view)
    {
        var bounds = ZoomBounds;
        if (!float.IsFinite(view.Zoom) || view.Zoom < bounds.Minimum || view.Zoom > bounds.Maximum || !view.Offset.IsFinite())
            throw new InvalidDataException("Map view is nonfinite or outside the zoom range.");
        _zoom = view.Zoom; _offset = view.Offset; ClampPan(); QueueRedraw();
    }
    internal void ZoomAt(Vector2 position, int direction)
    {
        var point = (position - _offset) / Extent;
        var bounds = ZoomBounds;
        _zoom = Math.Clamp(_zoom * (direction > 0 ? 1.2f : 1 / 1.2f), bounds.Minimum, bounds.Maximum);
        _offset = position - point * Extent; ClampPan(); QueueRedraw();
    }
    internal event Action<FalloutMapMarker>? Selected;
    internal event Action<string>? Hovered;
    internal object State => new
    {
        markers = _markers.Select(marker => new
        {
            reference = marker.Marker.Reference.ToString(),
            marker.Marker.Name,
            x = marker.Point.X,
            y = marker.Point.Y,
            pixel = new[] { Position.X + _offset.X + marker.Point.X * Extent.X,
                Position.Y + _offset.Y + marker.Point.Y * Extent.Y },
            canTravel = marker.Travel
        }).ToArray(),
        player = new[] { _player.X, _player.Y },
        rect = new[] { Position.X, Position.Y, Size.X, Size.Y },
        zoom = _zoom
    };

    internal NativeOwnedWorldMap(FalloutPluginStack records, FalloutReferenceWorld references, FalloutFormKey world,
        float playerX, float playerY, float heading, Color color)
    {
        Name = "OwnedWorldMap"; ClipContents = true; MouseFilter = MouseFilterEnum.Stop; _color = color; _heading = heading;
        _settings = records.NumericSettings;
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/main/map_menu.xml"));
        _markerTemplate = new XElement(menu.Descendants("template")
            .Single(tile => (string?)tile.Attribute("name") == "MapMarkerTemplate").Elements().Single());
        var image = menu.Descendants().Single(tile => (string?)tile.Attribute("name") == "MM_WorldMap_ParentImage");
        _markerParent = new XElement("rect", new XAttribute("name", "MM_WorldMap_ParentImage"),
            new XElement(image.Element("x") ?? throw new InvalidDataException("World map has no source X pan rule.")),
            new XElement(image.Element("y") ?? throw new InvalidDataException("World map has no source Y pan rule.")),
            image.Elements().Where(property => property.Name.LocalName is "alpha" or "brightness" or "systemcolor")
                .Select(property => new XElement(property)), _markerTemplate);
        _cursorTemplate = new XElement(menu.Descendants().Single(tile => (string?)tile.Attribute("name") == "MM_WorldMapCursor"));
        _markerRules = new(new XElement("menu",
            new XElement(menu.Descendants("menu").Single().Element("systemcolor") ??
                throw new InvalidDataException("World map has no source system color.")), _markerParent, _cursorTemplate));
        var map = FalloutWorldMap.Read(records, world);
        _mapSize = new(map.Width, map.Height);
        _texture = NativeOwnedMediaLoader.LoadTexture(map.Texture);
        _cursor = NativeOwnedMediaLoader.LoadTexture("textures/interface/icons/misc/glow_cursor.dds");
        var point = FalloutWorldMap.Position(records, world, playerX, playerY); _player = new(point.X, point.Y);
        foreach (var marker in FalloutWorldMap.SourceMarkers(records))
        {
            var state = references.MapMarkerState(marker.Marker);
            if (!state.Visible || marker.World is not { } markerWorld || FalloutWorldMap.Read(records, markerWorld).World != map.World) continue;
            var uv = FalloutWorldMap.Position(records, markerWorld, marker.X, marker.Y);
            var icon = !state.CanTravel ? "undiscovered" : marker.Marker.Type switch
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
        SetMeta("opennv_map_unbound", "fast-travel-time-and-nearby-enemy-policy,quest-target-routing");
    }
    private (float Minimum, float Maximum) ZoomBounds
    {
        get
        {
            var minimum = _settings.Float("fWorldMapMinZoom");
            var maximum = _settings.Float("fWorldMapMaxZoom");
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum <= 0 || maximum <= minimum)
                throw new InvalidDataException("World map source zoom bounds are invalid.");
            return (minimum, maximum);
        }
    }
    private Vector2 Extent => _mapSize * _zoom;
    internal void CenterPlayer() { _offset = Size / 2 - _player * Extent; ClampPan(); QueueRedraw(); }
    private void ClampPan()
    {
        _markerRules.Bind(_markerParent, "width", Extent.X);
        _markerRules.Bind(_markerParent, "height", Extent.Y);
        // The expression adds the proposed displacement, then applies the
        // authored bounds. No native gameplay position is changed by panning.
        _markerRules.Bind(_markerParent, "dragdeltax", _offset.X);
        _markerRules.Bind(_markerParent, "dragdeltay", _offset.Y);
        _offset = new(_markerRules.Number(_markerParent, "x", bindings: false),
            _markerRules.Number(_markerParent, "y", bindings: false));
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ZoomAt(mouse.Position, mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1); AcceptEvent();
            }
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed) { _drag = _press = mouse.Position; _dragDistance = 0; }
                else if (_drag is not null)
                {
                    _drag = null;
                    if (_dragDistance < 6 && _press.DistanceTo(mouse.Position) < 6)
                    {
                        var selected = _markers.Where(marker => (_offset + marker.Point * Extent).DistanceTo(mouse.Position) < 22)
                            .OrderBy(marker => (_offset + marker.Point * Extent).DistanceSquaredTo(mouse.Position)).FirstOrDefault();
                        if (selected.Marker is { } marker) Selected?.Invoke(marker);
                    }
                }
                AcceptEvent();
            }
        }
        if (input is InputEventMouseMotion motion)
        {
            if (_drag is not null) { _dragDistance += motion.Relative.Length(); _offset += motion.Relative; ClampPan(); _drag = motion.Position; QueueRedraw(); }
            var selected = _markers.Where(marker => (_offset + marker.Point * Extent).DistanceTo(motion.Position) < 22)
                .OrderBy(marker => (_offset + marker.Point * Extent).DistanceSquaredTo(motion.Position)).FirstOrDefault();
            Hovered?.Invoke(selected.Marker?.Name ?? ""); AcceptEvent();
        }
    }
    public override void _Draw()
    {
        DrawTextureRect(_texture, new(_offset, Extent), false, _markerRules.DrawingColor(_markerParent));
        var bounds = ZoomBounds;
        var minimum = _settings.Float("fWorldMapMarkerMinSize");
        var maximum = _settings.Float("fWorldMapMarkerMaxSize");
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum <= 0 || maximum <= 0)
            throw new InvalidDataException("World map source marker sizes are invalid.");
        _markerRules.Bind(_markerParent, "user0", Mathf.Lerp(minimum, maximum,
            (_zoom - bounds.Minimum) / (bounds.Maximum - bounds.Minimum)));
        var size = new Vector2(_markerRules.Number(_markerTemplate, "width"), _markerRules.Number(_markerTemplate, "height"));
        var zoom = _markerRules.Number(_markerTemplate, "zoom");
        var crop = new Vector2(_markerRules.Number(_markerTemplate, "cropx"), _markerRules.Number(_markerTemplate, "cropy"));
        foreach (var marker in _markers)
        {
            var point = _offset + marker.Point * Extent;
            if (NativeUiImage.Sample(new(point - size / 2, size), new(Vector2.Zero, marker.Art.GetSize()), zoom, crop) is { } sample)
                DrawTextureRectRegion(marker.Art, sample.Destination, sample.Source, _markerRules.DrawingColor(_markerTemplate));
        }
        var cursorMinimum = _settings.Float("fWorldfPlayerCursorMinSize");
        var cursorMaximum = _settings.Float("fWorldPlayerCursorMaxSize");
        if (!float.IsFinite(cursorMinimum) || !float.IsFinite(cursorMaximum) || cursorMinimum <= 0 || cursorMaximum <= 0)
            throw new InvalidDataException("World map source cursor sizes are invalid.");
        _markerRules.Bind(_markerParent, "user2", Mathf.Lerp(cursorMinimum, cursorMaximum,
            (_zoom - bounds.Minimum) / (bounds.Maximum - bounds.Minimum)));
        var cursorSize = new Vector2(_markerRules.Number(_cursorTemplate, "width"), _markerRules.Number(_cursorTemplate, "height"));
        var cursorZoom = _markerRules.Number(_cursorTemplate, "zoom");
        var cursorCrop = new Vector2(_markerRules.Number(_cursorTemplate, "cropx"), _markerRules.Number(_cursorTemplate, "cropy"));
        DrawSetTransform(_offset + _player * Extent, _heading);
        if (NativeUiImage.Sample(new(-cursorSize / 2, cursorSize), new(Vector2.Zero, _cursor.GetSize()), cursorZoom, cursorCrop) is { } cursorSample)
            DrawTextureRectRegion(_cursor, cursorSample.Destination, cursorSample.Source, _color);
        DrawSetTransform(Vector2.Zero);
    }
}
