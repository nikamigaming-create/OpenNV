using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeLocalMapDoor(FalloutFormKey Reference, Vector2 Position);
internal sealed record NativeLocalMapSource(Rect2 Bounds, Vector2 Player, float NorthRadians, float UnitsToMetres,
    bool Interior, int GridCount, float MinimumZoom, float MaximumZoom, float MarkerMinimum, float MarkerMaximum,
    float CursorMinimum, float CursorMaximum, IReadOnlyList<NativeLocalMapDoor> Doors);
internal sealed class NativeLocalMapView
{
    internal bool Initialized;
    internal float Magnification;
    internal Vector2 Center;
}

// Source projection; the resident scene view still lacks native map materials,
// vertical coverage/culling and persistent exploration ownership.
internal sealed partial class NativeOwnedLocalMap(World3D world, Vector3 player, Color color, float heading,
    NativeLocalMapSource source, NativeLocalMapView view) : Control
{
    private Camera3D _camera = null!;
    private SubViewport _viewport = null!;
    private TextureRect _arrow = null!;
    private TextureRect _image = null!;
    private MapOverlay _overlay = null!;
    private NativeOwnedMenuTree _tiles = null!;
    private XElement _parent = null!;
    private bool _dragging;
    internal string? Error { get; private set; }
    private float CanvasSize => source.GridCount * (source.Interior ? 2048 : 1024) * view.Magnification;
    internal object State => new
    {
        error = Error,
        bounds = new[] { source.Bounds.Position.X, source.Bounds.Position.Y, source.Bounds.Size.X, source.Bounds.Size.Y },
        magnification = view.Magnification,
        center = new[] { view.Center.X, view.Center.Y },
        player = new[] { source.Player.X, source.Player.Y },
        source.NorthRadians,
        source.GridCount,
        cameraSpanMetres = _camera?.Size,
        cameraPositionMetres = _camera is null ? null : new[] { _camera.Position.X, _camera.Position.Y, _camera.Position.Z },
        doors = source.Doors.Select(door => new
        {
            reference = door.Reference.ToString(),
            position = new[] { door.Position.X, door.Position.Y },
            normalized = new[] { (door.Position.X - source.Bounds.Position.X) / source.Bounds.Size.X,
                1 - (door.Position.Y - source.Bounds.Position.Y) / source.Bounds.Size.Y }
        }).ToArray(),
        unbound = "native-map-shader,vertical-camera-coverage,ceiling-culling,secret-geometry,fog-exploration,marker-selection"
    };

    private sealed partial class MapOverlay(NativeOwnedMenuTree tiles) : Control
    {
        public override void _Draw() => tiles.Draw(this);
    }

    public override void _Ready()
    {
        try { Initialize(); }
        catch (Exception error)
        {
            Error = error.Message;
            Visible = false;
            if (_viewport is not null) _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            SetMeta("opennv_local_map_error", Error);
            GD.PushError($"OPENNV_LOCAL_MAP_UNBOUND {Error}");
        }
    }

    private void Initialize()
    {
        if (!source.Bounds.HasArea() || !source.Player.IsFinite() || !float.IsFinite(source.NorthRadians) ||
            !float.IsFinite(source.UnitsToMetres) || source.UnitsToMetres <= 0 || source.GridCount <= 0 ||
            !float.IsFinite(source.MinimumZoom) || !float.IsFinite(source.MaximumZoom) ||
            source.MinimumZoom <= 0 || source.MaximumZoom <= source.MinimumZoom)
            throw new InvalidDataException("Local map source projection is invalid.");
        if (!view.Initialized)
        {
            view.Magnification = Math.Clamp(1, source.MinimumZoom, source.MaximumZoom);
            view.Center = source.Player; view.Initialized = true;
        }
        view.Magnification = Math.Clamp(view.Magnification, source.MinimumZoom, source.MaximumZoom);
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop;
        SetMeta("opennv_local_map_unbound", "native-map-shader,vertical-camera-coverage,ceiling-culling,secret-geometry,fog-exploration,marker-selection");
        _viewport = new()
        {
            Name = "ResidentLocalMap",
            World3D = world,
            Size = new(1, 1),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            GuiDisableInput = true,
            Disable3D = false
        };
        AddChild(_viewport);
        _camera = new()
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Position = player + Vector3.Up * (source.Interior ? 2 : 100),
            Rotation = new(-Mathf.Pi / 2, source.NorthRadians, 0),
            Near = .05f,
            Far = 500,
            CullMask = 1,
            Current = true
        };
        _viewport.AddChild(_camera);
        _image = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        var material = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            uniform vec4 map_tint : source_color;
            void fragment() {
                vec3 source = texture(TEXTURE, UV).rgb;
                float luminance = dot(source, vec3(0.2126, 0.7152, 0.0722));
                COLOR = vec4(map_tint.rgb * luminance, map_tint.a);
            }
            """ } };
        material.SetShaderParameter("map_tint", color); _image.Material = material;
        AddChild(_image);
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/main/map_menu.xml"));
        var template = menu.Descendants("template").Single(tile => (string?)tile.Attribute("name") == "MapMarkerTemplate").Elements().Single();
        _parent = new XElement("rect", new XAttribute("name", "MM_LocalMap_ParentImage"), new XElement("locus", 1));
        var root = new XElement("menu", new XAttribute("name", "LocalMapOverlay"), new XElement("systemcolor", "entity_pipboy"), _parent);
        _tiles = new(root);
        foreach (var door in source.Doors)
        {
            var normalized = (door.Position - source.Bounds.Position) / source.Bounds.Size;
            if (normalized.X is < 0 or > 1 || normalized.Y is < 0 or > 1) continue;
            var tile = new XElement(template); _parent.Add(tile);
            _tiles.SetFilename(tile, "Interface/Icons/Local Map/icon_local_door.dds");
            _tiles.Bind(tile, "_x", normalized.X); _tiles.Bind(tile, "_y", 1 - normalized.Y);
            _tiles.Bind(tile, "_selected", 0);
        }
        _overlay = new(_tiles) { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_overlay);
        _arrow = new()
        {
            // Native rotates the sampled cursor art by the opposite sign;
            // its source yaw includes this authored-image alignment offset.
            Rotation = heading + .3f + source.NorthRadians,
            Modulate = color,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_arrow); Resized += UpdateProjection; UpdateProjection();
    }

    private void UpdateProjection()
    {
        if (_camera is null || Size.X <= 0 || Size.Y <= 0) return;
        var scale = CanvasSize / source.Bounds.Size.Y;
        var normalized = (view.Center - source.Bounds.Position) / source.Bounds.Size;
        var pan = Size / 2 - new Vector2(normalized.X, 1 - normalized.Y) * CanvasSize;
        pan.X = Math.Clamp(pan.X, Size.X / 2 - CanvasSize, Size.X / 2);
        pan.Y = Math.Clamp(pan.Y, Size.Y / 2 - CanvasSize, Size.Y / 2);
        var center = (Size / 2 - pan) / CanvasSize;
        view.Center = source.Bounds.Position + new Vector2(center.X, 1 - center.Y) * source.Bounds.Size;
        var physical = view.Center.Rotated(source.NorthRadians);
        _camera.Position = new(physical.X * source.UnitsToMetres, _camera.Position.Y, -physical.Y * source.UnitsToMetres);
        _camera.Size = Size.Y / scale * source.UnitsToMetres;
        _viewport.Size = new(Math.Max(1, (int)MathF.Ceiling(Size.X)), Math.Max(1, (int)MathF.Ceiling(Size.Y)));
        var visible = new Rect2(pan, Vector2.One * CanvasSize).Intersection(new(Vector2.Zero, Size));
        _image.Visible = visible.HasArea(); _image.Position = visible.Position; _image.Size = visible.Size;
        _image.Texture = new AtlasTexture { Atlas = _viewport.GetTexture(), Region = visible };
        _tiles.Bind(_parent, "x", pan.X); _tiles.Bind(_parent, "y", pan.Y);
        _tiles.Bind(_parent, "width", CanvasSize); _tiles.Bind(_parent, "height", CanvasSize);
        var zoom = (view.Magnification - source.MinimumZoom) / (source.MaximumZoom - source.MinimumZoom);
        _tiles.Bind(_parent, "user0", Mathf.Lerp(source.MarkerMinimum, source.MarkerMaximum, zoom));
        _overlay.Size = Size; _overlay.QueueRedraw();
        var width = Mathf.Lerp(source.CursorMinimum, source.CursorMaximum, zoom);
        var arrowSize = new Vector2(width, width * 1.28f);
        var texture = NativeOwnedMediaLoader.LoadTexture("textures/interface/icons/misc/glow_cursor.dds");
        var sample = NativeUiImage.Sample(new(Vector2.Zero, arrowSize), new(Vector2.Zero, texture.GetSize()), width * 3.5f, Vector2.Zero);
        _arrow.Texture = sample is { } region ? new AtlasTexture { Atlas = texture, Region = region.Source } : null;
        _arrow.Size = arrowSize; _arrow.PivotOffset = arrowSize / 2;
        var playerPoint = (source.Player - view.Center) * scale;
        _arrow.Position = Size / 2 + new Vector2(playerPoint.X, -playerPoint.Y) - arrowSize / 2;
    }

    internal void Zoom(int direction)
    {
        if (direction == 0) return;
        view.Magnification = Math.Clamp(view.Magnification * (direction > 0 ? 1.1f : 1 / 1.1f), source.MinimumZoom, source.MaximumZoom);
        UpdateProjection();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left) _dragging = mouse.Pressed;
            else if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                Zoom(mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && _dragging)
        {
            var delta = motion.Relative * source.Bounds.Size.Y / CanvasSize;
            view.Center -= new Vector2(delta.X, -delta.Y); UpdateProjection(); AcceptEvent();
        }
    }
}
