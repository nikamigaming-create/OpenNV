using System.Globalization;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedSpecialBookMenu : Control
{
    internal const string MenuPath = "menus/chargen/specialbookmenu.xml";
    private const string ModelPath = "meshes/terminals/babybook02.nif";
    private readonly FalloutPluginStack _records;
    private readonly FalloutSpecialAllocationSession _session;
    private readonly FalloutSpecialBookPresentation _declaration;
    private readonly FalloutSpecialBookTextures _textures;
    private readonly NativeOwnedNifMenuSurface _surface;
    private readonly RuntimeNifControllerPlayer _animation;
    private readonly Action _accepted;
    private readonly Action<Exception> _failed;
    private readonly Dictionary<string, Action> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _targetPages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _input = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (FalloutSoundRecord Source, long Revision)> _sounds = new(StringComparer.Ordinal);
    private readonly FalloutSoundRandomState _soundRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private IReadOnlyList<int> _values = [];
    private int _page, _index = 1;
    private bool _turning, _submitted;
    internal string? Error { get; private set; }
    internal FalloutSpecialBookPresentation Declaration => _declaration;
    internal RuntimeNifControllerPlayer Animation => _animation;
    internal IReadOnlyList<NativeOwnedNifMenuTarget> Targets => _surface.Targets(IsActiveTarget);
    internal object State => new
    {
        menuId = FalloutSpecialBookPresentation.MenuId,
        page = _page,
        index = _index,
        values = _values,
        budget = _session.Budget,
        remaining = _session.Budget - _values.Sum(),
        turning = _turning,
        submitted = _submitted,
        permanentReadBaseWriteOwner = _session.Owner,
        textureDeclarations = _textures.Paths,
        error = Error,
        pointerGeometry = _surface.LastPicked,
        animation = _animation.Observation,
        targets = Targets.Select(target => new
        {
            target.Geometry,
            center = new[] { target.Center.X, target.Center.Y },
            bounds = new[] { target.Bounds.Position.X, target.Bounds.Position.Y, target.Bounds.Size.X, target.Bounds.Size.Y },
            target.InFront
        }),
        integration = "explicit-permanent-read-and-base-write-binding;host-owns-command-menu-and-save-policy",
        unbound = "PC-shortcut-repeat,texture-input-mode-switching,fade-and-postprocessing,clip-plane-and-animated-bound-fitting,language-remapping,matched-retail-and-XR-pixels"
    };

    internal NativeOwnedSpecialBookMenu(FalloutPluginStack records, FalloutSpecialAllocationBinding binding, int? budget,
        Action accepted, Action<Exception> failed)
    {
        try
        {
            Name = "SPECIALBookMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
            LayoutMode = 1; AnchorsPreset = (int)LayoutPreset.FullRect; AnchorRight = 1; AnchorBottom = 1;
            _records = records; _accepted = accepted; _failed = failed;
            var content = RuntimeLiveContentSource.Current ?? throw new NotSupportedException("Owned SPECIAL book content is absent.");
            if (!content.TryRead(ModelPath, null, out var bytes, out var identity)) throw new FileNotFoundException(ModelPath);
            var source = FalloutNifFile.Read(bytes);
            var sequences = source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                .Select(block => (FalloutNifControllerSequence)source.ReadObject(block.Index)).ToArray();
            _declaration = FalloutExecutableStringTable.ReadSpecialBook(content.FalloutExecutablePath,
                sequences.Select(sequence => sequence.Name).ToArray());
            _textures = _declaration.Textures ?? throw new NotSupportedException("Owned SPECIAL book texture declarations are absent.");
            if (!string.Equals(_declaration.AnimatedModel.Replace('\\', '/'), ModelPath, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("SPECIAL book executable and winning model binding disagree.");
            foreach (var sequence in sequences)
            {
                if (sequence.TextKeys < 0) continue;
                var keys = ((FalloutNifTextKeyExtraData)source.ReadObject(sequence.TextKeys)).Keys;
                if (keys.Any(key => key.Value.Trim() is not ("start" or "end")))
                    throw new NotSupportedException("SPECIAL book has an unbound source animation text key.");
            }
            _session = new(budget ?? _declaration.DefaultBudget, binding);
            _ = _session.Values;
            var settings = FalloutInstallationSettings.Read(content);
            var native = new Basis(Vector3.Right, -_declaration.RotationRadians);
            var basis = GamebryoCoordinate.ConvertBasis([native.X.X, native.Y.X, native.Z.X, native.X.Y, native.Y.Y, native.Z.Y,
            native.X.Z, native.Y.Z, native.Z.Z], _declaration.ModelScale, "SPECIAL book model pose");
            var pose = new Transform3D(basis, GamebryoCoordinate.ConvertVector(new(0, 0, _declaration.Depth)));
            var camera = new Transform3D(new Basis(Vector3.Right, Vector3.Forward, Vector3.Up), GamebryoCoordinate.ConvertVector(new(0, 0, 1)));
            // NiCamera's source frustum constructor retains a one-unit near plane;
            // the shared rendered-menu far plane remains separately unmeasured.
            _surface = new([new(_declaration.AnimatedModel, source, pose)], new(camera,
                _ => _declaration.HorizontalSlope(settings.Number("Display", "fDefaultFOV")), 1, 5000,
                _declaration.LightRadiusMultiple, Vector3.One * _declaration.LightIntensity));
            AddChild(_surface); _surface.PreloadTextures(_textures.Paths); _animation = _surface.Animation;
            _animation.TextKeyHandler = _ => "structural-sequence-boundary";
            var menu = FalloutMenuXml.Expand(FalloutMenuXml.Read(MenuPath)).Elements("menu").Single();
            if ((string?)menu.Attribute("name") != "SPECIALBookMenu" || menu.Element("alpha")?.Value.Trim() != "0" ||
                menu.Descendants("filename").Any()) throw new NotSupportedException("SPECIAL book visible XML tiles are unbound.");
            foreach (var mapping in menu.Elements().Where(element => element.Name.LocalName.StartsWith('x') && element.Element("ref") is not null))
            {
                var reference = mapping.Element("ref")!;
                if ((string?)reference.Attribute("trait") != "clicked") throw new NotSupportedException("SPECIAL book input trait is unbound.");
                _input.Add(mapping.Name.LocalName, (string)reference.Attribute("src")!);
            }
            var controls = menu.Descendants().Where(element => element.Element("id") is not null)
                .ToDictionary(element => (int)element.Element("id")!, element => (string)element.Attribute("name")!);
            if (controls.Count != 8 || Enumerable.Range(0, 8).Any(id => !controls.ContainsKey(id)))
                throw new NotSupportedException("SPECIAL book source input protocol is incomplete.");
            _actions.Add(controls[0], () => Turn(1)); _actions.Add(controls[1], () => Turn(-1));
            _actions.Add(controls[2], () => Change(CurrentAttribute, 1)); _actions.Add(controls[3], () => Change(CurrentAttribute, -1));
            _actions.Add(controls[4], Submit); _actions.Add(controls[6], () => SelectIndex(-1)); _actions.Add(controls[7], () => SelectIndex(1));
            void Target(string name, int page, Action action) { _targetPages.Add(name, page); _actions.Add(name, action); _ = _surface.Geometry(name); }
            Target("LookInside_Btn:0", 0, () => Turn(1));
            for (var page = 1; page < FalloutSpecialBookPresentation.ReviewPage; page++)
            {
                var attribute = page - 1; var prefix = "P" + page.ToString(CultureInfo.InvariantCulture);
                Target(prefix + "_RT_Btn:0", page, () => Turn(1)); Target(prefix + "_LT_Btn:0", page, () => Turn(-1));
                Target(prefix + "_Increase_Btn:0", page, () => Change(attribute, 1)); Target(prefix + "_Decrease_Btn:0", page, () => Change(attribute, -1));
            }
            Target("AllDone_Btn:0", FalloutSpecialBookPresentation.ReviewPage, Submit);
            Target("Index_LT_Btn:0", FalloutSpecialBookPresentation.ReviewPage, () => Turn(-1));
            for (var index = 0; index < FalloutSpecialAllocationSession.AttributeCount; index++)
            {
                var attribute = index; var prefix = "Index_" + FalloutNativeVigorResolver.AttributeNames[index];
                Target(prefix + "Increase_Btn:0", FalloutSpecialBookPresentation.ReviewPage, () => Change(attribute, 1));
                Target(prefix + "Decrease_Btn:0", FalloutSpecialBookPresentation.ReviewPage, () => Change(attribute, -1));
            }
            SetMeta("opennv_ui_source", MenuPath); SetMeta("opennv_book_model", identity); SetMeta("opennv_menu_id", FalloutSpecialBookPresentation.MenuId);
            Resized += () => Try(() => _surface.SetExtent(Size));
            Refresh();
        }
        catch { Free(); throw; }
    }

    private int CurrentAttribute => _page is > 0 and < FalloutSpecialBookPresentation.ReviewPage ? _page - 1 :
        _page == FalloutSpecialBookPresentation.ReviewPage ? _index - 1 : -1;

    public override void _Ready() => Try(() =>
    {
        // The source constructor keeps page 0. Its initial sequence is bound
        // at the first pose until ordinary Look Inside/next-page input occurs.
        _animation.PlaySourceSequence(_declaration.ForwardSequences[0]); _animation.SetProcess(false);
        _surface.SetExtent(Size); Refresh();
    });

    public override void _Process(double delta) => Try(() =>
    {
        if (_turning && !_animation.IsProcessing()) { _turning = false; Refresh(); }
        else if (!_session.Values.SequenceEqual(_values)) Refresh();
    });

    public override void _GuiInput(InputEvent input)
    {
        if (_submitted || Error is not null || input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        Try(() => { var hit = _surface.Pick(click.Position, IsActiveTarget); if (hit is not null) _actions[hit](); AcceptEvent(); });
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_submitted || Error is not null) return;
        var route = input switch
        {
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Left } => "xleft",
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Right } => "xright",
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Up } => "xup",
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Down } => "xdown",
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
        Try(() => { _actions[control](); GetViewport().SetInputAsHandled(); });
    }

    private void Turn(int direction)
    {
        var next = _page + direction;
        if (_turning || next < 0 || next > _declaration.LastPage) return;
        _page = next; _turning = true; _animation.PlaySourceSequence(_declaration.Transition(next, direction));
        Play("OBJBookSpecialPageTurn"); Refresh();
    }
    private void SelectIndex(int direction)
    {
        if (_page != FalloutSpecialBookPresentation.ReviewPage) return;
        var next = Math.Clamp(_index + direction, 1, FalloutSpecialAllocationSession.AttributeCount);
        if (_index == next) return;
        _index = next; Play("OBJBookSpecialFocus"); Refresh();
    }
    private void Change(int attribute, int direction)
    {
        if (attribute < 0 || attribute >= FalloutSpecialAllocationSession.AttributeCount) return;
        var previous = _values[attribute];
        if (direction < 0 && previous <= FalloutSpecialAllocationSession.Minimum || direction > 0 && previous >= FalloutSpecialAllocationSession.Maximum) return;
        if (_session.Change(FalloutSpecialAllocationSession.FirstActorValue + attribute, direction))
        { Play("OBJBookSpecialNumber"); Refresh(); }
    }
    private void Submit()
    {
        if (_submitted) return;
        if (!_session.CanFinish) { Play("UIActivateNothing"); return; }
        _submitted = true; _accepted();
    }

    private void Refresh()
    {
        _values = _session.Values; var remaining = _session.Budget - _values.Sum();
        if (remaining < 0 || _values.Any(value => value is < 0 or > FalloutSpecialAllocationSession.Maximum))
            throw new NotSupportedException("SPECIAL book digit table cannot represent the bound permanent values.");
        for (var page = 1; page < FalloutSpecialBookPresentation.ReviewPage; page++)
        {
            var prefix = "P" + page.ToString(CultureInfo.InvariantCulture); var value = _values[page - 1];
            Number(prefix + "_PointVal:0", value);
            _surface.Geometry(prefix + "_Increase_Btn:0").Visible = remaining > 0 && value < FalloutSpecialAllocationSession.Maximum;
            _surface.Geometry(prefix + "_Decrease_Btn:0").Visible = value > FalloutSpecialAllocationSession.Minimum;
            Remaining(prefix, remaining);
            _surface.SetTexture(prefix + "_RT_Btn:0", _textures.Buttons["BBRTOff"]);
            _surface.SetTexture(prefix + "_LT_Btn:0", _textures.Buttons["BBLTOff"]);
            _surface.SetTexture(prefix + "_Message:0", _textures.Message(remaining));
        }
        for (var index = 0; index < _values.Count; index++)
        {
            var prefix = "Index_" + FalloutNativeVigorResolver.AttributeNames[index]; var value = _values[index];
            Number(prefix + "PointVal:0", value);
            _surface.Geometry(prefix + "Increase_Btn:0").Visible = _index == index + 1 && remaining > 0 && value < FalloutSpecialAllocationSession.Maximum;
            _surface.Geometry(prefix + "Decrease_Btn:0").Visible = _index == index + 1 && value > FalloutSpecialAllocationSession.Minimum;
        }
        Remaining("Index", remaining);
        _surface.SetTexture("Index_Message:0", _textures.Message(remaining));
        _surface.SetTexture("LookInside_Btn:0", _textures.Buttons["BBRTOff"]);
        _surface.SetTexture("Index_LT_Btn:0", _textures.Buttons["BBLTOn"]);
        _surface.SetTexture("AllDone_Btn:0", _textures.Buttons[remaining > 0 ? "BBXOn" : "BBXOff"]);
    }
    private void Remaining(string prefix, int remaining)
    {
        var tens = _surface.Geometry(prefix + "_PointsRemainDigit1:0"); var ones = _surface.Geometry(prefix + "_PointsRemainDigit2:0");
        tens.Visible = remaining >= 10; ones.Visible = remaining != 0;
        if (tens.Visible) Number(prefix + "_PointsRemainDigit1:0", remaining / 10);
        if (ones.Visible) Number(prefix + "_PointsRemainDigit2:0", remaining % 10);
    }
    private void Number(string geometry, int value) => _surface.SetTexture(geometry, _textures.Digits[value]);
    private bool IsActiveTarget(string name, MeshInstance3D mesh) => _targetPages.TryGetValue(name, out var page) && page == _page && mesh.IsVisibleInTree();
    private void Play(string editorId)
    {
        if (!_sounds.TryGetValue(editorId, out var entry) || entry.Revision != _records.SoundPaths.Revision(entry.Source.FormKey))
        {
            var record = FalloutSoundRecordReader.Find(_records, editorId);
            entry = (FalloutSoundRecordReader.Read(_records, record.FormKey), _records.SoundPaths.Revision(record.FormKey)); _sounds[editorId] = entry;
        }
        var player = NativeOwnedSoundPlayback.CreateMenu(entry.Source, _records, RuntimeLiveContentSource.Current!, _soundRandom);
        AddChild(player); player.Finished += player.QueueFree; player.Play();
    }
    private void Try(Action action)
    {
        if (Error is not null) return;
        try { action(); }
        catch (Exception error) { Error = error.Message; _animation.SetProcess(false); _failed(error); }
    }
}
