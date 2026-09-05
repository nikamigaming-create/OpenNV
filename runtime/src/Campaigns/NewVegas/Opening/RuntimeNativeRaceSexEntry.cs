using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeRaceSexEntry : CanvasLayer
{
    private FalloutPluginStack _records = null!;
    private FalloutNativeCharacterCreation _creation = null!;
    private FalloutInstallationSettings _settings = null!;
    private NativeOwnedRenderedDevice _device = null!;
    private NativeOwnedRenderedScreen _screen = null!;
    private NativeOwnedActorPreview? _portrait;
    private NativeOwnedMessageMenu? _confirmation;
    private SceneTree? _pausedTree;
    private bool _previousPause;
    private int _page, _previewRevision = -1;
    internal string? Error { get; private set; }
    internal event Action<FalloutNativeRaceSexSelection>? Accepted;
    internal event Action<Exception>? Failed;
    internal FalloutNativeCharacterCreation Creation => _creation;
    internal void SelectPage(int page)
    {
        if (page < 0 || page >= _creation.Headers.Count) throw new ArgumentOutOfRangeException(nameof(page));
        _page = page; ShowPage();
    }
    internal object State => new
    {
        page = _page,
        revision = _creation.Revision,
        previewRevision = _previewRevision,
        character = _creation.Selection,
        error = Error,
        sourceControls = _creation.Controls.Controls.Count,
        parity = "unverified"
    };

    internal void Configure(FalloutNativeRaceSexContract contract, FalloutNativeRaceSexSelection current, FalloutPluginStack records)
    {
        Name = "NativeRaceSexEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        _records = records;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Creation has no owned content source.");
        _settings = FalloutInstallationSettings.Read(source);
        _creation = new(records, contract, current, _settings);
        _device = new("meshes/terminals/nv_reflectron_ui.nif", _settings) { Size = GetViewport().GetVisibleRect().Size };
        AddChild(_device);
        _screen = new(_device, records, _settings, Fail); AddChild(_screen);
        _screen.Menu.Navigate += Navigate;
        GetViewport().SizeChanged += Resize;
        _pausedTree = GetTree(); _previousPause = _pausedTree.Paused; _pausedTree.Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshPreview(); ShowPage();
        GD.Print($"OPENNV_NATIVE_CREATION_PRESENTED pages={_creation.Headers.Count} controls={_creation.Controls.Controls.Count} source=owned-nif-xml-fnt-ctl-player-records parity=unverified");
    }
    private void Resize() => _device.Size = GetViewport().GetVisibleRect().Size;
    private string Text(string setting) => FalloutGameSettingStrings.Read(_records, setting);
    private NativeRaceSexChoice Link(int page) => new(_creation.Header(page) + " >", false, () => { _page = page; ShowPage(); }, Selectable: false);
    private NativeRaceSexChoice Slider(string setting, int min, int max, int jump, Func<int> get, Action<int> set, Func<string>? display = null)
        => new(Text(setting), false, () => { }, new(min, max, jump, get, set, display ?? (() => "")), false);
    private void Edit(Action action, bool rebuild = true)
    {
        action();
        if (rebuild) ShowPage();
    }
    private void ShowPage()
    {
        var state = _creation.Selection;
        IReadOnlyList<NativeRaceSexChoice> rows = _page switch
        {
            0 => [new(Text("sMale"), !state.Female, () => Edit(() => _creation.ChangeIdentity(state.RaceRuntimeFormId, false))),
                new(Text("sFemale"), state.Female, () => Edit(() => _creation.ChangeIdentity(state.RaceRuntimeFormId, true)))],
            1 => _creation.Races.Select(race => new NativeRaceSexChoice(race.DisplayName, race.RuntimeFormId == state.RaceRuntimeFormId,
                () => Edit(() => _creation.ChangeIdentity(race.RuntimeFormId, state.Female)))).ToArray(),
            2 => FaceRows(),
            3 => [Link(5), Link(6), Link(7)],
            4 => [Link(9), Link(19), Link(8)],
            5 => _creation.Race.HairFor(state.Female).OrderBy(part => part.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(part => new NativeRaceSexChoice(part.DisplayName, part.RuntimeFormId == state.HairRuntimeFormId, () => Edit(() => _creation.SetHair(part)))).ToArray(),
            6 => HairColorRows(),
            7 => _creation.FacialHair().Select(part => new NativeRaceSexChoice(part.DisplayName, state.Face!.HeadParts.Contains(part.RuntimeFormId),
                () => Edit(() => _creation.ToggleHeadPart(part)))).ToArray(),
            8 => _creation.Race.EyesFor(state.Female).OrderBy(part => part.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(part => new NativeRaceSexChoice(part.DisplayName, part.RuntimeFormId == state.EyesRuntimeFormId, () => Edit(() => _creation.SetEyes(part)))).ToArray(),
            9 => Enumerable.Range(10, 9).Select(Link).ToArray(),
            >= 10 and <= 19 => ControlRows(),
            _ => throw new NotSupportedException("Creation page has no native owner."),
        };
        _screen.Menu.SetPage(_page, _creation.Header(_page), rows, _page == 3);
    }
    private IReadOnlyList<NativeRaceSexChoice> FaceRows()
    {
        var rows = new List<NativeRaceSexChoice>(); var presets = _creation.Presets();
        if (presets.Count > 1) rows.Add(Slider("sRSMPreset", 1, presets.Count, 5, () => _creation.PresetIndex,
            value => _creation.ApplyPreset(value), () => _creation.PresetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        rows.Add(Link(4));
        rows.Add(new(Text("sRSMRandomize"), false, () => Confirm("sRSMConfirmRandomize", () =>
            throw new NotSupportedException("Native FaceGen random distribution and random stream are not yet bound.")), Selectable: false));
        rows.Add(Slider("sRSMAge", 1, 10, 3, () => _creation.AgeValue, _creation.SetAge));
        return rows;
    }
    private IReadOnlyList<NativeRaceSexChoice> HairColorRows()
    {
        var rows = new List<NativeRaceSexChoice>
        {
            Slider("sRSMPreset", 0, 15, 4, () => _creation.HairPresetIndex, _creation.SetHairPreset, () => _creation.HairPresetLabel),
        };
        string[] labels = ["sRSMRedAbbrev", "sRSMGreenAbbrev", "sRSMBlueAbbrev"];
        for (var channel = 0; channel < labels.Length; channel++)
        {
            var component = channel;
            rows.Add(Slider(labels[channel], 0, 255, 32, () => _creation.Selection.Face!.HairColor[component],
                value => _creation.SetHairComponent(component, value), () => _creation.Selection.Face!.HairColor[component].ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return rows;
    }
    private IReadOnlyList<NativeRaceSexChoice> ControlRows()
    {
        var controls = _creation.Controls.Controls.Where(row => row.Page == _page);
        if (_page == 19) controls = _creation.Controls.TextureOrder.Select(index => controls.Single(row => row.Index == index)).ToArray();
        return controls.Select(row =>
        {
            var limits = _creation.Limits(row);
            return Slider(row.Setting, limits.Minimum, limits.Maximum, Math.Max(1, (int)MathF.Ceiling((limits.Maximum - limits.Minimum) / 4f)),
                () => _creation.Value(row), value => _creation.SetControl(row, value));
        }).ToArray();
    }
    private void Navigate(int direction)
    {
        if (_confirmation is not null) return;
        if (direction > 0 && _page == 3) { Confirm("sRSMConfirmDone", () => Accepted?.Invoke(_creation.Selection)); return; }
        if (direction > 0) _page++;
        else _page = _page switch { <= 3 => Math.Max(0, _page - 1), 4 => 2, 5 or 6 or 7 => 3, 8 or 9 or 19 => 4, >= 10 and <= 18 => 9, _ => throw new NotSupportedException() };
        ShowPage();
    }
    private void Confirm(string setting, Action accepted)
    {
        _screen.SetProcessInput(false);
        var prompt = setting == "sRSMConfirmDone" ? $"{Text(setting)} {Text("sRSMCharacter")}?" : Text(setting);
        _confirmation = new(new(_records.RuntimeFormKey(_creation.Selection.RaceRuntimeFormId), "", prompt, true, [Text("sNo"), Text("sYes")]),
            _records, index =>
            {
                var menu = _confirmation!; _confirmation = null; RemoveChild(menu); menu.QueueFree();
                _screen.SetProcessInput(true);
                if (index == 1) { try { accepted(); } catch (Exception error) { Fail(error); } }
            }, Fail);
        AddChild(_confirmation);
    }
    private void RefreshPreview()
    {
        var appearance = _creation.Appearance();
        var portrait = new NativeOwnedActorPreview(_records, appearance, _settings, (int)_screen.ContentView.Size.X);
        AddChild(portrait);
        try
        {
            // MTIdle is the engine's neutral standing animation group. Its
            // resource namespace follows the source actor's skeleton family.
            var skeleton = appearance.SkeletonPath.Replace('\\', '/');
            var path = skeleton[..skeleton.LastIndexOf('/')] + "/locomotion/mtidle.kf";
            var source = RuntimeLiveContentSource.Current!;
            if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException("Neutral source animation group is absent.", path);
            var file = FalloutNifFile.Read(bytes); var clip = file.Roots.Select(file.ReadControllerSequence).Single();
            portrait.Actor.PlayBaseSequence(file, clip, identity);
            portrait.UpdateProjection(); _screen.SetPortrait(portrait);
            if (_portrait is not null) { RemoveChild(_portrait); _portrait.QueueFree(); }
            _portrait = portrait; _previewRevision = _creation.Revision;
        }
        catch { RemoveChild(portrait); portrait.QueueFree(); throw; }
    }
    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            if (_portrait?.Actor.AnimationError is { } error) throw new InvalidOperationException(error);
            if (_previewRevision != _creation.Revision) RefreshPreview();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error) { Error = error.Message; GD.PushError($"OPENNV_NATIVE_CREATION_UNBOUND {error}"); Failed?.Invoke(error); }
    internal void ReleasePause()
    {
        if (_pausedTree is null) return;
        _pausedTree.Paused = _previousPause; _pausedTree = null;
    }
    public override void _ExitTree() { GetViewport().SizeChanged -= Resize; ReleasePause(); }
}
