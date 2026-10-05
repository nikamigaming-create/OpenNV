using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private static Dictionary<string, string>? _nextSessionOptions;
    private static bool _nextSessionContinue;
    private static bool _nextSessionPauseAfterCheckpointLoad;
    private static RuntimeSaveSlotMetadata? _nextSessionCheckpoint;
    private static string? _scriptEventSourceIdentity;
    private static FalloutScriptEvents? _scriptEvents;
    private bool _continueAfterRestart;
    private bool _pauseAfterCheckpointLoad;
    private RuntimeSaveSlotMetadata? _pendingCheckpointRestore, _restoredNativeCheckpoint;
    private bool _retiringNativeSession;
    private bool _nativeSessionTransitioning;
    private Task? _nativeMenuRead;
    private CanvasLayer? _nativeSessionLayer;
    private NativeGameSessionMenu? _nativeSessionMenu;
    private bool _sessionWasPaused;
    private Input.MouseModeEnum _sessionMouseMode;
    private Control? _sessionPreviousMenu;
    private bool _nativeDeathPresented;

    private FalloutScriptEvents NativeScriptEvents()
    {
        // A scene reload is still the same application session. Changing the
        // selected plugin graph starts a new logical engine session instead.
        var identity = string.Join('\n', _nativePluginStack!.Plugins.Select(plugin =>
            $"{plugin.Plugin.Path}|{plugin.Bytes}|{plugin.MtimeUnixMilliseconds}"));
        if (_scriptEventSourceIdentity != identity)
        {
            _scriptEventSourceIdentity = identity;
            _scriptEvents = new();
        }
        return _scriptEvents!;
    }

    private void AdvanceNativeDeath()
    {
        if (_nativeDeathPresented || _nativeDoorLoading || _nativePlayer is null ||
            _nativeOpeningStageDriver?.Vitals.HitPoints != 0) return;
        CancelNativeManualSave("Player defeated before the pending F5 could commit.");
        _nativeDeathPresented = true;
        if (_nativeXr?.PointAtPipBoy is not null) FocusNativeXrPipBoy(false);
        _nativePlayer.SetModalInput(true);
        OpenNativeSessionMenu(showSaves: false);
        GD.Print("OPENNV_NATIVE_PLAYER_DEFEATED health=0 owner=shared-vitals recovery=save-browser");
    }

    private RuntimeSaveSlotCatalog NativeSaveSlots() => new(Path.GetFullPath(RequireOption(_options, "save-path")), root =>
    {
        if (!root.TryGetProperty("Schema", out var schema) || schema.ValueKind != JsonValueKind.String ||
            !schema.GetString()!.StartsWith("opennv-native-fnv-campaign-save/", StringComparison.Ordinal) ||
            !root.TryGetProperty("SaveCompatibilityId", out var identity) ||
            identity.GetString() != RuntimeLiveContentSource.Current!.SaveCompatibilityId)
            throw new InvalidDataException("This save belongs to a different game or source stack.");
    });

    private FalloutNativeCampaignRestore ReadNativeSave(string path) => FalloutNativeCampaignSave.Read(path,
        RuntimeLiveContentSource.Current!.SaveCompatibilityId, _nativePluginStack!, _nativeVigorContract!,
        _nativeTagSkillContract!, _nativeOpeningGrant!, _nativeTraitFarewellContract!);

    internal RuntimeSaveSlotMetadata CreateNativeCheckpoint(Guid id)
    {
        if (_nativeSessionTransitioning || _nativeDoorLoading || _nativePlayer is null ||
            _nativeActiveCell is null || _nativeOpeningStageDriver is null)
            throw new InvalidOperationException("A checkpoint requires a settled native campaign session.");
        var slot = NativeSaveSlots().Create(id, () => _nativeOpeningStageDriver.PersistWorldState(_nativeActiveCell.Cell.FormKey));
        GD.Print($"OPENNV_NATIVE_CHECKPOINT_CREATED id={slot.Id} save={slot.Path} owner=shared-campaign-state");
        return slot;
    }

    internal RuntimeSaveSlotMetadata LoadNativeCheckpoint(Guid id, bool pauseAfterLoad)
    {
        if (_nativeSessionTransitioning || _nativeDoorLoading || _nativePluginStack is null ||
            _nativeVigorContract is null || _nativeTagSkillContract is null ||
            _nativeOpeningGrant is null || _nativeTraitFarewellContract is null)
            throw new InvalidOperationException("Checkpoint loading requires an indexed native source stack.");
        var slot = NativeSaveSlots().ReadSlot(id.ToString("N"));
        SelectNativeSave(slot, pauseAfterLoad);
        return slot;
    }

    private void SaveNativeMenuSlot()
    {
        try
        {
            if (_nativeDoorLoading || _nativePlayer is null) return;
            if (_nativeOpeningStageDriver!.Vitals.HitPoints == 0)
                throw new InvalidOperationException("Load an earlier save after death; the previous Continue save is preserved.");
            var slot = CreateNativeCheckpoint(Guid.NewGuid());
            CancelNativeManualSave($"Pending F5 superseded by explicit complete menu save {slot.Id}.");
            GD.Print($"OPENNV_NATIVE_SAVE_SLOT_CREATED id={slot.Id} save={slot.Path}");
        }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_NATIVE_SAVE_SLOT_FAILURE {error}");
            if (_nativeSessionMenu is not null) throw;
        }
    }

    private void ToggleNativeSessionMenu()
    {
        if (_nativeSessionTransitioning) return;
        if (_nativeSessionMenu is not null) { _nativeSessionMenu.Back(); return; }
        if (_nativePlayer is null || _nativeDoorLoading || _nativeOpeningStageDriver is null) return;
        if (_nativeXr?.PointAtPipBoy is not null) { FocusNativeXrPipBoy(false); return; }
        if (_nativePlayer.ModalInput) { _nativeXr?.CancelModal(); return; }
        OpenNativeSessionMenu(showSaves: false);
    }

    private void OpenNativeSessionMenu(bool showSaves, Control? previousMenu = null)
    {
        if (_nativeSessionLayer is not null) return;
        _sessionWasPaused = GetTree().Paused; _sessionMouseMode = Input.MouseMode;
        _sessionPreviousMenu = previousMenu; previousMenu?.Hide();
        _nativeSessionLayer = new CanvasLayer { Name = "NativeSessionLayer", Layer = 150, ProcessMode = ProcessModeEnum.Always };
        _nativeSessionMenu = new(NativeSaveSlots(), _nativePlayer is not null, showSaves, _nativeOpeningStageDriver?.Vitals.HitPoints == 0,
            CloseNativeSessionMenu, SaveNativeMenuSlot, LoadNativeSelectedSlot,
            () => RestartNativeSession(false), () => QuitNativeSession(), DescribeNativeSave);
        AddChild(_nativeSessionLayer); _nativeSessionLayer.AddChild(_nativeSessionMenu);
        _nativePlayer?.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = true;
        _nativeSessionMenu.ShowManualSaveReceipt(_nativeManualSaves.Receipt);
        GD.Print($"OPENNV_NATIVE_SESSION_MENU_OPEN browser={showSaves} xr={_nativeXr is not null}");
    }

    private void CloseNativeSessionMenu()
    {
        if (_nativeSessionTransitioning) return;
        if (_nativeDeathPresented && !_retiringNativeSession) return;
        _nativeSessionLayer?.QueueFree(); _nativeSessionLayer = null; _nativeSessionMenu = null;
        _nativePlayer?.SetModalInput(false); GetTree().Paused = _sessionWasPaused; Input.MouseMode = _sessionMouseMode;
        _sessionPreviousMenu?.Show(); _sessionPreviousMenu = null;
        GD.Print("OPENNV_NATIVE_SESSION_MENU_CLOSED");
    }

    private string DescribeNativeSave(RuntimeSaveSlotMetadata slot)
    {
        var location = slot.MapName ?? "Unknown location";
        var parts = location.Split(':');
        if (parts.Length == 2 && uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
        {
            var record = _nativePluginStack!.GetEffective(new(parts[0], id));
            var fields = record.ReadSubrecords().ToArray();
            var label = fields.SingleOrDefault(field => field.Signature == "FULL").Data;
            if (label.IsEmpty) label = fields.SingleOrDefault(field => field.Signature == "EDID").Data;
            if (!label.IsEmpty) location = FalloutDialogueTopic.Text(label.Span);
        }
        var kind = slot.Id == "current" ? "CURRENT" : "SAVE";
        return $"{kind} · {slot.CharacterName} · {location} · HP {slot.HitPoints}\n{slot.WrittenUtc.ToLocalTime():g}";
    }

    private void LoadNativeSelectedSlot(RuntimeSaveSlotMetadata slot)
        => SelectNativeSave(slot, pauseAfterLoad: false);

    private void SelectNativeSave(RuntimeSaveSlotMetadata slot, bool pauseAfterLoad)
    {
        // Validate the complete source-bound state before replacing Continue.
        _ = ReadNativeSave(slot.Path);
        NativeSaveSlots().Activate(slot.Id, preserveCurrent: true);
        GD.Print($"OPENNV_NATIVE_SAVE_SLOT_SELECTED id={slot.Id}");
        RestartNativeSession(true, pauseAfterLoad, slot);
    }

    private async void RestartNativeSession(bool continueSave, bool pauseAfterLoad = false, RuntimeSaveSlotMetadata? checkpoint = null)
    {
        if (_nativeSessionTransitioning) return;
        CancelNativeManualSave("Native session transition started before the pending F5 could commit.");
        _nativeSessionTransitioning = true;
        try
        {
            BeginLoadingScreen(continueSave ? "Preparing saved game" : "Returning to the main menu");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await DrainNativeSourceReaders();
            _nextSessionOptions = new(_options, StringComparer.OrdinalIgnoreCase);
            _nextSessionOptions.Remove("launcher"); _nextSessionOptions.Remove("new-game");
            _nextSessionContinue = continueSave;
            _nextSessionPauseAfterCheckpointLoad = continueSave && pauseAfterLoad;
            _nextSessionCheckpoint = continueSave ? checkpoint : null;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            _retiringNativeSession = true;
            _nativeQuestScripts?.Scripts.Events.EnterMainMenu();
            _nativeScriptStorage?.Controls?.Flush();
            GetTree().Paused = false;
            var error = GetTree().ReloadCurrentScene();
            if (error != Error.Ok) throw new InvalidOperationException($"Session reload failed: {error}.");
        }
        catch (Exception error)
        {
            _nextSessionOptions = null; _nextSessionContinue = false; _nextSessionPauseAfterCheckpointLoad = false;
            _nextSessionCheckpoint = null;
            _retiringNativeSession = false;
            _nativeSessionTransitioning = false;
            GD.PushError($"OPENNV_NATIVE_SESSION_RELOAD_FAILURE {error}");
            DismissLoadingScreen();
            GetTree().Paused = true;
            _nativeSessionMenu?.ShowFailure(error.Message);
        }
    }

    private async Task DrainNativeSourceReaders()
    {
        GetTree().Paused = true;
        var pending = _nativeGridNpcPreparations.Select(item => item.ReadTask).ToList();
        if (_nativeMenuRead is { } menuRead) pending.Add(menuRead);
        if (_nativePlayerMoveRead is { } moveRead) pending.Add(moveRead);
        if (_nativeDoorRead is { } doorRead) pending.Add(doorRead);
        if (_nativeGridRead is { } gridRead) pending.Add(gridRead);
        foreach (var lod in FindChildren("*", "", true, false).OfType<RuntimeNativeExteriorLod>())
            pending.Add(lod.StopSourceReads());
        CancelNativeGridRead();
        try { await Task.WhenAll(pending); }
        catch (Exception readError) { GD.Print($"OPENNV_SESSION_OLD_READ_FINISHED {readError.Message}"); }
    }

    private void OnNativeCloseRequested() => QuitNativeSession();

    private async void QuitNativeSession()
    {
        if (_nativeSessionTransitioning) return;
        CancelNativeManualSave("Native session transition started before the pending F5 could commit.");
        _nativeSessionTransitioning = true;
        try
        {
            await DrainNativeSourceReaders();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            _nativeScriptStorage?.Controls?.Flush();
            _retiringNativeSession = true;
            GD.Print($"OPENNV_NATIVE_SESSION_QUIT prototypes={_nativeNifPrototypes.Count} sourceReaders=drained");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            _nativeSessionTransitioning = false;
            GD.PushError($"OPENNV_NATIVE_SESSION_QUIT_FAILURE {error}");
            _nativeSessionMenu?.ShowFailure(error.Message);
        }
    }
}
