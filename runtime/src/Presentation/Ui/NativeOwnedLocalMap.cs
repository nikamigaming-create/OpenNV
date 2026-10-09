using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

// A second view of the resident world, with no copied or generated game assets.
internal sealed partial class NativeOwnedLocalMap(World3D world, Vector3 player, bool interior, Color color, float heading) : Control
{
    private Camera3D _camera = null!;
    private Vector2? _drag;
    private float _span = 60;
    private readonly Vector3 _player = player;
    private TextureRect _arrow = null!;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        var viewport = new SubViewport
        {
            Name = "ResidentLocalMap",
            World3D = world,
            Size = new(1024, 640),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            GuiDisableInput = true,
            Disable3D = false
        };
        AddChild(viewport);
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = _span,
            Position = _player + Vector3.Up * (interior ? 2 : 100),
            Rotation = new(-Mathf.Pi / 2, 0, 0),
            Near = 0.05f,
            Far = 500,
            CullMask = 1,
            Current = true
        };
        viewport.AddChild(_camera);
        var image = new TextureRect
        {
            Texture = viewport.GetTexture(),
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
        material.SetShaderParameter("map_tint", color);
        image.Material = material;
        AddChild(image); image.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _arrow = new TextureRect
        {
            Texture = NativeOwnedMediaLoader.LoadTexture("textures/interface/icons/misc/glow_cursor.dds"),
            Size = new(32, 32),
            PivotOffset = new(16, 16),
            Rotation = heading,
            Modulate = color,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_arrow);
        Resized += UpdateArrow;
        UpdateArrow();
    }

    private void UpdateArrow()
    {
        var displacement = _player - _camera.Position;
        _arrow.Position = Size / 2 + new Vector2(displacement.X, displacement.Z) * Size.Y / _span - _arrow.Size / 2;
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left) _drag = mouse.Pressed ? mouse.Position : null;
            else if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                _span = Math.Clamp(_span * (mouse.ButtonIndex == MouseButton.WheelUp ? 0.8f : 1.25f), 8, 300);
                _camera.Size = _span; UpdateArrow();
            }
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && _drag is { } previous)
        {
            var delta = (motion.Position - previous) * _span / Math.Max(1, Size.Y);
            _camera.Position -= new Vector3(delta.X, 0, delta.Y);
            _drag = motion.Position; UpdateArrow(); AcceptEvent();
        }
    }
}
