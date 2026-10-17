using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal RuntimeManualSaveAdmission ObserveManualSaveAdmission(RuntimeManualSaveSourceOrder? sourceOrder = null,
        RuntimeManualSaveReceipt? manual = null)
    {
        if (Vitals.HitPoints <= 0) return new(RuntimeManualSaveAdmissionKind.Refused, "player-defeated");
        if (BlockingExecutionError is { } failure)
            return new(RuntimeManualSaveAdmissionKind.Refused, $"native-driver-failure: {failure}");
        var sourceBoundary = sourceOrder is null ? RuntimeManualSaveSourceBoundary.Observe(_scripts.ScriptManualSaves) :
            sourceOrder.ObserveAdmission(_scripts.ScriptManualSaves,
                manual ?? throw new InvalidOperationException("Ordered save admission has no original manual receipt."), Godot.Engine.GetProcessFrames());
        if (sourceBoundary.Kind == RuntimeManualSaveAdmissionKind.Refused) return sourceBoundary;
        if (_scripts.References!.PlayerMoves.Pending) return new(RuntimeManualSaveAdmissionKind.Refused, "player-move");
        // A queued automatic snapshot is not an active writer. The complete
        // manual snapshot can fulfill it after ordinary capture/audio admission.
        if (_scripts.References.PendingHitEventCount != 0) return new(RuntimeManualSaveAdmissionKind.Refused, "reference-hit-event");
        if (StageResultsSaveBlocker is { } stage) return new(RuntimeManualSaveAdmissionKind.Refused, stage);
        var blocker = SaveContinuationBlocker;
        if (blocker is not null and not "actor-procedure-initialization")
            return new(RuntimeManualSaveAdmissionKind.Refused, blocker);
        if (_scripts.References.PendingAnimationSoundCaptureCount == 0)
            return blocker is null ? new(RuntimeManualSaveAdmissionKind.Ready) :
                new(RuntimeManualSaveAdmissionKind.Refused, blocker);
        if (_scripts.References.PendingAnimationSoundFiniteVoiceWait() is not { Count: > 0 } voices)
            return new(RuntimeManualSaveAdmissionKind.Refused, "animation-sound-continuation");
        if (blocker == "actor-procedure-initialization" &&
            (_scripts.References.PendingProcedureFiniteVoiceWait() is not { Count: > 0 } procedures ||
                procedures.Any(voice => !voices.Contains(voice))))
            return new(RuntimeManualSaveAdmissionKind.Refused, blocker);
        return new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: voices);
    }
}
