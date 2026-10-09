using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedExperienceHud : Control
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutExperienceHudSource _source;
    private readonly FalloutExperienceNotifications _owner;
    private readonly NativeOwnedMenuTree _tiles;
    private readonly FalloutInstallationSettings _settings;
    private readonly Func<FalloutExperienceNotificationAdmission> _admission;
    private readonly Func<bool> _pendingLevel, _characterGenerationEnded;
    private readonly Func<FalloutExperienceClockReading> _clock;
    private readonly Dictionary<AudioStreamPlayer, FalloutExperienceSoundReceipt> _voices = [];
    private readonly int _safeZoneScale;
    private Guid _view;
    private NativeViewportLayout? _viewport;
    private FalloutExperienceNotificationFrame? _drawFrame, _submittedFrame;
    private ulong? _submittedIdleRevision;
    private bool _postDrawBound, _retiring;
    internal string? Error { get; private set; }

    internal static NativeOwnedExperienceHud Attach(Node parent, FalloutPluginStack records,
        FalloutExperienceHudSource source, FalloutExperienceNotifications owner,
        Func<FalloutExperienceNotificationAdmission> admission, Func<bool> pendingLevel, Func<bool> characterGenerationEnded,
        Func<FalloutExperienceClockReading> clock, FalloutUiComponentStore? scriptUi = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        NativeOwnedExperienceHud? result = null;
        try
        {
            // All owned resource admission precedes the first Node allocation.
            // A rejected font/art/source arm cannot leave a detached Control.
            var prepared = Prepare(records, source, owner, scriptUi);
            result = new(records, source, owner, prepared.Tiles, prepared.Settings, prepared.SafeZoneScale,
                admission, pendingLevel, characterGenerationEnded, clock);
            parent.AddChild(result);
            if (result.Error is { } error) throw new InvalidOperationException(error);
            return result;
        }
        catch (Exception error)
        {
            owner.RetainFailure(error);
            if (GodotObject.IsInstanceValid(result)) result!.Free();
            throw;
        }
    }

    private static (NativeOwnedMenuTree Tiles, FalloutInstallationSettings Settings, int SafeZoneScale) Prepare(
        FalloutPluginStack records, FalloutExperienceHudSource source, FalloutExperienceNotifications owner,
        FalloutUiComponentStore? scriptUi)
    {
        if (owner.Contract != source.Contract) throw new InvalidDataException("Native XP HUD differs from its actual source owner.");
        var live = records.OwnedSource ?? throw new InvalidOperationException("Native XP HUD has no selected owned source.");
        if (!ReferenceEquals(live, RuntimeLiveContentSource.Current))
            throw new InvalidDataException("XP resource presentation has a foreign current source selection.");
        var settings = FalloutInstallationSettings.Read(live);
        var safeZoneScale = FalloutExecutableStringTable.ReadHudMessageLayout(live.FalloutExecutablePath).SafeZoneScale;
        var tiles = new NativeOwnedMenuTree(source.Menu, name => FalloutGameSettingStrings.Read(records, name), scriptUi);
        var opacity = settings.Number(source.Declaration.OpacitySetting);
        if (opacity <= 0) throw new NotSupportedException("Zero-opacity XP suppression/advancement requires its original source branch owner.");
        // HUDMainMenu starts hidden in XML; its admitted living gameplay
        // surface owns the original menu show transition. Script overrides
        // still take precedence and cannot masquerade as a displayed branch.
        tiles.Bind(source.Menu, "visible", 1);
        if (tiles.Number(source.Menu, "visible") <= 0 || tiles.Number(source.Tile("XPMeter"), "visible") <= 0)
            throw new NotSupportedException("Hidden source XP parent requires its actual suppression/advancement owner.");
        // Preflight both source arms, including original art, atlas, configured
        // bitmap fonts and system colors. No system-font or blank-art fallback.
        foreach (var name in FalloutExperienceHudSource.Tiles.Skip(1))
        { tiles.Bind(source.Tile(name), "visible", 1); tiles.Bind(source.Tile(name), "alpha", opacity * 255); }
        tiles.Text[source.Tile("XPAmount")] = FalloutExperienceHudSource.Integer(source.Declaration.AmountFormat, 1, 16);
        tiles.Text[source.Tile("XPLastLevel")] = FalloutExperienceHudSource.Integer(source.Declaration.LevelFormat, 1, 16);
        tiles.Text[source.Tile("XPNextLevel")] = FalloutExperienceHudSource.Integer(source.Declaration.LevelFormat, 2, 16);
        tiles.Text[source.Tile("XPLabel")] = source.StatsLabel; tiles.Text[source.Tile("XPLevelUp")] = source.LevelUpLabel;
        tiles.ValidateDrawing();
        foreach (var name in FalloutExperienceHudSource.Tiles.Skip(1)) tiles.Bind(source.Tile(name), "visible", 0);
        return (tiles, settings, safeZoneScale);
    }

    private NativeOwnedExperienceHud(FalloutPluginStack records, FalloutExperienceHudSource source, FalloutExperienceNotifications owner,
        NativeOwnedMenuTree tiles, FalloutInstallationSettings settings, int safeZoneScale,
        Func<FalloutExperienceNotificationAdmission> admission, Func<bool> pendingLevel, Func<bool> characterGenerationEnded,
        Func<FalloutExperienceClockReading> clock)
    {
        _records = records; _source = source; _owner = owner; _tiles = tiles; _settings = settings; _safeZoneScale = safeZoneScale;
        _admission = admission; _pendingLevel = pendingLevel; _characterGenerationEnded = characterGenerationEnded; _clock = clock;
        Name = "NativeExperienceHud"; MouseFilter = MouseFilterEnum.Ignore; ProcessMode = ProcessModeEnum.Always;
    }

    public override void _EnterTree()
    {
        try
        {
            _view = _owner.AttachPresentation(_source.Contract);
            _viewport = new(this, Layout); Layout();
            RenderingServer.FramePostDraw += FramePresented; _postDrawBound = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private void Layout()
    {
        // The executable positions the tile in its actual render extent and
        // selected safe-zone inputs. This owner adds no fitted reference size.
        Size = _tiles.Screen = GetViewportRect().Size; Scale = Vector2.One; _tiles.ResolutionConverter = 1;
        var x = _settings.Number("Interface", "iSafeZoneX");
        var y = _settings.Number("Interface", "iSafeZoneY");
        var wideX = _settings.Number("Interface", "iSafeZoneXWide");
        var wideY = _settings.Number("Interface", "iSafeZoneYWide");
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(wideX) || !float.IsFinite(wideY))
            throw new InvalidDataException("XP original safe-zone input is nonfinite.");
        // Both source branches publish the same placement when their inputs
        // coincide. Distinct inputs need the original viewport selector; a
        // fitted aspect-ratio guess cannot own that source branch.
        if (x != wideX || y != wideY)
            throw new NotSupportedException("XP source widescreen selector is unowned for distinct configured safe-zone inputs.");
        var meter = _source.Tile("XPMeter");
        _tiles.Bind(meter, "x", Size.X - _safeZoneScale * x - _source.Declaration.MeterInsetX);
        _tiles.Bind(meter, "y", Size.Y - _safeZoneScale * y - _source.Declaration.MeterInsetY);
        _tiles.Bind(meter, "user7", _tiles.Number(meter, "y"));
    }
    public override void _Process(double delta)
    {
        if (Error is not null || _view == Guid.Empty || _retiring) return;
        try
        {
            var admission = _admission();
            Visible = admission == FalloutExperienceNotificationAdmission.Ready;
            if (admission == FalloutExperienceNotificationAdmission.Ready && !IsVisibleInTree())
                admission = FalloutExperienceNotificationAdmission.Held;
            _owner.Tick(_clock(), admission, _pendingLevel(), _characterGenerationEnded());
            if (admission != FalloutExperienceNotificationAdmission.Ready)
            { Visible = false; _drawFrame = null; _submittedFrame = null; _submittedIdleRevision = null; return; }
            Visible = true;
            foreach (var request in _owner.PendingSounds(_view)) Play(request);
            _drawFrame = _owner.Frame(_view);
            if (_drawFrame is { } frame) Bind(frame); else HideTiles();
            ValidatePublication(_drawFrame);
            _tiles.ValidateDrawing(); QueueRedraw();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Bind(FalloutExperienceNotificationFrame frame)
    {
        var opacity = _settings.Number(_source.Declaration.OpacitySetting) * 255;
        foreach (var name in FalloutExperienceHudSource.Tiles.Skip(1))
        {
            var level = name == "XPLevelUp"; var tile = _source.Tile(name);
            _tiles.Bind(tile, "visible", (level ? frame.LevelVisible : frame.MeterVisible) ? 1 : 0);
            _tiles.Bind(tile, "alpha", (level ? frame.LevelAlpha : frame.MeterAlpha) * opacity);
        }
        var pointer = _source.Tile("XPPointer");
        var min = _tiles.Number(pointer, "_x_min"); var max = _tiles.Number(pointer, "_x_max");
        if (!float.IsFinite(min) || !float.IsFinite(max) || max <= min)
            throw new InvalidDataException("XP source pointer interval is invalid.");
        _tiles.Bind(pointer, "x", min + (max - min) * frame.PointerFraction);
        _tiles.Text[_source.Tile("XPAmount")] = FalloutExperienceHudSource.Integer(_source.Declaration.AmountFormat, frame.Amount, 16);
        var buffer = frame.LevelBufferIsShort ? 3 : 16;
        _tiles.Text[_source.Tile("XPLastLevel")] = FalloutExperienceHudSource.Integer(_source.Declaration.LevelFormat, frame.LastLevel, buffer);
        _tiles.Text[_source.Tile("XPNextLevel")] = FalloutExperienceHudSource.Integer(_source.Declaration.LevelFormat, frame.NextLevel, buffer);
        var levelText = _source.Tile("XPLevelUp");
        if (frame.LevelTimestamp is { } stamp) _tiles.Bind(levelText, "user13", stamp);
        _tiles.Bind(levelText, "user14", frame.LevelTextStarted ? 1 : 0);
    }
    private void HideTiles()
    {
        foreach (var name in FalloutExperienceHudSource.Tiles.Skip(1)) _tiles.Bind(_source.Tile(name), "visible", 0);
    }
    private void ValidatePublication(FalloutExperienceNotificationFrame? frame)
    {
        if (_tiles.Number(_source.Menu, "visible") <= 0 || _tiles.Number(_source.Tile("XPMeter"), "visible") <= 0)
            throw new NotSupportedException("XP source/script parent suppression has no admitted phase retirement owner.");
        var opacity = _settings.Number(_source.Declaration.OpacitySetting) * 255;
        foreach (var name in FalloutExperienceHudSource.Tiles.Skip(1))
        {
            var level = name == "XPLevelUp"; var tile = _source.Tile(name);
            var shown = frame is not null && (level ? frame.LevelVisible : frame.MeterVisible);
            if (_tiles.Number(tile, "visible") != (shown ? 1 : 0) || shown &&
                _tiles.Number(tile, "alpha") != (level ? frame!.LevelAlpha : frame!.MeterAlpha) * opacity)
                throw new NotSupportedException("XP source/script visibility or clock override differs from the submitted notification phase.");
        }
    }
    public override void _Draw()
    {
        if (Error is not null || !Visible || _retiring) return;
        try
        {
            ValidatePublication(_drawFrame);
            _tiles.Draw(this); _submittedFrame = _drawFrame;
            _submittedIdleRevision = _drawFrame is null ? _owner.IdleRevision(_view) : null;
        }
        catch (Exception error) { Fail(error); }
    }
    private void FramePresented()
    {
        if (Error is not null || !IsVisibleInTree() || _retiring) return;
        var submitted = _submittedFrame; var idle = _submittedIdleRevision;
        _submittedFrame = null;
        _submittedIdleRevision = null;
        try
        {
            if (submitted is { } frame && _owner.CurrentSubmission(frame)) _owner.Presented(frame, _clock());
            else if (idle is { } revision && _owner.CurrentIdleSubmission(_view, revision)) _owner.PresentedIdle(_view, revision);
        }
        catch (Exception error) { Fail(error); }
    }
    private long _menuSoundOccurrence;
    private void Play(FalloutExperienceSoundReceipt request)
    {
        var record = FalloutSoundRecordReader.Find(_records, request.EditorId);
        var descriptor = FalloutSoundRecordReader.Read(_records, record.FormKey);
        if (descriptor.IsLooping) throw new NotSupportedException("XP notification source sound is looping without its explicit retirement owner.");
        var player = NativeOwnedSoundPlayback.CreateMenu(descriptor, _records,
            NativeOwnedSoundPlayback.MenuCall(this, checked(++_menuSoundOccurrence), descriptor.FormKey));
        try
        {
            player.ProcessMode = ProcessModeEnum.Always; _voices.Add(player, request);
            player.Finished += () => Finished(player);
            player.TreeExiting += () => VoiceExited(player);
            AddChild(player); _owner.SoundStarted(_view, request); player.Play();
            if (!player.Playing) throw new InvalidOperationException("XP source sound did not acquire its actual native playback owner.");
        }
        catch (Exception error)
        {
            _voices.Remove(player); Fail(error);
            if (GodotObject.IsInstanceValid(player)) player.Free();
            throw;
        }
    }
    private void Finished(AudioStreamPlayer player)
    {
        try
        {
            if (!_voices.TryGetValue(player, out var request)) throw new InvalidOperationException("XP native sound retired twice.");
            _owner.SoundFinished(_view, request.Sequence, request.EditorId); _voices.Remove(player); player.QueueFree();
        }
        catch (Exception error) { Fail(error); }
    }
    private void VoiceExited(AudioStreamPlayer player)
    {
        if (!_voices.Remove(player)) return;
        if (_retiring) return;
        Fail(new InvalidOperationException("XP native voice was stopped/retired without its actual finite completion receipt."));
    }
    private void Fail(Exception error)
    {
        var first = Error is null;
        Error ??= error.Message; _owner.RetainFailure(error); _drawFrame = null; _submittedFrame = null; _submittedIdleRevision = null;
        if (first) GD.PushError($"OPENNV_EXPERIENCE_NOTIFICATION_FAIL {error.Message}");
    }
    // The actual coordinator session-retirement transaction calls this before
    // source disposal. Cancellation releases native voices; it never emits a
    // Finished receipt, retires a UI sequence or makes advancement ready.
    internal void RetireForSession()
    {
        if (_retiring) return;
        _retiring = true; Visible = false; _drawFrame = null; _submittedFrame = null; _submittedIdleRevision = null;
        foreach (var player in _voices.Keys.ToArray()) if (GodotObject.IsInstanceValid(player)) player.Stop();
        _voices.Clear();
        if (_view != Guid.Empty) { _owner.DetachPresentation(_view); _view = Guid.Empty; }
    }
    public override void _ExitTree()
    {
        _viewport?.Dispose(); _viewport = null;
        if (_postDrawBound) { RenderingServer.FramePostDraw -= FramePresented; _postDrawBound = false; }
        if (!_retiring && _voices.Count != 0) _owner.RetainFailure(new InvalidOperationException("XP native sound owner exited before its actual finite retirement."));
        foreach (var player in _voices.Keys) if (GodotObject.IsInstanceValid(player)) player.Stop();
        _voices.Clear();
        if (_view != Guid.Empty) { _owner.DetachPresentation(_view); _view = Guid.Empty; }
    }
}
