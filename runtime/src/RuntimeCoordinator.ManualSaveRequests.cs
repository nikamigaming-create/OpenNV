using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly RuntimeManualSaveRequests _nativeManualSaves = new();
    private readonly Guid _nativeManualSaveSession = Guid.NewGuid();
    private RuntimeNativeManualSavePreparation? _nativeManualSavePreparation;
    private CanvasLayer? _nativeManualSaveStatusLayer;
    private NativeManualSaveStatus? _nativeManualSaveStatus;
    private RuntimeManualSaveReceipt? _publishedNativeManualSave;

    private void SaveNativeManualSlot() => RequestNativeManualSave(RuntimeManualSaveOrigin.PlayerInput);

    private RuntimeManualSaveReceipt RequestNativeManualSave(RuntimeManualSaveOrigin origin)
    {
        var source = RuntimeLiveContentSource.Current;
        var identity = source?.SaveCompatibilityId ?? "unbound-source";
        if (_nativeManualSaves.Pending && _nativeManualSaves.Receipt!.SourceCompatibilityId != identity)
            CancelNativeManualSave("The selected source stack changed before manual save committed.");
        var receipt = _nativeManualSaves.Request(_nativeManualSaveSession, identity, Engine.GetProcessFrames(), origin);
        GD.Print($"OPENNV_NATIVE_MANUAL_SAVE_REQUEST generation={receipt.Generation} slot={receipt.Slot:N} count={receipt.RequestCount} origin={origin} disposition=pending");
        if (_nativeManualSavePreparation is not null)
        {
            PublishNativeManualSave(receipt);
            return receipt;
        }
        try
        {
            if (_nativeSessionTransitioning || _retiringNativeSession || _nativeDoorLoading ||
                _nativePlayer is null || _nativeActiveCell is null || _nativeOpeningStageDriver is null ||
                _nativePluginStack is null || _nativeReferences is null || _nativeQuestScripts is null || source is null)
                throw new InvalidOperationException("Manual save has no settled native session/source owner.");
            if (_nativeOpeningStageDriver.Vitals.HitPoints <= 0)
                throw new InvalidOperationException("Load an earlier save after death; the previous Continue save is preserved.");
            if (origin == RuntimeManualSaveOrigin.PlayerInput && (_nativePlayer.ModalInput || GetTree().Paused) ||
                origin == RuntimeManualSaveOrigin.SessionMenu && _nativeSessionMenu is null)
                throw new InvalidOperationException("Manual save input has no matching player or session-menu owner.");
            var player = _nativePlayer; var driver = _nativeOpeningStageDriver; var records = _nativePluginStack;
            var world = _nativeReferences; var root = _nativeCurrentCellRoot; var cell = _nativeActiveCell.Cell.FormKey;
            var scripts = _nativeQuestScripts;
            var menu = _nativeSessionMenu;
            string? Invalidation() =>
                _nativeSessionTransitioning || _retiringNativeSession ? "Native session transition started before manual save committed." :
                _nativeDoorLoading || _nativeLoadingLayer is not null ? "Native loading began before manual save committed." :
                !ReferenceEquals(RuntimeLiveContentSource.Current, source) || !ReferenceEquals(_nativePlayer, player) ||
                !ReferenceEquals(_nativeOpeningStageDriver, driver) || !ReferenceEquals(_nativePluginStack, records) ||
                !ReferenceEquals(_nativeQuestScripts, scripts) || !GodotObject.IsInstanceValid(scripts) || !scripts.IsInsideTree() ||
                !ReferenceEquals(_nativeReferences, world) || !ReferenceEquals(_nativeCurrentCellRoot, root) ||
                _nativeActiveCell?.Cell.FormKey != cell || !ReferenceEquals(_nativeSessionMenu, menu)
                    ? "Manual save source, native session, cell or menu generation changed." :
                driver.Vitals.HitPoints <= 0 ? "Player defeated before manual save committed." : null;
            RuntimeManualSaveAdmission Admission()
            {
                if (scripts.Scripts.Sounds.ActiveVoices != 0 || scripts.Scripts.Sounds.LastError is not null)
                    return new(RuntimeManualSaveAdmissionKind.Refused,
                        $"unsupported-script-sound-continuation: active={scripts.Scripts.Sounds.ActiveVoices} error={scripts.Scripts.Sounds.LastError ?? "none"}");
                var menus = NativeActiveMenus() ?? [];
                if (menus.Any(code => menu is null || code != 1013 && code != FalloutScriptMenus.Category(1013)))
                    return new(RuntimeManualSaveAdmissionKind.Refused, "unsupported-native-menu");
                return driver.ObserveManualSaveAdmission(_nativeManualSavePreparation?.SourceOrder, _nativeManualSaves.Receipt);
            }
            var preparation = new RuntimeNativeManualSavePreparation(_nativeManualSaves, _nativeManualSaveSession, identity,
                player, records.SoundVoices, Invalidation, Admission, CreateNativeCheckpoint,
                PublishNativeManualSave, () => _nativeManualSavePreparation = null, sourceProducers: [scripts],
                sourceRequests: driver.ManualSourceSaveRequests, originalSourceBlocker: driver.ObserveOriginalSourceManualSaveBlocker);
            _nativeManualSavePreparation = preparation;
            AddChild(preparation);
            preparation.Begin();
        }
        catch (Exception error)
        {
            if (_nativeManualSaves.Pending) _nativeManualSaves.Fail(error.Message);
            _nativeManualSavePreparation?.Cancel("Save preparation could not start: " + error.Message);
            PublishNativeManualSave(_nativeManualSaves.Receipt!);
            GD.PushError($"OPENNV_NATIVE_SAVE_SLOT_FAILURE {error}");
        }
        return _nativeManualSaves.Receipt!;
    }

    private void PublishNativeManualSave(RuntimeManualSaveReceipt receipt)
    {
        if (_publishedNativeManualSave == receipt) return;
        var prior = _publishedNativeManualSave;
        _publishedNativeManualSave = receipt;
        if (_nativeSessionMenu is null)
        {
            if (_nativeManualSaveStatus is null)
            {
                _nativeManualSaveStatusLayer = new CanvasLayer { Name = "ManualSaveStatusLayer", Layer = 151, ProcessMode = ProcessModeEnum.Always };
                _nativeManualSaveStatus = new(CancelNativeManualSaveFromInput, CloseNativeManualSaveStatus);
                AddChild(_nativeManualSaveStatusLayer); _nativeManualSaveStatusLayer.AddChild(_nativeManualSaveStatus);
            }
            _nativeManualSaveStatus.ShowReceipt(receipt);
        }
        _nativeSessionMenu?.ShowManualSaveReceipt(receipt);
        if (receipt.Disposition != "pending" && (prior is null || prior.Generation != receipt.Generation || prior.Disposition != receipt.Disposition))
        {
            if (receipt.Disposition == "completed")
                GD.Print($"OPENNV_NATIVE_SAVE_SLOT_CREATED id={receipt.CommittedSlot!.Id} save={receipt.CommittedSlot.Path} request={receipt.Generation}");
            else GD.PushError($"OPENNV_NATIVE_MANUAL_SAVE_FAILURE generation={receipt.Generation} disposition={receipt.Disposition} error={receipt.Error}");
        }
    }

    private void CancelNativeManualSaveFromInput() => CancelNativeManualSave("Player cancelled save preparation; the previous Continue save is retained.");

    private void CancelNativeManualSave(string reason)
    {
        if (!_nativeManualSaves.Pending) return;
        if (_nativeManualSavePreparation is { } preparation) preparation.Cancel(reason);
        else { _nativeManualSaves.Cancel(reason); PublishNativeManualSave(_nativeManualSaves.Receipt!); }
        GD.Print($"OPENNV_NATIVE_MANUAL_SAVE_CANCELLED generation={_nativeManualSaves.Receipt!.Generation} reason={reason}");
    }

    private void CloseNativeManualSaveStatus()
    {
        if (_nativeManualSaves.Pending) { CancelNativeManualSaveFromInput(); return; }
        _nativeManualSaveStatusLayer?.QueueFree(); _nativeManualSaveStatusLayer = null; _nativeManualSaveStatus = null;
    }

    private string? NativeSourceManualSaveBlocker()
    {
        if (_nativeSessionTransitioning || _retiringNativeSession) return "session-transition";
        if (_nativeDoorLoading || _nativeLoadingLayer is not null) return "loading";
        if (_nativeManualSavePreparation?.PermitsOriginalSourceDrain() == true) return null;
        return GetTree().Paused ? "paused" : NativeActiveMenus()?.Any() == true ? "native-menu" : null;
    }
}
