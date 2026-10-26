using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeHudMessageAudit : Node
{
    private NativeOwnedHudMessages _hud = null!;
    private FalloutHudNotifications _queue = null!;
    private FalloutPluginStack _records = null!;
    private RuntimeLiveContentSource _source = null!;
    private int _frames;
    private string? _capture;
    private bool _shown;
    private int _pendingWhileHidden;
    private bool _objectiveMode;
    private bool _noteMode;
    private bool _noteRestored;
    private bool _registeredNoteVoice;
    private FalloutScriptSounds _sounds = null!;
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args is [var controlRoot, "--controls"])
            {
                _controlMode = true;
                CheckControlHandoff(controlRoot);
                return;
            }
            _noteMode = args.Length == 3 && args[1] == "--note";
            _objectiveMode = args.Length is 4 or 5 && args[1] == "--objective";
            if (!_objectiveMode && !_noteMode && args.Length is < 1 or > 2) throw new ArgumentException("Expected Data [capture] or Data --objective quest index [capture].");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            _source = RuntimeLiveContentSource.Current!;
            _records = FalloutPluginStack.Load(_source.PluginSources);
            var inventory = new FalloutPlayerInventory();
            _queue = inventory.Notifications;
            var quests = new FalloutQuestState(_records, _queue);
            if (_noteMode)
            {
                using var world = new OpenNV.Runtime.World.Cells.FalloutReferenceWorld(_records);
                world.AcquirePlayerNote(FalloutDialogueTopic.Find(_records, "NOTE", args[2]).FormKey, _queue);
            }
            else if (_objectiveMode)
            {
                // Component presentation check, never an ordinary campaign save
                // or evidence that the player earned this objective.
                quests.ApplyObjective(FalloutDialogueTopic.Find(_records, "QUST", args[2]).FormKey,
                    uint.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), true, true);
                _capture = args.Length == 5 ? args[4] : null;
            }
            else
            {
                var scripts = new FalloutQuestScripts(_records, quests, new HashSet<FalloutFormKey>(), inventory);
                // A fresh quest clock has its authored initial phase; zero elapsed
                // time is not an invocation. Advance bounded simulation frames.
                for (var frame = 0; frame < 600 && _queue.Capture().Pending.Count == 0; frame++) scripts.Advance(1d / 60);
                _capture = args.Length == 2 ? args[1] : null;
            }
            if (_queue.Capture().Pending.Count == 0) throw new InvalidDataException("Selected source scripts emitted no HUD events.");
            _pendingWhileHidden = _queue.Capture().Pending.Count;
            var menus = new FalloutScriptMenus();
            _sounds = new FalloutScriptSounds(_records, menus);
            AddChild(new NativeOwnedScriptSoundPlayer(_sounds, _source, menus));
            _hud = new NativeOwnedHudMessages(_records, _queue, () => _shown, quests, noteSounds: _sounds);
            AddChild(_hud);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    public override void _Process(double delta)
    {
        if (_controlMode) return;
        if (_hud is null || ++_frames < 4) return;
        if (!_shown)
        {
            if (_queue.Current is not null || _queue.Capture().Pending.Count != _pendingWhileHidden)
            {
                GD.PushError("OPENNV_HUD_AUDIT_FAIL Hidden gameplay consumed unseen notifications.");
                GetTree().Quit(1); return;
            }
            _shown = true; _frames = 0; return;
        }
        if (_hud.Error is not null || _queue.Current is null)
        {
            GD.PushError("OPENNV_HUD_AUDIT_FAIL " + _hud.Error);
            GetTree().Quit(1);
            return;
        }
        if (_objectiveMode && _queue.Elapsed < 0.6) return;
        if (_noteMode && !_queue.Current.Presented)
        {
            GD.PushError("OPENNV_HUD_AUDIT_FAIL Note audio/presentation handoff was not committed.");
            GetTree().Quit(1); return;
        }
        if (_noteMode)
        {
            if (_sounds.LastRequest is not { Id: 1, Selection.Play: true } request || _sounds.LastMedia is not { } media ||
                !media.Path.StartsWith(request.Source.LogicalPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                !double.IsFinite(media.Duration) || media.Duration <= 0 || media.Sha256.Length != 64)
            {
                GD.PushError("OPENNV_HUD_AUDIT_FAIL No single source-selected folder sound receipt."); GetTree().Quit(1); return;
            }
            if (!_noteRestored)
            {
                _registeredNoteVoice |= _sounds.ActiveVoices == 1 && _records.SoundVoices.ActiveVoices == 1;
                if (_sounds.ActiveVoices != 0) return;
                if (!_registeredNoteVoice || _records.SoundVoices.ActiveVoices != 0 || _sounds.LastDisposition != "source-sound-completed")
                {
                    GD.PushError("OPENNV_HUD_AUDIT_FAIL Note voice was not registered and completed exactly once."); GetTree().Quit(1); return;
                }
                var cold = new FalloutHudNotifications();
                cold.Restore(JsonSerializer.Deserialize<FalloutHudNotificationsSnapshot>(JsonSerializer.Serialize(_queue.Capture()))!);
                _hud.QueueFree();
                _queue = cold;
                _hud = new NativeOwnedHudMessages(_records, cold, () => _shown, noteSounds: _sounds);
                AddChild(_hud); _noteRestored = true; _frames = 0; return;
            }
            if (_sounds.ActiveVoices != 0 || _records.SoundVoices.ActiveVoices != 0)
            {
                GD.PushError("OPENNV_HUD_AUDIT_FAIL Cold presentation replayed the note sound."); GetTree().Quit(1); return;
            }
            GD.Print("OPENNV_NOTE_HUD_SOUND_PASS actualFolderVariant=true finiteRegisteredVoice=true completed=true coldPresentedNoReplay=true audioParity=unverified");
        }
        GD.Print("OPENNV_HUD_AUDIT_STATE " + JsonSerializer.Serialize(_hud.State));
        if (_capture is not null && DisplayServer.GetName() != "headless")
            GetViewport().GetTexture().GetImage().SavePng(_capture);
        GD.Print($"OPENNV_HUD_AUDIT_PASS source={(_noteMode ? "owned-note-component" : _objectiveMode ? "owned-objective-component" : "actual-start-enabled-scripts")} queue=source-order hidden=retained drawing=original-tiles-and-fonts parity=unverified");
        GetTree().Quit();
    }
    public override void _ExitTree()
    {
        _records?.Dispose();
        _source?.Dispose();
    }
}
