using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeQuestScripts : Node
{
    private readonly FalloutPluginStack _records;
    internal FalloutQuestScripts Scripts { get; }
    private CanvasLayer? _layer;
    private FalloutSourceMessage? _current;
    private bool _pausedBefore;
    private string? _error;
    private readonly FalloutPlayerInventory _inventory;
    private NativeOwnedHudMessages? _hud;
    private bool _worldActive;
    private readonly InputSystem.RuntimeNativeScriptEvents _events;
    private readonly RuntimeNativeUiClock _uiClock;
    private IDisposable? _activationSoundDefault;
    private IDisposable? _vampireQueryDefault;
    internal Func<FalloutCondition, float>? EvaluateMessageCondition { get; set; }
    internal Func<IEnumerable<uint>?>? ActiveMenus { get; set; }
    internal Func<FalloutFormKey, Node3D?> SoundReference { get; set; } =
        _ => throw new NotSupportedException("Script sound reference presentation has no world owner.");
    internal float SoundUnitsToMetres { get; set; }
    internal FalloutNewGameBootstrap? Bootstrap { get; set; }
    internal string? StartupError => _error;
    internal object State => Observe(detailed: true);
    internal object Observe(bool detailed) => new
    {
        scripts = Scripts.Observe(detailed),
        worldActive = _worldActive,
        message = _current,
        hud = _hud?.State,
        error = _error,
        startupMenuExecution = _worldActive ? "world-host" : Bootstrap is null ? "unbound-bootstrap-host" : "source-bootstrap-host",
        bootstrap = Bootstrap?.State
    };

    internal FalloutQuestScriptsSnapshot Capture() => Scripts.Capture(_current);
    internal void ActivateWorld(bool loaded)
    {
        if (_worldActive) return;
        if (loaded) Scripts.Events.LoadGame();
        _worldActive = true;
        _events.Active = true;
    }

    internal RuntimeNativeQuestScripts(FalloutPluginStack records, FalloutQuestState quests, IReadOnlySet<FalloutFormKey> claimed,
        FalloutPlayerInventory inventory, FalloutGlobalState? globals = null, FalloutReferenceWorld? references = null,
        FalloutScriptEvents? events = null, FalloutScriptStorage? storage = null)
    {
        Name = "NativeQuestScripts";
        _records = records;
        _inventory = inventory;
        Scripts = new(records, quests, claimed, inventory, globals, references: references, events: events, storage: storage);
        _events = new(Scripts.Events, () => Scripts.Host?.InvokeFunction);
        _uiClock = new(() => Scripts.Ui);
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue + 1;
    }

    public override void _Ready()
    {
        AddChild(_uiClock);
        AddChild(_events);
        if (RuntimeLiveContentSource.Current is { } source)
        {
            _vampireQueryDefault = Scripts.ActorQueries.BindVampire(() => FalloutExecutableStringTable.ReadVampireQueryDeclaration(
                Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
                    source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe")));
            _activationSoundDefault = Scripts.NoActivationSound.BindDefault(() => FalloutExecutableStringTable.ReadNoActivationSoundDefault(
                Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
                    source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe")));
            AddChild(new NativeOwnedScriptSoundPlayer(Scripts.Sounds, source, Scripts.Menus,
                reference => SoundReference(reference), SoundUnitsToMetres));
            AddChild(new Rendering.NativeOwnedScreenBlood(Scripts.ScreenBlood, source, Scripts.Menus));
        }
        try
        {
            var hudLayer = new CanvasLayer { Name = "NativeHudLayer", Layer = 1, ProcessMode = ProcessModeEnum.Always };
            AddChild(hudLayer);
            _hud = new NativeOwnedHudMessages(_records, _inventory.Notifications);
            hudLayer.AddChild(_hud);
        }
        catch (Exception error)
        {
            _error = error.Message;
            GD.PushError($"OPENNV_HUD_OWNER_UNBOUND {error.Message}");
        }
    }

    public override void _ExitTree()
    {
        _activationSoundDefault?.Dispose(); _activationSoundDefault = null;
        _vampireQueryDefault?.Dispose(); _vampireQueryDefault = null;
    }

    public override void _Process(double delta)
    {
        if (_error is not null) return;
        if (!_worldActive)
        {
            if (Bootstrap is not null)
            {
                try { Bootstrap.Advance(delta, [4, 1007, 2]); }
                catch (Exception error)
                {
                    _error = error.Message;
                    GD.PushError($"OPENNV_NEW_GAME_BOOTSTRAP_FAIL {error}");
                }
                return;
            }
            // The current title path has no player/source-command host yet.
            // Keep its clocks without pretending those menu blocks executed.
            Scripts.Advance(delta, gameMode: false, menus: [4], execute: false);
            return;
        }
        if (_layer is not null || GetTree().Paused)
        {
            var menus = ActiveMenus?.Invoke()?.ToList();
            if (_layer is not null) { menus ??= []; menus.AddRange([1001, 2]); }
            Scripts.Advance(delta, gameMode: false, menus: menus);
            if (_current?.Request is { } request && !Scripts.MessageResults.IsPending(request))
            {
                CloseMessage();
                if (Scripts.TryTakeMessage(out var replacement)) Show(replacement!);
            }
            return;
        }
        if (Scripts.TryTakeMessage(out var restored)) { Show(restored!); return; }
        Scripts.Advance(delta);
        if (Scripts.TryTakeMessage(out var message)) Show(message!);
    }

    private void Show(FalloutSourceMessage message)
    {
        _current = message;
        _pausedBefore = GetTree().Paused;
        GetTree().Paused = true;
        _layer = new CanvasLayer { Name = "NativeMessageLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
        AddChild(_layer);
        try
        {
            message = message.ResolveButtons(_records, condition => (EvaluateMessageCondition ??
                throw new NotSupportedException("Message condition has no gameplay owner."))(condition));
            _current = message;
            _layer.AddChild(new NativeOwnedMessageMenu(message, _records, choice =>
            {
                // A removed canvas may still have an input callback queued.
                // It must never close or answer its replacement.
                if (_current?.Request != message.Request) return;
                var accepted = Scripts.MessageResults.Select(message.Request ??
                    throw new InvalidDataException("Message input has no source request."), choice);
                GD.Print($"OPENNV_SOURCE_MESSAGE_{(accepted ? "ACCEPT" : "SUPERSEDED")} source={message.Form} choice={choice}");
                CloseMessage();
                if (Scripts.TryTakeMessage(out var next)) Show(next!);
            }, error => Fail(message, error)));
            GD.Print($"OPENNV_SOURCE_MESSAGE_OPEN source={message.Form}");
        }
        catch (Exception error)
        {
            Fail(message, error);
        }
    }

    private void CloseMessage()
    {
        _layer?.QueueFree();
        _layer = null;
        _current = null;
        GetTree().Paused = _pausedBefore;
    }

    private void Fail(FalloutSourceMessage message, Exception error)
    {
        _error = error.Message;
        GD.PushError($"OPENNV_SOURCE_MESSAGE_UNBOUND source={message.Form} error={error.Message}");
    }
}
