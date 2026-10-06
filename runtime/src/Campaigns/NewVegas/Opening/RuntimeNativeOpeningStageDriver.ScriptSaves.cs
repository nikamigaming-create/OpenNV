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
    private string? SourceFiniteAudioSaveBlocker => _scripts.References!.PendingAnimationSoundCaptureCount != 0 &&
        _scripts.References.PendingAnimationSoundFiniteVoiceWait() is { Count: > 0 } ? "source-finite-audio" : null;

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
        (_scripts.References!.PlayerMoves.Pending ? "player-move" : SaveContinuationBlocker ?? SourceFiniteAudioSaveBlocker);
}
