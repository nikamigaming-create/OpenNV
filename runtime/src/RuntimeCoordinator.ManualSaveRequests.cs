using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly RuntimeManualSaveRequests _nativeManualSaves = new();
    private readonly Guid _nativeManualSaveSession = Guid.NewGuid();

    // F5 is a live request. The pause-menu writer and diagnostic checkpoint.save
    // retain their existing synchronous return and complete-save validation.
    private void SaveNativeManualSlot()
    {
        try
        {
            if (_nativeSessionTransitioning || _retiringNativeSession || _nativeDoorLoading ||
                _nativePlayer is null || _nativeActiveCell is null || _nativeOpeningStageDriver is null)
                throw new InvalidOperationException("F5 has no settled native session.");
            var source = RuntimeLiveContentSource.Current?.SaveCompatibilityId ??
                throw new InvalidOperationException("F5 has no selected source identity.");
            var receipt = _nativeManualSaves.Request(_nativeManualSaveSession, source, Engine.GetProcessFrames());
            GD.Print($"OPENNV_NATIVE_MANUAL_SAVE_REQUEST generation={receipt.Generation} slot={receipt.Slot:N} count={receipt.RequestCount} disposition=pending");
        }
        catch (Exception error) { GD.PushError($"OPENNV_NATIVE_SAVE_SLOT_FAILURE {error}"); }
    }

    private void DrainNativeManualSave()
    {
        if (!_nativeManualSaves.Pending) return;
        var prior = _nativeManualSaves.Receipt!;
        var source = RuntimeLiveContentSource.Current?.SaveCompatibilityId ?? "unbound-source";
        _nativeManualSaves.Drain(_nativeManualSaveSession, source, Engine.GetProcessFrames(), () =>
        {
            if (_nativeSessionTransitioning || _retiringNativeSession || _nativePlayer is null ||
                _nativeActiveCell is null || _nativeOpeningStageDriver is null)
                return new(RuntimeManualSaveAdmissionKind.Refused, "session-transition");
            if (_nativeDoorLoading || _nativeLoadingLayer is not null)
                return new(RuntimeManualSaveAdmissionKind.Refused, "loading");
            if (NativeActiveMenus()?.Any() == true || GetTree().Paused)
                return new(RuntimeManualSaveAdmissionKind.Refused, "native-menu-or-pause");
            return _nativeOpeningStageDriver.ObserveManualSaveAdmission();
        }, CreateNativeCheckpoint);
        var current = _nativeManualSaves.Receipt!;
        if (current.Disposition != prior.Disposition)
        {
            if (current.Disposition == "completed")
                GD.Print($"OPENNV_NATIVE_SAVE_SLOT_CREATED id={current.CommittedSlot!.Id} save={current.CommittedSlot.Path} request={current.Generation}");
            else GD.PushError($"OPENNV_NATIVE_MANUAL_SAVE_FAILURE generation={current.Generation} disposition={current.Disposition} error={current.Error}");
            _nativeSessionMenu?.ShowManualSaveReceipt(current);
        }
    }

    private void CancelNativeManualSave(string reason)
    {
        if (!_nativeManualSaves.Pending) return;
        _nativeManualSaves.Cancel(reason);
        GD.Print($"OPENNV_NATIVE_MANUAL_SAVE_CANCELLED generation={_nativeManualSaves.Receipt!.Generation} reason={reason}");
    }
}
