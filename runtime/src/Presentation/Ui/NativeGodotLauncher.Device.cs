using Godot;
using System.Text.Json;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    // Canonical installation target for the original, delivered v2 GLB. The producer's
    // static CRT artwork is never used as launcher state or as an input surface.
    private const string DeviceScenePath = "res://assets/launcher/device/launcher_device.glb";
    private static readonly Vector2I DeviceCanvasSize = new(1550, 1080);
    private static readonly Vector3 DeviceScreenCenter = new(-.099f, .263f, .078f);
    private readonly Dictionary<string, Vector2> _deviceMotionLimits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _deviceSelectorPoses = new(StringComparer.Ordinal);
    private StandardMaterial3D? _deviceReadyMaterial;
    private Aabb _deviceBounds;
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
    private ulong _devicePressedAt;
    private Transform3D _deviceUnpressedTransform;
    private bool _deviceStandardView;
    private bool _deviceReadScreen;
    private readonly Dictionary<string, Transform3D> _deviceControlRest = new(StringComparer.Ordinal);

    internal string DevicePresentationStatus { get; private set; } = "original-device-not-installed";
    internal bool DevicePresentationConstructed => _deviceModel is not null && _deviceCanvas is not null;
    internal string DeviceSelectorContractStatus { get; private set; } = "selector-contract-not-bound";
    internal Vector2? DeviceScreenUv(Vector2 point) => PickDevice(point)?.Uv;

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
            BindDeviceContract(nodes);
            foreach (var (name, node) in _deviceControls) _deviceControlRest.Add(name, node.Transform);
            CacheDevicePicking(nodes, screen);

            _deviceCanvas = new SubViewport
            {
                Name = "DedicatedInstrumentCanvas",
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
            BuildInstrumentReadout(_deviceCanvas);
            // Preserve the readout's design coordinates and square pixel scale
            // inside the wider physical v2 screen (310 x 216 mm).
            _instrumentReadout!.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            _instrumentReadout.Size = new Vector2(1200, 900);
            _instrumentReadout.Scale = new Vector2(1.2f, 1.2f);
            _instrumentReadout.Position = new Vector2(55, 0);
            var screenMaterial = new StandardMaterial3D
            {
                ResourceName = "Dedicated phosphor instrument readout",
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
                    AlbedoColor = new Color(.13f, .22f, .16f, .025f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = .25f,
                    ClearcoatEnabled = true,
                    Clearcoat = .15f,
                    DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                };
            }
            var readyLens = nodes.OfType<MeshInstance3D>().Single(node => node.Name == "StatusLamp_ReadyLens");
            _deviceReadyMaterial = new StandardMaterial3D
            {
                ResourceName = "Source readiness indicator",
                AlbedoColor = new Color("30210e"),
                Roughness = .25f,
                Emission = new Color("ffb849"),
                EmissionEnabled = false,
            };
            readyLens.MaterialOverride = _deviceReadyMaterial;
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
                    AmbientLightEnergy = .30f,
                    TonemapMode = Godot.Environment.ToneMapper.Filmic,
                },
            };
            _deviceView.AddChild(environment);
            var key = new DirectionalLight3D { RotationDegrees = new Vector3(-35, -28, 0), LightEnergy = .55f, ShadowEnabled = true };
            _deviceView.AddChild(key);
            var fill = new OmniLight3D { Position = new Vector3(-.45f, .3f, .5f), LightColor = new Color("9eb4a2"), LightEnergy = .16f, OmniRange = 2 };
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
            // The accessible browser stays on its own surface. It is never
            // parented into, drawn on or used as input authority for the CRT.
            interfaceRoot.Hide();
            FocusMode = FocusModeEnum.All;
            GetNode<Control>("PipBoyHousing").Hide();
            GetNode<Control>("LauncherBackdrop").Hide();
            BuildDeviceViewControls();
            _deviceHostWindow = GetWindow();
            _deviceHostWindow.FocusExited += CancelDevicePointer;
            DevicePresentationStatus = "original-device-instrument-readout-constructed";
            if (_deviceAvailability is not null)
            {
                _deviceAvailability.Text = "";
                _deviceAvailability.TooltipText = "";
            }
            ResizeDevice();
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree() && !_deviceStandardView)
                    GrabFocus();
            }).CallDeferred();
        }
        catch (Exception error)
        {
            RetireDevice();
            SetDeviceUnavailable("Device view could not be prepared: " + error.Message);
        }
    }

    private void BindDeviceContract(Node3D[] nodes)
    {
        using var contract = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/launcher/device/integration_contract.json"));
        var root = contract.RootElement;
        if (root.GetProperty("version").GetString() != "2.1.0")
            throw new InvalidDataException("Device requires the verified v2.1 four-game selector contract.");
        var bounds = root.GetProperty("bounds_blender_m");
        var minimum = new Vector3(bounds[0][0].GetSingle(), bounds[2][0].GetSingle(), -bounds[1][1].GetSingle());
        var maximum = new Vector3(bounds[0][1].GetSingle(), bounds[2][1].GetSingle(), -bounds[1][0].GetSingle());
        if (!minimum.IsFinite() || !maximum.IsFinite() ||
            maximum.X <= minimum.X || maximum.Y <= minimum.Y || maximum.Z <= minimum.Z)
            throw new InvalidDataException("Device bounds are invalid.");
        _deviceBounds = new Aabb(minimum, maximum - minimum);
        foreach (var row in root.GetProperty("controls").EnumerateArray())
        {
            var exportedName = row.GetProperty("name").GetString()!;
            var metadata = row.GetProperty("metadata");
            var name = exportedName;
            if (exportedName == "CONTROL_Brightness_pivotY")
            {
                name = metadata.GetProperty("legacy_alias").GetString()!;
                if (name != "CONTROL_Range_pivotY") throw new InvalidDataException("Brightness semantic alias changed.");
            }
            var action = name switch
            {
                "CONTROL_MainTuning_pivotY" => "PreviousGame / NextGame",
                "CONTROL_Range_pivotY" => "ScreenBrightness",
                "CONTROL_Toggle_0_pivotX" => "AudioToggle",
                "CONTROL_Toggle_1_pivotX" => "ReadViewToggle",
                "CONTROL_FunctionKey_0" => "Play",
                "CONTROL_FunctionKey_1" => "Settings",
                "CONTROL_FunctionKey_2" => "Back",
                "CONTROL_FunctionKey_3" => "Quit",
                _ => throw new InvalidDataException("Unbound physical control: " + exportedName),
            };
            if (metadata.GetProperty("action").GetString() != action)
                throw new InvalidDataException("Physical control action changed: " + name);
            var node = nodes.Single(node => node.Name == exportedName);
            var actual = _deviceModel!.GlobalTransform.AffineInverse() * node.GlobalTransform;
            var position = row.GetProperty("position_godot_m");
            var expectedPosition = new Vector3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle());
            // C * BlenderBasis * inverse(C), where C maps (x,y,z) to (x,z,-y).
            var matrix = row.GetProperty("matrix_world_blender");
            var expectedBasis = new Basis(
                new Vector3(matrix[0][0].GetSingle(), matrix[2][0].GetSingle(), -matrix[1][0].GetSingle()),
                new Vector3(matrix[0][2].GetSingle(), matrix[2][2].GetSingle(), -matrix[1][2].GetSingle()),
                new Vector3(-matrix[0][1].GetSingle(), -matrix[2][1].GetSingle(), matrix[1][1].GetSingle()));
            if (actual.Origin.DistanceTo(expectedPosition) > .0001f ||
                actual.Basis.X.DistanceTo(expectedBasis.X) > .0001f ||
                actual.Basis.Y.DistanceTo(expectedBasis.Y) > .0001f ||
                actual.Basis.Z.DistanceTo(expectedBasis.Z) > .0001f)
                throw new InvalidDataException("Imported physical pivot differs from producer contract: " + name);
            if (name.StartsWith("CONTROL_FunctionKey_", StringComparison.Ordinal))
            {
                if (metadata.GetProperty("press_axis_local").GetString() != "+Y" ||
                    MathF.Abs(metadata.GetProperty("travel_m").GetSingle() - .003f) > .000001f)
                    throw new InvalidDataException("Physical key travel changed: " + name);
            }
            else
            {
                var axis = name.Contains("Toggle", StringComparison.Ordinal) ? "X" : "Y";
                if (metadata.GetProperty("rotation_axis_local").GetString() != axis)
                    throw new InvalidDataException("Physical control rotation axis changed: " + name);
                var limits = metadata.GetProperty("rotation_limits_degrees");
                var range = new Vector2(limits[0].GetSingle(), limits[1].GetSingle());
                if (!range.IsFinite() || range.X >= range.Y) throw new InvalidDataException("Physical control limits are invalid: " + name);
                _deviceMotionLimits.Add(name, range);
            }
            _deviceControls.Add(name, node);
        }
        if (_deviceControls.Count != InstrumentControls.Length || InstrumentControls.Any(name => !_deviceControls.ContainsKey(name)))
            throw new InvalidDataException("Device contract does not supply the eight semantic controls.");
        BindDeviceSelector(root.GetProperty("selector"), nodes);
    }

    private void BindDeviceSelector(JsonElement contract, Node3D[] nodes)
    {
        var gameOrder = CampaignIds.Where(FallbackCampaigns.ContainsKey).ToArray();
        if (!contract.GetProperty("application_order").EnumerateArray().Select(value => value.GetString()).SequenceEqual(gameOrder) ||
            contract.GetProperty("detent_degrees").GetSingle() != 90 ||
            contract.GetProperty("neutral_pointer_front_angle_degrees").GetSingle() != 90 ||
            contract.GetProperty("lever_child_tilt_radians").GetSingle() != 0 ||
            contract.GetProperty("object").GetString() != "CONTROL_MainTuning_pivotY")
            throw new InvalidDataException("Selector game order, rest direction or detent contract changed.");
        var selector = _deviceControls["CONTROL_MainTuning_pivotY"];
        var pointerName = contract.GetProperty("pointer_tip").GetString();
        if (string.IsNullOrWhiteSpace(pointerName))
            throw new InvalidDataException("Selector contract has no pointer-tip mesh name.");
        var pointer = nodes.OfType<MeshInstance3D>().Single(node => node.Name == pointerName);
        var toSelector = selector.GlobalTransform.AffineInverse() * pointer.GlobalTransform;
        var neutral = Vector3.Zero; var count = 0;
        if (pointer.Mesh is not ArrayMesh pointerMesh) throw new InvalidDataException("Selector pointer requires imported geometry.");
        for (var surface = 0; surface < pointerMesh.GetSurfaceCount(); surface++)
        {
            using var arrays = pointerMesh.SurfaceGetArrays(surface);
            foreach (var vertex in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                neutral += toSelector * vertex; count++;
            }
        }
        if (count == 0) throw new InvalidDataException("Selector pointer geometry is empty.");
        neutral /= count;
        if (!neutral.IsFinite() || neutral.Y <= 0 || MathF.Abs(neutral.X) > .0001f)
            throw new InvalidDataException("Selector pointer does not face up in its authored rest pose.");
        var positions = contract.GetProperty("game_positions").EnumerateArray().ToArray();
        if (positions.Length != gameOrder.Length) throw new InvalidDataException("Selector is missing game positions.");
        var limits = _deviceMotionLimits["CONTROL_MainTuning_pivotY"];
        for (var index = 0; index < positions.Length; index++)
        {
            var position = positions[index]; var game = position.GetProperty("game_id").GetString()!;
            var degrees = position.GetProperty("local_rotation_degrees").GetSingle();
            var front = position.GetProperty("front_angle_degrees").GetSingle();
            if (game != gameOrder[index] || !float.IsFinite(degrees) || !float.IsFinite(front) ||
                degrees < limits.X || degrees > limits.Y || MathF.Abs(degrees - (limits.X + index * 90)) > .0001f)
                throw new InvalidDataException("Selector keyed rotation does not match its declared detents.");
            var direction = new Basis(Vector3.Forward, Mathf.DegToRad(degrees)) * neutral;
            var actual = new Vector2(direction.X, direction.Y).Normalized();
            var expected = new Vector2(MathF.Cos(Mathf.DegToRad(front)), MathF.Sin(Mathf.DegToRad(front)));
            var marker = nodes.Single(node => node.Name == $"Selector_DetentPin_{index}");
            var pin = selector.GlobalTransform.AffineInverse() * marker.GlobalPosition;
            if (actual.DistanceTo(expected) > .001f || actual.DistanceTo(new Vector2(pin.X, pin.Y).Normalized()) > .001f)
                throw new InvalidDataException("Selector pointer and physical marker disagree for " + game);
            _deviceSelectorPoses.Add(game, degrees);
        }
        DeviceSelectorContractStatus = "four-game-dial-contract-bound";
    }

    private void PoseDeviceInstruments(float brightness, bool audioEnabled, bool readView, bool ready)
    {
        if (_deviceControls.TryGetValue("CONTROL_MainTuning_pivotY", out var selector) &&
            _deviceControlRest.TryGetValue("CONTROL_MainTuning_pivotY", out var rest) &&
            _devicePressedControl != "CONTROL_MainTuning_pivotY")
        {
            if (_deviceSelectorPoses.TryGetValue(InstrumentSelectedGame, out var degrees))
            {
                // Assign the keyed pose directly: 225 to -45 is the same
                // clockwise quarter-turn modulo360, without a reverse sweep.
                selector.Transform = new Transform3D(rest.Basis * new Basis(Vector3.Forward, Mathf.DegToRad(degrees)), rest.Origin);
                DeviceSelectorContractStatus = "four-game-dial-contract-bound";
            }
            else DeviceSelectorContractStatus = "selector-game-unmapped: " + InstrumentSelectedGame;
        }
        Pose("CONTROL_Range_pivotY", Vector3.Forward, Math.Clamp((brightness - .35f) / .65f, 0, 1));
        Pose("CONTROL_Toggle_0_pivotX", Vector3.Right, audioEnabled ? 1 : 0);
        Pose("CONTROL_Toggle_1_pivotX", Vector3.Right, readView ? 1 : 0);
        if (_deviceReadyMaterial is not null)
        {
            _deviceReadyMaterial.AlbedoColor = new Color(ready ? "b88732" : "30210e");
            _deviceReadyMaterial.EmissionEnabled = ready;
        }
        void Pose(string name, Vector3 axis, float fraction)
        {
            if (_devicePressedControl == name || !_deviceMotionLimits.TryGetValue(name, out var limits) ||
                !_deviceControlRest.TryGetValue(name, out var restPose) || !_deviceControls.TryGetValue(name, out var node)) return;
            var degrees = Mathf.Lerp(limits.X, limits.Y, fraction);
            node.Transform = new Transform3D(restPose.Basis * new Basis(axis, Mathf.DegToRad(degrees)), restPose.Origin);
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
        Aabb? observedBounds = null;
        foreach (var mesh in nodes.OfType<MeshInstance3D>())
        {
            if (mesh.Name == "CRT_Glass" || mesh.Mesh is null) continue;
            if (mesh.Mesh is not ArrayMesh sourceMesh)
                throw new InvalidDataException("Original device GLB requires imported array meshes.");
            string? control = null;
            for (Node? parent = mesh; parent is not null && parent != _deviceModel; parent = parent.GetParent())
            {
                var binding = _deviceControls.FirstOrDefault(pair => pair.Value == parent);
                if (binding.Key is not null) { control = binding.Key; break; }
            }
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
                var toModel = _deviceModel!.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
                foreach (var vertex in vertices)
                {
                    var point = toModel * vertex;
                    observedBounds = observedBounds?.Expand(point) ?? new Aabb(point, Vector3.Zero);
                }
                if (mesh == screen && (uvs.Length != vertices.Length || uvs.Any(uv => !uv.IsFinite() || uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1)))
                    throw new InvalidDataException("CRT screen requires the producer's continuous 0..1 UVs.");
                if (mesh == screen)
                {
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var uv = uvs[vertex];
                        var x = 2 * uv.X - 1; var y = 1 - 2 * uv.Y;
                        var radius = Mathf.Pow(Mathf.Pow(MathF.Abs(x), 6) + Mathf.Pow(MathF.Abs(y), 6), 1f / 6);
                        var scale = radius > 1 ? 1 / radius : 1;
                        x *= scale; y *= scale;
                        var expected = new Vector3(-.099f + .155f * x, .263f + .108f * y,
                            .064f + .014f * (1 - x * x) * (1 - y * y));
                        if ((toModel * vertices[vertex]).DistanceTo(expected) > .0015f)
                            throw new InvalidDataException("Imported CRT geometry/UV orientation differs from producer contract v2.0.0.");
                    }
                }
                _devicePickSurfaces.Add(new(mesh, vertices, indices, uvs, mesh == screen, control));
            }
        }
        if (observedBounds is not { } observed || observed.Position.DistanceTo(_deviceBounds.Position) > .0015f ||
            observed.End.DistanceTo(_deviceBounds.End) > .0015f)
            throw new InvalidDataException("Imported housing bounds differ from producer contract v2.0.0.");
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
        _deviceReadButton.TooltipText = "Magnify the instrument readout — F3";
        _deviceReadButton.Pressed += () =>
        {
            CancelDevicePointer(); ActivateInstrumentControl("CONTROL_Toggle_1_pivotX", 1);
            GetViewport().GuiReleaseFocus(); GrabFocus();
        };
        _deviceViewControls.AddChild(_deviceReadButton);
        _deviceStandardButton = ActionButton("Accessible launcher (F2)", 38);
        _deviceStandardButton.TooltipText = "Separate accessible launcher with ordinary keyboard focus and full setup — F2";
        _deviceStandardButton.Pressed += ToggleDeviceStandardView;
        _deviceViewControls.AddChild(_deviceStandardButton);
        _deviceReadButton.Flat = _deviceStandardButton.Flat = true;
        _deviceReadButton.AddThemeFontSizeOverride("font_size", 12);
        _deviceStandardButton.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_deviceViewControls);
    }

    private void ToggleDeviceStandardView()
    {
        if (_deviceInterface is null || _deviceCanvas is null || _deviceImage is null) return;
        CancelDevicePointer(); GetViewport().GuiReleaseFocus();
        _deviceStandardView = !_deviceStandardView;
        _deviceInterface.Visible = _deviceStandardView;
        _deviceImage.Visible = !_deviceStandardView;
        GetNode<Control>("PipBoyHousing").Visible = _deviceStandardView;
        GetNode<Control>("LauncherBackdrop").Visible = _deviceStandardView;
        if (_deviceViewControls is not null) MoveChild(_deviceViewControls, GetChildCount() - 1);
        if (_deviceStandardButton is not null) _deviceStandardButton.Text = _deviceStandardView ? "Instrument view (F2)" : "Accessible launcher (F2)";
        if (_deviceReadButton is not null) _deviceReadButton.Visible = !_deviceStandardView;
        if (_deviceViewControls is not null) _deviceViewControls.Position = new Vector2(_deviceStandardView ? MathF.Max(16, Size.X / 2 - 90) : 16, 12);
        if (_deviceStandardView) _search.GrabFocus(); else GrabFocus();
        RefreshInstrument();
    }

    private void ResizeDevice()
    {
        if (_deviceView is null || _deviceCamera is null) return;
        _deviceView.Size = new Vector2I(Math.Max(1, (int)Size.X), Math.Max(1, (int)Size.Y));
        var aspect = _deviceView.Size.X / (float)_deviceView.Size.Y;
        var bounds = _deviceReadScreen
            ? new Aabb(new Vector3(-.254f, .155f, .064f), new Vector3(.310f, .216f, .014f)) : _deviceBounds;
        var target = _deviceReadScreen ? DeviceScreenCenter : bounds.GetCenter();
        var direction = _deviceReadScreen ? Vector3.Back : new Vector3(-.38f, .18f, 1).Normalized();
        _deviceCamera.Position = target + direction;
        _deviceCamera.LookAt(target, Vector3.Up);
        var vertical = MathF.Tan(Mathf.DegToRad(_deviceCamera.Fov / 2));
        var right = _deviceCamera.Basis.X; var up = _deviceCamera.Basis.Y;
        var distance = .1f;
        for (var corner = 0; corner < 8; corner++)
        {
            var offset = bounds.GetEndpoint(corner) - target;
            var span = MathF.Max(MathF.Abs(offset.Dot(up)) / vertical, MathF.Abs(offset.Dot(right)) / (vertical * aspect));
            distance = MathF.Max(distance, offset.Dot(direction) + span * 1.08f);
        }
        _deviceCamera.Position = target + direction * distance;
        _deviceCamera.LookAt(target, Vector3.Up);
        if (_deviceReadButton is not null) _deviceReadButton.Text = _deviceReadScreen ? "Whole device" : "Read screen";
    }

    public override void _Input(InputEvent input)
    {
        if (_deviceCanvas is null || _deviceModel is null) return;
        if (_fileDialog is not null || _stackWindow is not null) { CancelDevicePointer(); return; }
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F2 } or
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Back })
        {
            ToggleDeviceStandardView(); GetViewport().SetInputAsHandled(); return;
        }
        if (_deviceStandardView) return;
        if (HandleInstrumentInput(input)) { GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion) return;
        if (input is not InputEventMouse mouse) return;
        if (_deviceViewControls is not null && _deviceViewControls.GetGlobalRect().HasPoint(mouse.Position))
        {
            CancelDevicePointer(); return;
        }
        var hit = PickDevice(mouse.Position);
        if (hit?.Control is { } hovered) SetInstrumentFocus(hovered);
        if (input is InputEventMouseButton { Pressed: true }) GetViewport().GuiReleaseFocus();
        if (_devicePressedControl is not null)
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                var name = _devicePressedControl;
                var held = Time.GetTicksMsec() - _devicePressedAt >= 700;
                ReleaseDeviceControl();
                if (hit?.Control == name) ActivateDeviceControl(name, name == "CONTROL_FunctionKey_1" && held ? 0 : 1);
            }
            GetViewport().SetInputAsHandled(); return;
        }
        if (hit?.Uv is not null)
        {
            // The CRT is an output instrument, not a projected browser. Wheel
            // only pages long status text; clicks never trigger app controls.
            if (input is InputEventMouseButton { Pressed: true } scroll &&
                scroll.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                PageInstrumentFeedback(scroll.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
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
            _devicePressedAt = Time.GetTicksMsec();
            var node = _deviceControls[control];
            _deviceUnpressedTransform = node.Transform;
            if (control.StartsWith("CONTROL_FunctionKey_", StringComparison.Ordinal))
                node.TranslateObjectLocal(Vector3.Forward * .003f); // Blender +Y -> glTF -Z.
        }
        GetViewport().SetInputAsHandled();
    }

    private void ActivateDeviceControl(string name, int direction) => ActivateInstrumentControl(name, direction);

    private void ReleaseDeviceControl()
    {
        if (_devicePressedControl is { } name && _deviceControls.TryGetValue(name, out var node) && GodotObject.IsInstanceValid(node))
            node.Transform = _deviceUnpressedTransform;
        _devicePressedControl = null;
    }

    private void CancelDevicePointer()
    {
        ReleaseDeviceControl();
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
        _deviceInterface?.Show();
        _deviceModel = null; _deviceCamera = null; _deviceInterface = null;
        _deviceControls.Clear(); _deviceControlRest.Clear(); _devicePickSurfaces.Clear(); _deviceMotionLimits.Clear();
        _deviceSelectorPoses.Clear(); DeviceSelectorContractStatus = "selector-contract-not-bound";
        _deviceReadyMaterial = null;
        _instrumentReadout = null;
        GetNodeOrNull<Control>("PipBoyHousing")?.Show();
        GetNodeOrNull<Control>("LauncherBackdrop")?.Show();
    }
}
