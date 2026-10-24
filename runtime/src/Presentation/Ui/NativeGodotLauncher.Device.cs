using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    // Canonical installation target for the original, delivered v1 GLB. The producer's
    // static CRT artwork is never used as launcher state or as an input surface.
    private const string DeviceScenePath = "res://assets/launcher/device/launcher_device.glb";
    private static readonly Vector2I DeviceCanvasSize = new(1200, 900);
    private static readonly Vector3 DeviceScreenCenter = new(-.07f, .22f, .114f);
    private readonly List<DevicePickSurface> _devicePickSurfaces = [];
    private readonly Dictionary<string, Node3D> _deviceControls = new(StringComparer.Ordinal);
    private SubViewport? _deviceView;
    private SubViewport? _deviceCanvas;
    private Node3D? _deviceModel;
    private Camera3D? _deviceCamera;
    private TextureRect? _deviceImage;
    private Control? _deviceInterface;
    private HBoxContainer? _deviceViewControls;
    private Button? _deviceStandardButton;
    private Button? _deviceReadButton;
    private Label? _deviceAvailability;
    private Window? _deviceHostWindow;
    private string? _devicePressedControl;
    private Transform3D _deviceUnpressedTransform;
    private Vector2? _deviceLastPointer;
    private bool _deviceLeftDown;
    private bool _deviceStandardView;
    private bool _deviceReadScreen;
    private float _deviceRangeAngle;

    internal string DevicePresentationStatus { get; private set; } = "original-device-not-installed";
    internal bool DevicePresentationConstructed => _deviceModel is not null && _deviceCanvas is not null;

    private sealed record DevicePickSurface(MeshInstance3D Node, Vector3[] Vertices, int[] Indices,
        Vector2[] Uvs, bool Screen, string? Control);
    private sealed record DeviceHit(Vector2? Uv, string? Control);

    private void TryBuildDevice(Control interfaceRoot)
    {
        if (!ResourceLoader.Exists(DeviceScenePath))
        {
            SetDeviceUnavailable("Original device model is not installed. The standard launcher remains available.");
            return;
        }
        try
        {
            _deviceView = new SubViewport
            {
                Name = "DeviceRenderViewport",
                OwnWorld3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = true,
            };
            AddChild(_deviceView);
            var scene = ResourceLoader.Load<PackedScene>(DeviceScenePath)
                ?? throw new InvalidDataException("Original device GLB could not be loaded.");
            _deviceModel = scene.Instantiate<Node3D>();
            _deviceView.AddChild(_deviceModel);
            var nodes = _deviceModel.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(_deviceModel).ToArray();
            var screen = nodes.OfType<MeshInstance3D>().Single(node => node.Name == "CRT_Screen");
            foreach (var name in new[] { "CONTROL_MainTuning_pivotY", "CONTROL_Range_pivotY",
                         "CONTROL_Toggle_0_pivotX", "CONTROL_Toggle_1_pivotX",
                         "CONTROL_FunctionKey_0", "CONTROL_FunctionKey_1", "CONTROL_FunctionKey_2", "CONTROL_FunctionKey_3" })
                _deviceControls.Add(name, nodes.Single(node => node.Name == name));
            CacheDevicePicking(nodes, screen);

            _deviceCanvas = new SubViewport
            {
                Name = "LiveLauncherCanvas",
                Size = DeviceCanvasSize,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = true,
            };
            AddChild(_deviceCanvas);
            var phosphor = new ColorRect
            {
                Name = "DeviceCanvasBackdrop",
                Color = new Color("071109"),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            phosphor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _deviceCanvas.AddChild(phosphor);
            var screenMaterial = new StandardMaterial3D
            {
                ResourceName = "Live launcher viewport",
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = _deviceCanvas.GetTexture(),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            screen.MaterialOverride = screenMaterial;
            foreach (var glass in nodes.OfType<MeshInstance3D>().Where(node => node.Name == "CRT_Glass"))
            {
                glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                glass.MaterialOverride = new StandardMaterial3D
                {
                    ResourceName = "Original instrument glass",
                    AlbedoColor = new Color(.65f, .85f, .7f, .06f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = .18f,
                    DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                };
            }
            _deviceCamera = new Camera3D { Name = "DeviceCamera", Current = true, Fov = 32, Near = .01f, Far = 8 };
            _deviceView.AddChild(_deviceCamera);
            var environment = new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color("101410"),
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color("b8c1ac"),
                    AmbientLightEnergy = .45f,
                    TonemapMode = Godot.Environment.ToneMapper.Filmic,
                },
            };
            _deviceView.AddChild(environment);
            var key = new DirectionalLight3D { RotationDegrees = new Vector3(-35, -28, 0), LightEnergy = .8f, ShadowEnabled = true };
            _deviceView.AddChild(key);
            var fill = new OmniLight3D { Position = new Vector3(-.45f, .3f, .5f), LightColor = new Color("9eb4a2"), LightEnergy = .25f, OmniRange = 2 };
            _deviceView.AddChild(fill);
            _deviceImage = new TextureRect
            {
                Name = "OriginalDeviceView",
                Texture = _deviceView.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _deviceImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_deviceImage);
            MoveChild(_deviceImage, 0);
            _deviceInterface = interfaceRoot;
            interfaceRoot.Theme = Theme;
            interfaceRoot.Reparent(_deviceCanvas, false);
            interfaceRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            GetNode<Control>("PipBoyHousing").Hide();
            GetNode<Control>("LauncherBackdrop").Hide();
            BuildDeviceViewControls();
            _deviceHostWindow = GetWindow();
            _deviceHostWindow.FocusExited += CancelDevicePointer;
            DevicePresentationStatus = "original-device-live-canvas-constructed";
            if (_deviceAvailability is not null)
            {
                _deviceAvailability.Text = "";
                _deviceAvailability.TooltipText = "";
            }
            ResizeDevice();
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree() && !_deviceStandardView)
                    _search.GrabFocus();
            }).CallDeferred();
        }
        catch (Exception error)
        {
            RetireDevice();
            SetDeviceUnavailable("Device view could not be prepared: " + error.Message);
        }
    }

    private void SetDeviceUnavailable(string reason)
    {
        DevicePresentationStatus = reason;
        if (_deviceAvailability is null) return;
        _deviceAvailability.Text = "Device view unavailable";
        _deviceAvailability.TooltipText = reason;
        GD.PushWarning(reason);
    }

    private void CacheDevicePicking(IEnumerable<Node3D> nodes, MeshInstance3D screen)
    {
        foreach (var mesh in nodes.OfType<MeshInstance3D>())
        {
            if (mesh.Name == "CRT_Glass" || mesh.Mesh is null) continue;
            if (mesh.Mesh is not ArrayMesh sourceMesh)
                throw new InvalidDataException("Original device GLB requires imported array meshes.");
            string? control = null;
            for (Node? parent = mesh; parent is not null && parent != _deviceModel; parent = parent.GetParent())
                if (_deviceControls.ContainsKey(parent.Name.ToString())) { control = parent.Name.ToString(); break; }
            for (var surface = 0; surface < sourceMesh.GetSurfaceCount(); surface++)
            {
                if (sourceMesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles)
                    throw new InvalidDataException("Original device requires triangle pick surfaces.");
                using var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                if (indices.Length == 0) indices = Enumerable.Range(0, vertices.Length).ToArray();
                if (indices.Length == 0 || indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= vertices.Length) || vertices.Any(v => !v.IsFinite()))
                    throw new InvalidDataException("Original device has invalid pick geometry: " + mesh.Name);
                if (mesh == screen && (uvs.Length != vertices.Length || uvs.Any(uv => !uv.IsFinite() || uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1)))
                    throw new InvalidDataException("CRT screen requires the producer's continuous 0..1 UVs.");
                if (mesh == screen)
                {
                    var toModel = _deviceModel!.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var uv = uvs[vertex];
                        var expected = new Vector3(-.07f + .288f * (uv.X - .5f), .22f + .216f * (.5f - uv.Y),
                            .1f + .014f * (1 - Mathf.Pow(2 * uv.X - 1, 2)) * (1 - Mathf.Pow(2 * uv.Y - 1, 2)));
                        if ((toModel * vertices[vertex]).DistanceTo(expected) > .0015f)
                            throw new InvalidDataException("Imported CRT geometry/UV orientation differs from producer contract v1.0.0.");
                    }
                }
                _devicePickSurfaces.Add(new(mesh, vertices, indices, uvs, mesh == screen, control));
            }
        }
        if (!_devicePickSurfaces.Any(surface => surface.Screen)) throw new InvalidDataException("CRT screen has no triangles.");
        foreach (var name in _deviceControls.Keys)
            if (!_devicePickSurfaces.Any(surface => surface.Control == name))
                throw new InvalidDataException("Physical control has no pick geometry: " + name);
    }

    private DeviceHit? PickDevice(Vector2 point)
    {
        if (_deviceCamera is null) return null;
        var origin = _deviceCamera.ProjectRayOrigin(point);
        var direction = _deviceCamera.ProjectRayNormal(point).Normalized();
        var nearest = float.PositiveInfinity;
        DeviceHit? result = null;
        foreach (var surface in _devicePickSurfaces)
        {
            if (!surface.Node.IsVisibleInTree()) continue;
            var inverse = surface.Node.GlobalTransform.AffineInverse();
            var localOrigin = inverse * origin;
            // Do not normalize after transforming: the ray parameter remains world distance.
            var localDirection = inverse.Basis * direction;
            for (var i = 0; i < surface.Indices.Length; i += 3)
            {
                var a = surface.Indices[i]; var b = surface.Indices[i + 1]; var c = surface.Indices[i + 2];
                var edge1 = surface.Vertices[b] - surface.Vertices[a];
                var edge2 = surface.Vertices[c] - surface.Vertices[a];
                var cross = localDirection.Cross(edge2); var determinant = edge1.Dot(cross);
                if (MathF.Abs(determinant) < 1e-9f) continue;
                var offset = localOrigin - surface.Vertices[a]; var u = offset.Dot(cross) / determinant;
                if (u is < 0 or > 1) continue;
                var q = offset.Cross(edge1); var v = localDirection.Dot(q) / determinant;
                if (v < 0 || u + v > 1) continue;
                var distance = edge2.Dot(q) / determinant;
                if (!float.IsFinite(distance) || distance < 0 || distance >= nearest) continue;
                nearest = distance;
                var uv = surface.Screen ? surface.Uvs[a] * (1 - u - v) + surface.Uvs[b] * u + surface.Uvs[c] * v : (Vector2?)null;
                result = new(uv, surface.Control);
            }
        }
        return result;
    }

    private void BuildDeviceViewControls()
    {
        _deviceViewControls = new HBoxContainer { Name = "DeviceViewControls", Position = new Vector2(16, 12) };
        _deviceViewControls.AddThemeConstantOverride("separation", 8);
        _deviceReadButton = ActionButton("Read screen", 38);
        _deviceReadButton.TooltipText = "Move closer to the live CRT screen";
        _deviceReadButton.Pressed += () =>
        {
            CancelDevicePointer(); _deviceReadScreen = !_deviceReadScreen; ResizeDevice();
            GetViewport().GuiReleaseFocus(); _search.GrabFocus();
        };
        _deviceViewControls.AddChild(_deviceReadButton);
        _deviceStandardButton = ActionButton("Standard view (F2)", 38);
        _deviceStandardButton.TooltipText = "Use the same live controls at full window size, with normal keyboard focus";
        _deviceStandardButton.Pressed += ToggleDeviceStandardView;
        _deviceViewControls.AddChild(_deviceStandardButton);
        AddChild(_deviceViewControls);
    }

    private void ToggleDeviceStandardView()
    {
        if (_deviceInterface is null || _deviceCanvas is null || _deviceImage is null) return;
        CancelDevicePointer();
        var focus = _deviceStandardView ? GetViewport().GuiGetFocusOwner() : _deviceCanvas.GuiGetFocusOwner();
        _deviceStandardView = !_deviceStandardView;
        _deviceInterface.Reparent(_deviceStandardView ? this : _deviceCanvas, false);
        _deviceInterface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _deviceImage.Visible = !_deviceStandardView;
        GetNode<Control>("PipBoyHousing").Visible = _deviceStandardView;
        GetNode<Control>("LauncherBackdrop").Visible = _deviceStandardView;
        if (_deviceViewControls is not null) MoveChild(_deviceViewControls, GetChildCount() - 1);
        if (_deviceStandardButton is not null) _deviceStandardButton.Text = _deviceStandardView ? "Device view (F2)" : "Standard view (F2)";
        if (_deviceReadButton is not null) _deviceReadButton.Visible = !_deviceStandardView;
        // A standard-view toggle occupies the top gutter, outside the live controls.
        if (_deviceViewControls is not null) _deviceViewControls.Position = new Vector2(_deviceStandardView ? MathF.Max(16, Size.X / 2 - 90) : 16, 12);
        if (focus is not null && GodotObject.IsInstanceValid(focus) && focus.IsVisibleInTree()) focus.GrabFocus();
        else if (!_deviceStandardView) { GetViewport().GuiReleaseFocus(); _search.GrabFocus(); }
    }

    private void ResizeDevice()
    {
        if (_deviceView is null || _deviceCamera is null) return;
        _deviceView.Size = new Vector2I(Math.Max(1, (int)Size.X), Math.Max(1, (int)Size.Y));
        var aspect = _deviceView.Size.X / (float)_deviceView.Size.Y;
        var height = _deviceReadScreen ? .216f : .4615f;
        var width = _deviceReadScreen ? .288f : .572f;
        var target = _deviceReadScreen ? DeviceScreenCenter : new Vector3(0, .23f, 0);
        var distance = MathF.Max(height, width / aspect) / (2 * MathF.Tan(Mathf.DegToRad(_deviceCamera.Fov / 2))) * 1.25f;
        _deviceCamera.Position = target + (_deviceReadScreen ? Vector3.Back : new Vector3(.13f, .08f, 1).Normalized()) * distance;
        _deviceCamera.LookAt(target, Vector3.Up);
        if (_deviceReadButton is not null) _deviceReadButton.Text = _deviceReadScreen ? "Whole device" : "Read screen";
    }

    public override void _Input(InputEvent input)
    {
        if (_deviceCanvas is null || _deviceModel is null) return;
        if (_fileDialog is not null || _stackWindow is not null) { CancelDevicePointer(); return; }
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F2 })
        {
            ToggleDeviceStandardView(); GetViewport().SetInputAsHandled(); return;
        }
        if (_deviceStandardView) return;
        if (input is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
        {
            var focus = GetViewport().GuiGetFocusOwner();
            if (focus is not null && _deviceViewControls is not null && _deviceViewControls.IsAncestorOf(focus)) return;
            _deviceCanvas.PushInput(input, true);
            if (_deviceCanvas.IsInputHandled()) GetViewport().SetInputAsHandled();
            return;
        }
        if (input is not InputEventMouse mouse) return;
        if (_deviceViewControls is not null && _deviceViewControls.GetGlobalRect().HasPoint(mouse.Position))
        {
            CancelDevicePointer(); return;
        }
        var hit = PickDevice(mouse.Position);
        if (input is InputEventMouseButton { Pressed: true }) GetViewport().GuiReleaseFocus();
        if (_devicePressedControl is not null)
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                var name = _devicePressedControl;
                ReleaseDeviceControl();
                if (hit?.Control == name) ActivateDeviceControl(name, 1);
            }
            GetViewport().SetInputAsHandled(); return;
        }
        if (hit?.Uv is { } uv)
        {
            var point = uv * new Vector2(DeviceCanvasSize.X, DeviceCanvasSize.Y);
            if (_deviceLastPointer is null) _deviceCanvas.NotifyMouseEntered();
            if (input is InputEventMouseMotion)
                NativeMenuPointerInput.Motion(_deviceCanvas, point, _deviceLastPointer is { } last ? point - last : Vector2.Zero, _deviceLeftDown);
            else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } left)
            {
                NativeMenuPointerInput.Motion(_deviceCanvas, point, Vector2.Zero, _deviceLeftDown);
                _deviceLeftDown = left.Pressed;
                NativeMenuPointerInput.LeftButton(_deviceCanvas, point, left.Pressed);
            }
            else
            {
                using var copy = (InputEventMouse)input.Duplicate();
                copy.Position = copy.GlobalPosition = point;
                _deviceCanvas.PushInput(copy, true);
            }
            _deviceLastPointer = point;
            GetViewport().SetInputAsHandled(); return;
        }
        CancelDevicePointer();
        if (hit?.Control is not { } control || input is not InputEventMouseButton { Pressed: true } button) return;
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (control is "CONTROL_MainTuning_pivotY" or "CONTROL_Range_pivotY")
                ActivateDeviceControl(control, button.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
        }
        else if (button.ButtonIndex == MouseButton.Left)
        {
            _devicePressedControl = control;
            var node = _deviceControls[control];
            _deviceUnpressedTransform = node.Transform;
            if (control.StartsWith("CONTROL_FunctionKey_", StringComparison.Ordinal))
                node.TranslateObjectLocal(Vector3.Forward * .003f); // Blender +Y -> glTF -Z.
        }
        GetViewport().SetInputAsHandled();
    }

    private void ActivateDeviceControl(string name, int direction)
    {
        switch (name)
        {
            case "CONTROL_FunctionKey_0": PressDeviceButton(_launch); break;
            case "CONTROL_FunctionKey_1": PressDeviceButton(_setupToggle); break;
            case "CONTROL_FunctionKey_2": SetSetupExpanded(false); _setupToggle.GrabFocus(); break;
            case "CONTROL_FunctionKey_3": PressDeviceButton(FindChild("QuitLauncher", true, false) as Button); break;
            case "CONTROL_MainTuning_pivotY":
            case "CONTROL_Range_pivotY":
                var games = _campaigns.Where(campaign => FallbackCampaigns.ContainsKey(campaign.Id) &&
                    _campaignButtons.TryGetValue(campaign.Id, out var button) && button.IsVisibleInTree() && !button.Disabled)
                    .Select(campaign => campaign.Id).ToArray();
                var index = Array.IndexOf(games, _selectedGameId);
                if (games.Length == 0) return;
                var nextIndex = index < 0 ? direction > 0 ? 0 : games.Length - 1 : (index + direction + games.Length) % games.Length;
                PressDeviceButton(_campaignButtons[games[nextIndex]]);
                var angle = direction * 30f;
                if (name == "CONTROL_Range_pivotY")
                {
                    var next = Mathf.Clamp(_deviceRangeAngle + angle, -45, 45);
                    angle = next - _deviceRangeAngle; _deviceRangeAngle = next;
                }
                _deviceControls[name].RotateObjectLocal(Vector3.Forward, Mathf.DegToRad(angle));
                break;
            case "CONTROL_Toggle_0_pivotX":
            case "CONTROL_Toggle_1_pivotX":
                var toggle = _deviceControls[name];
                var on = toggle.HasMeta("launcher_toggle_on") && toggle.GetMeta("launcher_toggle_on").AsBool();
                toggle.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(on ? -36 : 36));
                toggle.SetMeta("launcher_toggle_on", !on); // Cosmetic only, as specified by the producer.
                break;
        }
    }

    private static void PressDeviceButton(Button? button)
    {
        if (button is null || button.Disabled || !button.IsVisibleInTree()) return;
        button.EmitSignal(Button.SignalName.Pressed);
    }

    private void ReleaseDeviceControl()
    {
        if (_devicePressedControl is { } name && _deviceControls.TryGetValue(name, out var node) && GodotObject.IsInstanceValid(node))
            node.Transform = _deviceUnpressedTransform;
        _devicePressedControl = null;
    }

    private void CancelDevicePointer()
    {
        ReleaseDeviceControl();
        if (_deviceCanvas is not null && GodotObject.IsInstanceValid(_deviceCanvas) && _deviceCanvas.IsInsideTree())
        {
            if (_deviceLeftDown) NativeMenuPointerInput.CancelLeft(_deviceCanvas);
            if (_deviceLastPointer is not null) _deviceCanvas.NotifyMouseExited();
        }
        _deviceLeftDown = false; _deviceLastPointer = null;
    }

    private void RetireDevice()
    {
        CancelDevicePointer();
        if (_deviceHostWindow is not null && GodotObject.IsInstanceValid(_deviceHostWindow))
            _deviceHostWindow.FocusExited -= CancelDevicePointer;
        _deviceHostWindow = null;
        if (_deviceInterface is not null && GodotObject.IsInstanceValid(_deviceInterface) && _deviceInterface.IsInsideTree() && _deviceInterface.GetParent() != this)
        {
            _deviceInterface.Reparent(this, false);
            _deviceInterface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        foreach (var node in new Node?[] { _deviceViewControls, _deviceImage, _deviceView, _deviceCanvas })
            if (node is not null && GodotObject.IsInstanceValid(node)) node.QueueFree();
        _deviceViewControls = null; _deviceImage = null; _deviceView = null; _deviceCanvas = null;
        _deviceModel = null; _deviceCamera = null; _deviceInterface = null;
        _deviceControls.Clear(); _devicePickSurfaces.Clear();
        GetNodeOrNull<Control>("PipBoyHousing")?.Show();
        GetNodeOrNull<Control>("LauncherBackdrop")?.Show();
    }
}
