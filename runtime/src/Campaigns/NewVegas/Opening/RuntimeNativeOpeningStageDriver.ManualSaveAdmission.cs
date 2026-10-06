using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal RuntimeManualSaveAdmission ObserveManualSaveAdmission()
    {
        if (Vitals.HitPoints <= 0) return new(RuntimeManualSaveAdmissionKind.Refused, "player-defeated");
        if (_scripts.ScriptManualSaves.Pending || _scripts.ScriptManualSaves.EnteredInvocations != 0 ||
            _scripts.ScriptManualSaves.Error is not null)
            return new(RuntimeManualSaveAdmissionKind.Refused, "source-manual-save");
        if (_scripts.References!.PlayerMoves.Pending) return new(RuntimeManualSaveAdmissionKind.Refused, "player-move");
        if (_saveRequested) return new(RuntimeManualSaveAdmissionKind.Refused, "concurrent-auto-save");
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
