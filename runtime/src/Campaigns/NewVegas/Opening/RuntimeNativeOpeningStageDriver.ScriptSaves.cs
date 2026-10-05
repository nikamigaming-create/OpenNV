using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    // The coordinator owns ordinary slot creation and actual menu/loading
    // admission. Both are required before source ForceSave is admitted.
    internal Func<Guid, RuntimeSaveSlotMetadata>? SourceManualSaveWriter { get; set; }
    internal Func<string?>? SourceManualSaveBlocker { get; set; }

    private void BindSourceManualSaves()
    {
        if (SourceManualSaveWriter is not { } writer || SourceManualSaveBlocker is null) return;
        _scripts.ScriptManualSaves.Bind(writer, receipt =>
        {
            ExecutionError = $"Source ForceSave request {receipt.Generation} failed: {receipt.Error}";
            Godot.GD.PushError($"OPENNV_NATIVE_SOURCE_SAVE_DIVERGENCE: {ExecutionError}");
        }, Godot.Engine.GetProcessFrames);
    }

    private void DrainSourceManualSaves()
    {
        var requests = _scripts.ScriptManualSaves;
        requests.AdvancePhase();
        requests.Drain(() => _saveRequested
            ? throw new NotSupportedException("Concurrent AutoSave and ForceSave require persistent save-request ordering.")
            : SourceManualSaveBlocker?.Invoke() ??
            (_scripts.References!.PlayerMoves.Pending ? "player-move" : SaveContinuationBlocker));
    }
}
