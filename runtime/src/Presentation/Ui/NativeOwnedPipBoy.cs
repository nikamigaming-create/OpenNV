using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.Presentation.OpenXR;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Live NIF device, live menu pixels and one input projection.</summary>
internal sealed partial class NativeOwnedPipBoy : Control
{
    private readonly NativeOwnedRenderedDevice? _device;
    private readonly NativeOwnedDeviceSurface _surface;
    private readonly MeshInstance3D _screen;
    private readonly bool _worldSpace;
    private readonly Material? _originalScreenMaterial;
    private readonly SubViewport _canvas;
    private readonly NativeOwnedPipBoyMenu _menu;
    private readonly Action _close;
    private readonly string _toggleAction;
    private readonly List<(MeshInstance3D Mesh, NativeBitmapMenuButton Button)> _buttons = [];
    private readonly Dictionary<MeshInstance3D, NativeBitmapMenuButton> _buttonParts = [];
    private static readonly string[] Glows = ["StatsGlow:0", "ItemsGlow:0", "DataGlow:0"];
    private Vector2? _lastPointer;
    private readonly RuntimeNativePlayerActor _player;
    private double _openSeconds;
    private double _idleSeconds;
    private bool _closing;
    private bool _xrAwaitTriggerRelease;
    private bool _xrPressed;
    private bool _xrFocused;
    private ColorRect? _xrCursor;
    private NativeXrWristPresentation? _xrDevice;
    internal object State => new
    {
        menu = _menu.State,
        openingSeconds = _openSeconds,
        durationSeconds = _player.PipBoyDuration,
        closing = _closing,
        camera = _device?.Camera.Transform.ToString(),
        screen = _screen.GlobalTransform.ToString(),
        xr = !_worldSpace ? null : new
        {
            hand = _player.XrHandState,
            focused = _xrFocused,
            device = _xrDevice?.State,
            pressed = _xrPressed,
            pointer = _lastPointer?.ToString(),
            hit = _surface.PickedGeometry?.GetMeta("opennv_nif_source_name", "").AsString(),
            targets = _buttons.Select(value => new
            {
                name = value.Button.Text,
                surfaces = _buttonParts.Where(part => part.Value == value.Button).Select(part => new
                {
                    name = part.Key.GetMeta("opennv_nif_source_name", "").AsString(),
                    position = Vector(part.Key.ToGlobal(part.Key.GetAabb().GetCenter()))
                }).ToArray()
            }).ToArray(),
            screenVertices = _surface.ScreenVertices
                .Select(vertex => Vector(_screen.ToGlobal(vertex))).ToArray(),
            screenUvs = _surface.ScreenUvs
                .Select(uv => new[] { uv.X, uv.Y }).ToArray(),
            screenIndices = _surface.ScreenIndices
        }
    };
    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
    internal NativeOwnedPipBoy(NativeOwnedPipBoyMenu menu, FalloutInstallationSettings settings, RuntimeNativePlayerActor player,
        bool female, Color ambient, string toggleAction, Action close, bool worldSpace = false)
    {
        Name = "OwnedPipBoy"; ProcessMode = ProcessModeEnum.Always;
        _menu = menu; _close = close; _toggleAction = toggleAction;
        _player = player; _worldSpace = worldSpace;
        try
        {
            if (worldSpace)
            {
                var device = player.WristDevice() ?? throw new InvalidOperationException("The player has no equipped wrist device.");
                _surface = new(device.Path, device.Root, "pipboyscreen:0");
            }
            else
            {
                player.PosePipBoy(0, 0);
                _device = new(female ? "meshes/pipboy3000/pipboyarmfemale.nif" : "meshes/pipboy3000/pipboyarm.nif", settings, pipBoy: true, player: player);
                AddChild(_device); _surface = _device.Surface;
            }
            _screen = _surface.Geometry(_surface.ScreenName);
            _originalScreenMaterial = _screen.MaterialOverride;
            // The source light-effect mesh belongs to the flashlight presentation,
            // not the open menu. Leaving it on paints an additive green sheet over
            // the CRT. Menu light and world flashlight are separate device states.
            _surface.Geometry("PipboyLightEffect:0").Visible = false;
            foreach (var mesh in _surface.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
            {
                NativeNifMaterialEnvironment.Bind(mesh, new(ambient.R, ambient.G, ambient.B), Vector3.Zero, new(100000, 200000, 1), 1);
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                    if (mesh.GetActiveMaterial(surface) is ShaderMaterial shader)
                    {
                        if (shader.ResourceName == NativeNifEffectMaterial.ResourceIdentity) shader.SetShaderParameter("source_store_encoded", !worldSpace);
                        if (shader.ResourceName is NativeNifLightingMaterial.ResourceIdentity or NativeFaceGenMaterial.ResourceIdentity)
                            NativeNifPointLighting.Bind(shader, [], 1, storeEncoded: !worldSpace);
                    }
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                    if (mesh.GetActiveMaterial(surface) is ShaderMaterial { ResourceName: NativeNifLightingMaterial.ResourceIdentity } material &&
                        (material.GetMeta("opennv_nif_shader_flags").AsUInt32() & (1u << 25)) != 0)
                        foreach (var slot in new[] { "base_map", "normal_map" })
                        {
                            if (material.GetShaderParameter(slot).AsGodotObject() is not Texture2D texture || !texture.HasMeta("opennv_logical_texture")) continue;
                            var path = texture.GetMeta("opennv_logical_texture").AsString().Replace('\\', '/');
                            const string prefix = "textures/pipboy3000/xbox/";
                            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                            var member = path[prefix.Length..];
                            if (member.Contains('/')) member = settings.Require("General", "sLanguage").ToLowerInvariant() + member[member.IndexOf('/')..];
                            var selected = "textures/pipboy3000/pc/" + member;
                            material.SetShaderParameter(slot, NativeOwnedMediaLoader.LoadTexture(selected));
                            material.SetMeta("opennv_device_" + slot, selected);
                        }
            }
            _canvas = new SubViewport
            {
                Name = "PipBoySourceCanvas",
                Size = new(1280, 960),
                Disable3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            AddChild(_canvas); _canvas.AddChild(menu);
            _screen.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Back,
                AlbedoTexture = _canvas.GetTexture(),
                AlbedoColor = Colors.White,
                AlbedoTextureForceSrgb = false,
            };
            var font = NativeBitmapFontAsset.Read(settings, 2);
            string[] labels = ["STAT", "ITEMS", "DATA"];
            for (var index = 0; index < labels.Length; index++)
            {
                var button = new NativeBitmapMenuButton(font.Font, font.Atlas, Colors.White) { Text = labels[index], DrawText = false };
                var page = (FalloutPipBoyPage)index;
                button.Pressed += () => menu.Select(page, page == FalloutPipBoyPage.Data ? 1 : 0);
                AddChild(button); _buttons.Add((_surface.Geometry($"PipBoyButton0{index + 1}:0"), button));
                foreach (var geometry in _surface.GeometryParts($"PipBoyButton0{index + 1}")) _buttonParts.Add(geometry, button);
                _buttonParts.Add(_surface.Geometry(Glows[index]), button);
            }
            menu.PageChanged += UpdateGlows;
            SetMeta("opennv_pipboy_source", "owned-NIF/XML/DDS;authoritative-inventory-quests-map-markers");
            SetMeta("opennv_pipboy_unbound", "raise-lower-blend-policy,dial-button-animation,CRT-postprocess,physical-XR-attachment,matched-retail-projection");
            if (worldSpace)
            {
                _xrDevice = new(_surface);
                _xrCursor = new ColorRect
                {
                    Size = new(10, 10),
                    Color = Colors.White,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Visible = false
                };
                _canvas.AddChild(_xrCursor);
            }
            Resized += Layout;
        }
        catch
        {
            RestoreScreenMaterial();
            Free();
            throw;
        }
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); Layout(); UpdateGlows();
    }
    public override void _Process(double delta)
    {
        if (_worldSpace)
        {
            _xrDevice?.Advance(delta, _xrFocused);
            if (_closing) { _closing = false; _close(); }
            return;
        }
        _openSeconds = Math.Clamp(_openSeconds + (_closing ? -delta : delta), 0, _player.PipBoyDuration);
        _idleSeconds += delta;
        _device!.PosePipBoy(_idleSeconds, (float)(_openSeconds / _player.PipBoyDuration), _openSeconds); Layout();
        foreach (var (_, button) in _buttons) button.Disabled = _closing || _openSeconds < _player.PipBoyDuration;
        if (_closing && _openSeconds == 0) { SetProcess(false); _close(); }
    }
    private void UpdateGlows()
    {
        // The generated source tiles carry the current page; no second UI state.
        var state = _menu.GetMeta("opennv_page", 0).AsInt32();
        for (var index = 0; index < Glows.Length; index++) _surface.Geometry(Glows[index]).Visible = index == state;
    }
    private void Layout()
    {
        if (_worldSpace) return;
        if (!IsInsideTree() || Size.X <= 0 || Size.Y <= 0) return;
        _device!.Size = Size;
        foreach (var (mesh, button) in _buttons)
        {
            var vertices = mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var points = vertices.Select(vertex => _device!.Camera.UnprojectPosition(mesh.ToGlobal(vertex))).ToArray();
            var min = new Vector2(points.Min(point => point.X), points.Min(point => point.Y));
            var max = new Vector2(points.Max(point => point.X), points.Max(point => point.Y));
            button.Position = min - Vector2.One * 8; button.Size = max - min + Vector2.One * 16;
        }
    }
    public override void _Input(InputEvent input)
    {
        if (_worldSpace) return;
        if (input.IsActionPressed(_toggleAction) || input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        { _closing = true; GetViewport().SetInputAsHandled(); return; }
        if (_closing || _openSeconds < _player.PipBoyDuration) { GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouse mouse)
        {
            if (_buttons.Any(target => target.Button.GetGlobalRect().HasPoint(mouse.Position))) return;
            var uv = _device!.PickScreen(mouse.Position - GlobalPosition);
            // Keep a captured map drag at its last valid point when the cursor
            // leaves the physical screen. The outside sentinel is only a hover
            // location; applying it as a drag delta would fling the whole map.
            if (uv is null && mouse is InputEventMouseMotion drag && (drag.ButtonMask & MouseButtonMask.Left) != 0)
            { GetViewport().SetInputAsHandled(); return; }
            var point = uv is { } value ? value * new Vector2(1280, 960) : new Vector2(-1, -1);
            using var copy = (InputEventMouse)mouse.Duplicate(); copy.Position = copy.GlobalPosition = point;
            if (copy is InputEventMouseMotion motion) motion.Relative = _lastPointer is { } previous ? point - previous : Vector2.Zero;
            _lastPointer = point; _canvas.PushInput(copy, true); GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventKey)
        { _canvas.PushInput(input, true); GetViewport().SetInputAsHandled(); }
    }
    internal void RequestClose() => _closing = true;
    public override void _ExitTree() => RestoreScreenMaterial();
    private void RestoreScreenMaterial()
    {
        if (_worldSpace && IsInstanceValid(_screen))
        {
            _screen.MaterialOverride = _originalScreenMaterial;
        }
        _xrDevice?.Dispose(); _xrDevice = null;
    }
    internal void PoseXrDevice(Transform3D head) => _xrDevice?.Publish(head);
    internal bool PointFromXr(Transform3D aim, bool tracked, bool pressed)
    {
        if (_closing || !_xrFocused) return false;
        if (!tracked) { ReleaseXrPointer(); _xrAwaitTriggerRelease = true; return false; }
        if (_xrAwaitTriggerRelease)
        {
            if (!pressed) _xrAwaitTriggerRelease = false;
            pressed = false;
        }
        var uv = _surface.PickScreen(aim.Origin, -aim.Basis.Z);
        var point = uv is { } value ? value * new Vector2(1280, 960) : new Vector2(-1, -1);
        if (uv is not null || !_xrPressed)
        {
            using var motion = new InputEventMouseMotion
            {
                Position = point,
                GlobalPosition = point,
                Relative = _lastPointer is { } previous ? point - previous : Vector2.Zero,
                ButtonMask = _xrPressed ? MouseButtonMask.Left : 0
            };
            _canvas.PushInput(motion, true); _lastPointer = point;
        }
        if (_xrCursor is not null) { _xrCursor.Visible = uv is not null; _xrCursor.Position = point - _xrCursor.Size / 2; }
        if (_xrPressed != pressed)
        {
            if (pressed && _surface.PickedGeometry is { } part && _buttonParts.TryGetValue(part, out var button))
                button.EmitSignal(BaseButton.SignalName.Pressed);
            else
            {
                using var click = new InputEventMouseButton
                {
                    Position = _lastPointer ?? point,
                    GlobalPosition = _lastPointer ?? point,
                    ButtonIndex = MouseButton.Left,
                    Pressed = pressed
                };
                _canvas.PushInput(click, true);
            }
            _xrPressed = pressed;
        }
        return uv is not null || _surface.PickedGeometry is not null;
    }
    internal void SetXrFocus(bool focused)
    {
        ReleaseXrPointer();
        _xrFocused = focused; _closing = false;
        if (focused) _xrAwaitTriggerRelease = true;
    }
    private void ReleaseXrPointer()
    {
        if (_xrPressed)
        {
            using var release = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = _lastPointer ?? Vector2.Zero,
                GlobalPosition = _lastPointer ?? Vector2.Zero
            };
            _canvas.PushInput(release, true);
        }
        if (_xrCursor is not null) _xrCursor.Visible = false;
        _xrPressed = false;
    }
    internal void RefreshWorld(FalloutFormKey? world, Vector3 player, float heading) => _menu.RefreshWorld(world, player, heading);
}
