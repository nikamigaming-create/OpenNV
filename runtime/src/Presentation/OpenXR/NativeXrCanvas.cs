using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

/// <summary>Original canvas menus rendered once on a stereo world surface.</summary>
internal sealed partial class NativeXrCanvas : Node3D
{
    private readonly Camera3D _head;
    private readonly SubViewport _view;
    private readonly MeshInstance3D _panel;
    private readonly ColorRect _cursor;
    private readonly HashSet<CanvasLayer> _layers = [];
    private bool _modal, _pressed;
    private int _scrollDirection;
    private double _scrollDelay;
    private long _scrollEvents;
    private Vector2 _pointer = new(-1, -1);
    private const float Width = 1.8f, Height = 1.1571429f;
    internal object State => new
    {
        modal = _modal,
        layers = _layers.Where(IsInstanceValid).Select(node => node.Name.ToString()).ToArray(),
        pointer = new[] { _pointer.X, _pointer.Y },
        pressed = _pressed,
        scrollEvents = _scrollEvents,
        rendering = _view.RenderTargetUpdateMode.ToString(),
        surface = new
        {
            position = V(_panel.GlobalPosition),
            right = V(_panel.GlobalBasis.X),
            up = V(_panel.GlobalBasis.Y),
            size = new[] { Width, Height },
            pixels = new[] { _view.Size.X, _view.Size.Y }
        }
    };
    private static float[] V(Vector3 value) => [value.X, value.Y, value.Z];
    internal NativeXrCanvas(Camera3D head)
    {
        Name = "NativeXrMenus"; ProcessMode = ProcessModeEnum.Always; _head = head;
        _view = new SubViewport
        {
            Name = "OriginalCanvas",
            Size = new(1400, 900),
            Disable3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(_view);
        _panel = new MeshInstance3D
        {
            Name = "ReadableMenuSurface",
            Mesh = new QuadMesh { Size = new(Width, Height) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = _view.GetTexture(),
                AlbedoTextureForceSrgb = false,
                // A modal control must remain readable beside a wall or while
                // looking down at loot. It retains its stereo surface depth,
                // but world geometry cannot erase its text or buttons.
                NoDepthTest = true,
                RenderPriority = 10,
                CullMode = BaseMaterial3D.CullModeEnum.Back
            }
        };
        AddChild(_panel);
        var cursorLayer = new CanvasLayer { Layer = 1000 }; _view.AddChild(cursorLayer);
        _cursor = new ColorRect { Size = new(7, 7), Color = new(1, .7f, .2f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        cursorLayer.AddChild(_cursor);
    }
    internal void Admit(CanvasLayer layer)
    {
        if (!_layers.Add(layer)) return;
        var owner = layer.GetParent();
        layer.Reparent(_view, false);
        var release = Callable.From(() => { if (IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) layer.QueueFree(); });
        owner.Connect(Node.SignalName.TreeExiting, release);
        layer.TreeExiting += () =>
        {
            _layers.Remove(layer);
            if (IsInstanceValid(owner) && owner.IsConnected(Node.SignalName.TreeExiting, release))
                owner.Disconnect(Node.SignalName.TreeExiting, release);
        };
    }
    internal void Present(bool modal, bool visible)
    {
        if (modal != _modal || !modal)
        {
            var basis = _head.GlobalBasis.Orthonormalized();
            _panel.GlobalTransform = new(basis, _head.GlobalPosition - basis.Z);
        }
        if (_modal && !modal && _pressed)
        {
            using var release = new InputEventMouseButton
            {
                Position = _pointer,
                GlobalPosition = _pointer,
                ButtonIndex = MouseButton.Left,
                Pressed = false
            };
            _view.PushInput(release, true); _pressed = false;
        }
        _modal = modal;
        _panel.Visible = visible;
        var update = visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (_view.RenderTargetUpdateMode != update) _view.RenderTargetUpdateMode = update;
        if (!modal) _cursor.Visible = false;
    }
    internal void Point(Node3D aim, bool tracked, bool pressed)
    {
        Vector2 point = new(-1, -1);
        if (tracked)
        {
            var inverse = _panel.GlobalTransform.AffineInverse();
            var origin = inverse * aim.GlobalPosition; var direction = inverse.Basis * -aim.GlobalBasis.Z;
            var t = MathF.Abs(direction.Z) > .00001f ? -origin.Z / direction.Z : -1;
            var hit = origin + direction * t;
            if (t > 0 && MathF.Abs(hit.X) <= Width / 2 && MathF.Abs(hit.Y) <= Height / 2)
                point = new((hit.X / Width + .5f) * _view.Size.X, (.5f - hit.Y / Height) * _view.Size.Y);
        }
        using var motion = new InputEventMouseMotion
        {
            Position = point,
            GlobalPosition = point,
            Relative = point - _pointer,
            ButtonMask = _pressed ? MouseButtonMask.Left : 0
        };
        _view.PushInput(motion, true); _pointer = point;
        _cursor.Visible = point.X >= 0; _cursor.Position = point - _cursor.Size / 2;
        if (_pressed == pressed) return;
        using var button = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed };
        _view.PushInput(button, true); _pressed = pressed;
    }

    internal void Scroll(float axis, double delta)
    {
        var direction = _modal && _pointer.X >= 0 && float.IsFinite(axis) && MathF.Abs(axis) >= .6f ? Math.Sign(axis) : 0;
        if (direction != _scrollDirection) { _scrollDirection = direction; _scrollDelay = 0; }
        if (direction == 0 || (_scrollDelay -= delta) > 0) return;
        // OpenNV controller adaptation; the original menu retains item order,
        // selected column, scrolling and transfer ownership.
        var wheel = direction > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
        using var press = new InputEventMouseButton { Position = _pointer, GlobalPosition = _pointer, ButtonIndex = wheel, Pressed = true };
        using var release = new InputEventMouseButton { Position = _pointer, GlobalPosition = _pointer, ButtonIndex = wheel, Pressed = false };
        _view.PushInput(press, true); _view.PushInput(release, true);
        _scrollDelay = .18; ++_scrollEvents;
    }
}
