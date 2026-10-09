using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal Func<Guid, RuntimeSaveSlotMetadata>? SourceManualSaveWriter { get; set; }
    internal Func<string?>? SourceManualSaveBlocker { get; set; }
    internal Action? OrderedSaveQueueChanged { get; set; }
    internal FalloutScriptManualSaveRequests ManualSourceSaveRequests => _scripts.ScriptManualSaves;
    private string? SourceManualSaveFailure => _scripts.ScriptManualSaves.Order.Head is
    { Disposition: RuntimeSaveRequestDisposition.Failed, Origin: not (RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu) } failed
        ? failed.Error : null;
    internal string? SourceAnimationSoundSaveBlocker => _scripts.References!.AnimationSoundSaveBlocker;

    private void BindSourceManualSaves()
    {
        if (SourceManualSaveWriter is not { } writer || SourceManualSaveBlocker is null)
            throw new NotSupportedException("Native campaign has no complete ordered save writer/admission owner.");
        _scripts.ScriptManualSaves.Bind(writer,
            receipt => GD.PushError($"OPENNV_SOURCE_SAVE_FAILURE order={receipt.Generation} request={receipt.Slot:N} error={receipt.Error}"),
            Engine.GetProcessFrames,
            new(_saveCompatibilityId, _savePath, id => Path.Combine(_savePath + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json")),
            WriteOrderedContinue);
    }

    private RuntimeSaveSlotMetadata WriteOrderedContinue(RuntimeSaveRequest request)
    {
        if (_scripts.ScriptManualSaves.Order.Writing?.Order != request.Order || request.Destination != RuntimeSaveRequestDestination.Continue)
            throw new InvalidOperationException("Continue writer lacks the actual shared head lease.");
        _ = PersistWorldState(_activeCell);
        return new("current", _savePath, FalloutNativeCampaignSave.ExpectedSchema, _playerName, null, null, File.GetLastWriteTimeUtc(_savePath));
    }

    internal void RequestNativeSave(RuntimeSaveRequestOrigin origin, RuntimeSaveNativeSite site)
    {
        _scripts.ScriptManualSaves.RequestNative(origin, site);
    }

    private void DrainSourceManualSaves()
    {
        var requests = _scripts.ScriptManualSaves;
        requests.AdvancePhase();
        requests.Drain(ObserveOriginalSourceManualSaveBlocker);
        OrderedSaveQueueChanged?.Invoke();
    }

    internal string? ObserveOriginalSourceManualSaveBlocker() =>
        SourceManualSaveBlocker?.Invoke() ?? (_scripts.References!.PlayerMoves.Pending ? "player-move" :
            SaveContinuationBlocker ?? SourceAnimationSoundSaveBlocker);
}
