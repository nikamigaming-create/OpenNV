using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeLoveTesterTarget(string Geometry, Vector2 Center, Rect2 Bounds, bool InFront);

/// <summary>The LoveTester menu is its owned animated NIF, with XML input routing.</summary>
internal sealed partial class NativeOwnedLoveTesterMenu : Control
{
    internal const string MenuPath = "menus/chargen/love_tester_menu.xml";
    private const string ModelInterface = "meshes/architecture/GoodSprings/NV_VitoMaticVigorTester_Activate.NIF";
    private readonly FalloutNativeVigorContract _contract;
    private readonly FalloutPluginStack _records;
    private readonly FalloutInstallationSettings _settings;
    private readonly FalloutLoveTesterPresentation _declaration;
    private readonly SubViewport _view;
    private readonly Camera3D _camera;
    private readonly Node3D _animated, _cabinet;
    private readonly FalloutNifFile _source;
    private readonly RuntimeNifControllerPlayer _animation;
    private readonly TextureRect _pixels;
    private readonly Dictionary<string, MeshInstance3D> _geometry;
    private readonly Dictionary<string, Action> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _input = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FalloutSoundRecord> _sounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly FalloutSoundRandomState _soundRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private FalloutNativeSpecialState _state;
    private int _page, _index;
    private bool _turning, _accepted;
    internal event Action<FalloutNativeSpecialState>? Accepted;

    internal NativeOwnedLoveTesterMenu(FalloutNativeVigorContract contract, FalloutNativeSpecialState initial, FalloutPluginStack records)
    {
        Name = "LoveTesterMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        _contract = contract; _state = initial; _records = records;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned LoveTester content is absent.");
        _settings = FalloutInstallationSettings.Read(content);
        FalloutNifFile ReadModel(string path)
        {
            if (!content.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
            SetMeta("opennv_love_tester_" + (path == ModelInterface ? "cards" : "cabinet"), identity);
            return FalloutNifFile.Read(bytes);
        }
        _source = ReadModel(ModelInterface);
        var sequences = _source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
            .Select(block => ((FalloutNifControllerSequence)_source.ReadObject(block.Index)).Name).ToArray();
        _declaration = FalloutExecutableStringTable.ReadLoveTester(Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe"), sequences);
        var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read(MenuPath)).Elements("menu").Single();
        if ((string?)menu.Attribute("name") != "LoveTesterMenu" || menu.Element("alpha")?.Value.Trim() != "0")
            throw new NotSupportedException("LoveTester visible XML requires a separate tile presentation owner.");
        foreach (var mapping in menu.Elements().Where(element => element.Name.LocalName.StartsWith('x') && element.Element("ref") is not null))
        {
            var reference = mapping.Element("ref")!;
            if ((string?)reference.Attribute("trait") != "clicked") throw new NotSupportedException("LoveTester input trait is unbound.");
            _input.Add(mapping.Name.LocalName, (string)reference.Attribute("src")!);
        }
        var controls = menu.Descendants().Where(element => element.Element("id") is not null)
            .ToDictionary(element => (int)element.Element("id")!, element => (string)element.Attribute("name")!);
        if (controls.Count != 8 || Enumerable.Range(0, 8).Any(id => !controls.ContainsKey(id)))
            throw new NotSupportedException("LoveTester source control protocol is incomplete.");
        _view = new SubViewport
        {
            Name = "LoveTesterView",
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        AddChild(_view);
        _animated = RuntimeNativeNifMeshBuilder.Build(_source, 1).Root;
        _cabinet = RuntimeNativeNifMeshBuilder.Build(ReadModel(_declaration.CabinetModel), 1).Root;
        _view.AddChild(_animated); _view.AddChild(_cabinet);
        _camera = new Camera3D
        {
            Name = "LoveTesterCamera",
            Current = true,
            Position = GamebryoCoordinate.ConvertVector(new(0, 0, 1)),
            Basis = new Basis(Vector3.Right, Vector3.Forward, Vector3.Up)
        };
        _view.AddChild(_camera);
        _pixels = new TextureRect
        {
            Name = "OwnedLoveTesterPixels",
            Texture = _view.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_pixels);
        _geometry = _animated.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.HasMeta("opennv_nif_source_name"))
            .ToDictionary(mesh => mesh.GetMeta("opennv_nif_source_name").AsString(), StringComparer.Ordinal);
        _animation = _animated.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
        _actions.Add(controls[0], () => Turn(1)); _actions.Add(controls[1], () => Turn(-1));
        _actions.Add(controls[2], () => Change(CurrentAttribute, 1)); _actions.Add(controls[3], () => Change(CurrentAttribute, -1));
        _actions.Add(controls[4], Submit); _actions.Add(controls[6], () => SelectIndex(-1)); _actions.Add(controls[7], () => SelectIndex(1));
        _actions.Add("P1_RT_Btn:0", () => Turn(1)); _actions.Add("P1_LT_Btn:0", () => Turn(-1));
        _actions.Add("P1_Increase_Btn:0", () => Change(CurrentAttribute, 1)); _actions.Add("P1_Decrease_Btn:0", () => Change(CurrentAttribute, -1));
        _actions.Add("LookInside_Btn:0", () => Turn(1)); _actions.Add("AllDone_Btn:0", Submit);
        for (var index = 0; index < _state.Values.Count; index++)
        {
            var attribute = index; var prefix = "Index_" + FalloutNativeVigorResolver.AttributeNames[index];
            _actions.Add(prefix + "Increase_Btn:0", () => Change(attribute, 1));
            _actions.Add(prefix + "Decrease_Btn:0", () => Change(attribute, -1));
        }
        foreach (var sourceName in _actions.Keys.Where(name => name.EndsWith(":0", StringComparison.Ordinal)))
            if (!_geometry.ContainsKey(sourceName)) throw new InvalidDataException($"Owned LoveTester target is missing: {sourceName}.");
        SetMeta("opennv_ui_source", MenuPath); SetMeta("opennv_ui_presentation", "owned-nif-controller-manager-and-dds");
        SetMeta("opennv_ui_unbound", "matched-pixels,render-target-postprocessing,gamepad-repeat-timing");
        Resized += Layout; Refresh();
    }

    private int ReviewPage => _state.Values.Count + 1;
    private int CurrentAttribute => _page > 0 && _page < ReviewPage ? _page - 1 : -1;
    internal IReadOnlyList<NativeLoveTesterTarget> Targets => _geometry.Where(item => IsActiveTarget(item.Key, item.Value)).Select(item =>
    {
        var mesh = item.Value; var bounds = mesh.Mesh.GetAabb();
        var points = Enumerable.Range(0, 8).Select(index => _camera.UnprojectPosition(mesh.GlobalTransform * bounds.GetEndpoint(index))).ToArray();
        var minimum = points.Aggregate((a, b) => new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)));
        var maximum = points.Aggregate((a, b) => new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        var center = mesh.GlobalTransform * bounds.GetCenter();
        return new NativeLoveTesterTarget(item.Key, _camera.UnprojectPosition(center), new(minimum, maximum - minimum), !_camera.IsPositionBehind(center));
    }).ToArray();
    public override void _Ready()
    {
        // The owned initial sequence opens the cover onto the first attribute.
        // Freezing its first frame leaves the menu on an extra, inert cover page.
        _page = 1; _turning = true;
        _animation.PlaySourceSequence(_declaration.ForwardSequences[0]);
        Refresh();
        Layout();
    }

    private void Layout()
    {
        if (!IsInsideTree() || !_camera.IsInsideTree() || Size.X <= 0 || Size.Y <= 0) return;
        _view.Size = new(Math.Max(1, (int)Size.X), Math.Max(1, (int)Size.Y)); _pixels.Size = Size;
        var horizontal = _declaration.HorizontalSlope(_settings.Number("Display", "fDefaultFOV"));
        _camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        _camera.Fov = MathF.Atan(horizontal * Size.Y / Size.X) * 360 / MathF.PI;
        _camera.Near = _settings.Number("Display", "fNearDistance"); _camera.Far = 5000;
        var angles = _declaration.RotationRadians;
        // NiMatrix33 axis constructors use the engine's row-major signs,
        // independently of the NIF serialization boundary.
        var native = new Basis(Vector3.Right, -angles[0]) * new Basis(Vector3.Back, -angles[1]) * new Basis(Vector3.Right, -angles[2]);
        var basis = GamebryoCoordinate.ConvertBasis([
            native.X.X, native.Y.X, native.Z.X, native.X.Y, native.Y.Y, native.Z.Y,
            native.X.Z, native.Y.Z, native.Z.Z], 1, "LoveTester menu model");
        var width = 480 * Size.X / Size.Y;
        var depth = width > _declaration.LogicalWidthBoundary ? _declaration.WideDepth : _declaration.NarrowDepth;
        var transform = new Transform3D(basis, GamebryoCoordinate.ConvertVector(new(0, _declaration.VerticalOffset, depth)));
        _animated.Transform = transform; _cabinet.Transform = transform;
        var radius = FalloutNifBounds.ReadStatic(_source).Radius * _declaration.LightRadiusMultiple;
        var light = new NativeNifPointLight(_camera.GlobalTransform.AffineInverse() * Vector3.Zero,
            Vector3.One * _declaration.LightIntensity, radius);
        foreach (var mesh in _animated.FindChildren("*", "", true, false).Concat(_cabinet.FindChildren("*", "", true, false)).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetActiveMaterial(surface) is ShaderMaterial material && material.ResourceName == NativeNifLightingMaterial.ResourceIdentity)
                    NativeNifPointLighting.Bind(material, [light], 1, storeEncoded: true);
        SetMeta("opennv_love_tester_projection", new Vector2(horizontal, horizontal * Size.Y / Size.X));
    }

    public override void _Process(double delta)
    {
        if (!_turning || _animation.IsProcessing()) return;
        _turning = false; Refresh();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_accepted || _turning) return;
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var hit = Pick(click.Position); if (hit is not null) _actions[hit](); AcceptEvent();
        }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_accepted || _turning) return;
        string? route = input switch
        {
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Left } => "xbuttonlt",
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Right } => "xbuttonrt",
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Up } => CurrentAttribute >= 0 ? "xright" : "xup",
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Down } => CurrentAttribute >= 0 ? "xleft" : "xdown",
            InputEventKey { Pressed: true, Keycode: Key.Plus or Key.Equal or Key.KpAdd } => "xright",
            InputEventKey { Pressed: true, Keycode: Key.Minus or Key.Underscore or Key.KpSubtract } => "xleft",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.RightShoulder } => "xbuttonrt",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.LeftShoulder } => "xbuttonlt",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadRight } => "xright",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadLeft } => "xleft",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadUp } => "xup",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadDown } => "xdown",
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.X } => "xbuttonx",
            _ => null,
        };
        if (route is null || !_input.TryGetValue(route, out var control)) return;
        _actions[control](); GetViewport().SetInputAsHandled();
    }

    private void Turn(int direction)
    {
        var next = _page + direction;
        if (next > ReviewPage) { Submit(); return; }
        if (next < 0) return;
        _page = next; _turning = true; _animation.PlaySourceSequence(_declaration.Transition(_page, direction));
        Play("OBJBookSpecialPageTurn"); Refresh();
    }

    private void SelectIndex(int direction)
    {
        if (_page != ReviewPage) return;
        var next = Math.Clamp(_index + direction, 0, _state.Values.Count - 1);
        if (next == _index) return;
        _index = next; Play("OBJBookSpecialFocus"); Refresh();
    }

    private void Change(int index, int delta)
    {
        if (index < 0 || index >= _state.Values.Count) return;
        var value = _state.Values[index] + delta;
        if (value < _contract.MinimumAttribute || value > _contract.MaximumAttribute || _state.Values.Sum() + delta > _contract.RequiredTotal) return;
        _state = _state.WithValue(index, value); Play(delta < 0 ? "OBJBookSpecialNumberDown" : "OBJBookSpecialNumber"); Refresh();
    }

    private void Submit()
    {
        if (_state.Values.Sum() != _contract.RequiredTotal) { Play("UIActivateNothing"); return; }
        FalloutNativeVigorResolver.Validate(_contract, _state);
        Play("OBJBookSpecialNumberFinished", surviveMenu: true); _accepted = true; Accepted?.Invoke(_state);
    }

    private void Refresh()
    {
        var remaining = _contract.RequiredTotal - _state.Values.Sum();
        for (var index = 0; index < _state.Values.Count; index++)
        {
            var prefix = "Index_" + FalloutNativeVigorResolver.AttributeNames[index]; Number(prefix + "PointVal:0", _state.Values[index]);
            _geometry[prefix + "Increase_Btn:0"].Visible = remaining > 0 && _state.Values[index] < _contract.MaximumAttribute;
            _geometry[prefix + "Decrease_Btn:0"].Visible = _state.Values[index] > _contract.MinimumAttribute;
        }
        Number("P1_PointsRemainDigit1:0", remaining / 10); Number("P1_PointsRemainDigit2:0", remaining % 10);
        var selected = _page > 0 && _page < ReviewPage ? _state.Values[_page - 1] : 0;
        foreach (var node in _cabinet.FindChildren("*", "", true, false).OfType<Node3D>())
        {
            if (!node.HasMeta("opennv_nif_source_name")) continue;
            var match = Regex.Match(node.GetMeta("opennv_nif_source_name").AsString(), @"^P1_PointVal_(\d+)_GLOW(?::\d+)?$");
            if (match.Success) node.Visible = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) <= selected;
        }
        var current = CurrentAttribute;
        _geometry["P1_Increase_Btn:0"].Visible = current >= 0 && current < _state.Values.Count && remaining > 0 && _state.Values[current] < _contract.MaximumAttribute;
        _geometry["P1_Decrease_Btn:0"].Visible = current >= 0 && current < _state.Values.Count && _state.Values[current] > _contract.MinimumAttribute;
        foreach (var name in new[] { "P1_RT_Btn:0", "LookInside_Btn:0", "AllDone_Btn:0" })
            Texture(name, "textures/terminals/PC/BBRT" + (_page == ReviewPage && remaining != 0 ? "Off" : "On") + ".dds");
        Texture("P1_LT_Btn:0", "textures/terminals/PC/BBLTOff.dds");
        SetMeta("opennv_love_tester_page", _page); SetMeta("opennv_love_tester_selected_attribute", current);
        SetMeta("opennv_love_tester_remaining", remaining); SetMeta("opennv_love_tester_special", _state.Values.ToArray());
        SetMeta("opennv_love_tester_turning", _turning);
    }

    private void Number(string geometry, int value) => Texture(geometry, "textures/terminals/BBNumber" + value.ToString(CultureInfo.InvariantCulture) + ".dds");

    private void Texture(string geometry, string path)
    {
        if (!_textures.TryGetValue(path, out var texture)) _textures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
        var mesh = _geometry[geometry];
        if (mesh.GetActiveMaterial(0) is not ShaderMaterial material || material.ResourceName != NativeNifLightingMaterial.ResourceIdentity)
            throw new NotSupportedException($"LoveTester dynamic texture material is unbound: {geometry}.");
        material.SetShaderParameter("base_map", texture); material.SetShaderParameter("use_base_map", true);
        mesh.SetMeta("opennv_love_tester_texture", path);
    }

    private string? Pick(Vector2 position)
    {
        const float barycentricTolerance = 1e-5f;
        var origin = _camera.ProjectRayOrigin(position); var direction = _camera.ProjectRayNormal(position);
        var nearest = float.PositiveInfinity; string? result = null;
        foreach (var (name, mesh) in _geometry.Where(item => IsActiveTarget(item.Key, item.Value)))
        {
            var inverse = mesh.GlobalTransform.AffineInverse(); var o = inverse * origin; var d = inverse.Basis * direction;
            var arrays = mesh.Mesh.SurfaceGetArrays(0); var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for (var index = 0; index < indices.Length; index += 3)
            {
                var a = vertices[indices[index]]; var b = vertices[indices[index + 1]]; var c = vertices[indices[index + 2]];
                var first = b - a; var second = c - a; var cross = d.Cross(second); var determinant = first.Dot(cross);
                if (MathF.Abs(determinant) < 1e-8f) continue;
                var offset = o - a; var u = offset.Dot(cross) / determinant;
                // Adjacent source triangles share closed edges. Projection and
                // inverse-transform rounding must not open a crack between them.
                if (u < -barycentricTolerance || u > 1 + barycentricTolerance) continue;
                var q = offset.Cross(first); var v = d.Dot(q) / determinant;
                if (v < -barycentricTolerance || u + v > 1 + barycentricTolerance) continue;
                var hit = second.Dot(q) / determinant;
                if (hit > 0 && hit < nearest) { nearest = hit; result = name; }
            }
        }
        SetMeta("opennv_love_tester_pointer_geometry", result ?? string.Empty);
        return result;
    }

    private bool IsActiveTarget(string name, MeshInstance3D mesh) => _actions.ContainsKey(name) && mesh.IsVisibleInTree() &&
        (!name.StartsWith("Index_", StringComparison.Ordinal) || _page == ReviewPage) &&
        (name != "LookInside_Btn:0" || _page == 0) && (name != "AllDone_Btn:0" || _page == ReviewPage);

    private void Play(string editorId, bool surviveMenu = false)
    {
        if (!_sounds.TryGetValue(editorId, out var descriptor))
        {
            var record = _records.EffectiveRecords("SOUN").Single(record => record.ReadSubrecords()
                .Where(row => row.Signature == "EDID").Any(row => Encoding.ASCII.GetString(row.Data.Span).TrimEnd('\0') == editorId));
            _sounds.Add(editorId, descriptor = FalloutSoundRecordReader.Read(record));
        }
        var player = NativeOwnedSoundPlayback.CreateMenu(descriptor, RuntimeLiveContentSource.Current!, _soundRandom);
        (surviveMenu ? GetTree().Root : (Node)this).AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }
}
