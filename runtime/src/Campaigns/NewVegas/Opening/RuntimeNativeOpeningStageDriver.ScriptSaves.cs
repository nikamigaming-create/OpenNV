using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    // The coordinator owns ordinary slot creation and actual menu/loading
    // admission. Both are required before source ForceSave is admitted.
    internal Func<Guid, RuntimeSaveSlotMetadata>? SourceManualSaveWriter { get; set; }
    internal Func<string?>? SourceManualSaveBlocker { get; set; }
    internal FalloutScriptManualSaveRequests ManualSourceSaveRequests => _scripts.ScriptManualSaves;
    private string? SourceManualSaveFailure => _scripts.ScriptManualSaves.Receipt is { Disposition: "failed" } receipt
        ? $"Source ForceSave request {receipt.Generation} failed: {receipt.Error}" : null;
    private string? SourceAnimationSoundSaveBlocker => _scripts.References!.AnimationSoundSaveBlocker ?? _playerPackage?.AudioSaveBlocker;
    // Autosave only needs a current refusal. Full finite-voice matching and
    // source/media validation belong to actual save preparation, not every
    // waiting gameplay frame. One unsettled ledger is sufficient to defer.
    private string? AutoSaveAudioBlocker => _playerPackage?.AudioCaptureReady == false ? "player-audio-pending" :
        _scripts.References!.HasPendingAnimationSoundCapture ? "animation-audio-pending" : null;

    private string? ObserveAutoSaveBlocker()
    {
        if (SaveInteractionBlocker is { } interaction) return interaction;
        var world = _scripts.References!;
        if (world.PlayerMoves.Pending) return "player-move";
        if (world.TransientExplosions.SaveBlocker is { } explosion) return explosion;
        if (StageResultsSaveBlocker is { } stage) return stage;
        // Recheck whichever real reference owner blocked the previous attempt
        // before searching unrelated families. This retains no ready result.
        if (_saveAdmissionBlocker == "actor-procedure-initialization" && world.HasPendingProcedureCapture)
            return "actor-procedure-initialization";
        if (_saveAdmissionBlocker == "actor-corpse-continuation" && world.HasPendingCorpseEquipmentCapture)
            return "actor-corpse-continuation";
        return AutoSaveAudioBlocker ?? SaveContinuationBlocker;
    }

    private void BindSourceManualSaves()
    {
        if (SourceManualSaveWriter is not { } writer || SourceManualSaveBlocker is null) return;
        _scripts.ScriptManualSaves.Bind(writer, receipt =>
        {
            // The requesting source invocation has already retired. A failed
            // asynchronous writer retains its receipt and capture refusal;
            // it cannot become a failure of unrelated later instructions.
            Godot.GD.PushError($"OPENNV_NATIVE_SOURCE_SAVE_DIVERGENCE: Source ForceSave request {receipt.Generation} failed: {receipt.Error}");
        }, Godot.Engine.GetProcessFrames);
    }

    private void DrainSourceManualSaves()
    {
        var requests = _scripts.ScriptManualSaves;
        requests.AdvancePhase();
        requests.Drain(ObserveOriginalSourceManualSaveBlocker);
    }

    internal string? ObserveOriginalSourceManualSaveBlocker() => _saveRequested
        ? throw new NotSupportedException("Concurrent AutoSave and ForceSave require persistent save-request ordering.")
        : SourceManualSaveBlocker?.Invoke() ??
        (_scripts.References!.PlayerMoves.Pending ? "player-move" : SaveContinuationBlocker ?? SourceAnimationSoundSaveBlocker);
}
