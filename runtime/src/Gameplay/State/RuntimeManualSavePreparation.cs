using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum RuntimeManualSaveOrigin { PlayerInput, SessionMenu }
internal enum RuntimeManualSavePreparationPhase { DrainingFiniteNativeAudio, Capturing, Completed, Failed, Cancelled }
internal sealed record RuntimeManualSavePreparationReceipt(ulong StartedPhase, ulong StartedMilliseconds,
    ulong MaximumWaitMilliseconds, RuntimeManualSavePreparationPhase Phase,
    IReadOnlyList<FalloutFiniteSoundVoice> Voices, IReadOnlyList<FalloutFiniteSoundVoice> RemainingVoices);

internal static class RuntimeManualSaveSourceBoundary
{
    internal static RuntimeManualSaveAdmission Observe(FalloutScriptManualSaveRequests requests, bool autoSaveRequested = false)
    {
        if (autoSaveRequested) throw new InvalidDataException("A Boolean cannot authorize an AutoSave request; the actual source lease must enqueue it.");
        if (requests.WritingRequestedSlot) return new(RuntimeManualSaveAdmissionKind.Refused, "ordered-save-writing");
        try { requests.RequireCapture(); }
        catch (NotSupportedException error) { return new(RuntimeManualSaveAdmissionKind.Refused, "source-save: " + error.Message); }
        return new(RuntimeManualSaveAdmissionKind.Ready);
    }
}

internal sealed class RuntimeManualSavePreparation
{
    internal const ulong MaximumWaitMilliseconds = 60000;
    private ulong _lastPhase, _lastMilliseconds;
    internal RuntimeManualSavePreparationReceipt Receipt { get; private set; }

    internal RuntimeManualSavePreparation(ulong phase, ulong milliseconds, RuntimeManualSaveAdmission admission,
        IReadOnlyList<FalloutFiniteSoundVoice> preparedVoices, ulong maximumWaitMilliseconds = MaximumWaitMilliseconds)
    {
        admission.Validate();
        if (maximumWaitMilliseconds == 0 || maximumWaitMilliseconds > MaximumWaitMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(maximumWaitMilliseconds));
        if (admission.Kind == RuntimeManualSaveAdmissionKind.Refused)
            throw new NotSupportedException(admission.Blocker);
        ValidateVoices(preparedVoices);
        if (!SameVoices(preparedVoices, admission.Voices ?? []))
            throw new NotSupportedException("Complete-save admission does not cover the exact prepared native finite set.");
        var voices = Array.AsReadOnly(preparedVoices.ToArray());
        Receipt = new(phase, milliseconds, maximumWaitMilliseconds,
            voices.Count == 0 ? RuntimeManualSavePreparationPhase.Capturing : RuntimeManualSavePreparationPhase.DrainingFiniteNativeAudio,
            voices, voices);
        _lastPhase = phase; _lastMilliseconds = milliseconds;
    }

    internal RuntimeManualSaveAdmission Observe(ulong phase, ulong milliseconds,
        IReadOnlyList<FalloutFiniteSoundVoice> nativePending, RuntimeManualSaveAdmission admission)
    {
        admission.Validate(); ValidateVoices(nativePending);
        if (phase < _lastPhase || milliseconds < _lastMilliseconds)
            throw new InvalidDataException("Save-preparation phase or monotonic clock regressed.");
        _lastPhase = phase; _lastMilliseconds = milliseconds;
        if (milliseconds - Receipt.StartedMilliseconds > Receipt.MaximumWaitMilliseconds)
            return new(RuntimeManualSaveAdmissionKind.Refused, "Finite native save preparation exceeded its bounded wait; no save was committed.");
        if (nativePending.Any(voice => !Receipt.RemainingVoices.Contains(voice)))
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Save preparation acquired a new, changed or resumed source/native generation.");
        Receipt = Receipt with
        {
            RemainingVoices = Array.AsReadOnly(nativePending.ToArray()),
            Phase = nativePending.Count == 0 ? RuntimeManualSavePreparationPhase.Capturing :
                RuntimeManualSavePreparationPhase.DrainingFiniteNativeAudio
        };
        if (admission.Kind == RuntimeManualSaveAdmissionKind.Refused) return admission;
        if (!SameVoices(nativePending, admission.Voices ?? []))
            throw new InvalidDataException("Save admission and genuine native Finished receipts disagree.");
        return admission;
    }

    private static bool SameVoices(IReadOnlyList<FalloutFiniteSoundVoice> left, IReadOnlyList<FalloutFiniteSoundVoice> right) =>
        left.Count == right.Count && left.All(right.Contains);
    private static void ValidateVoices(IReadOnlyList<FalloutFiniteSoundVoice> voices)
    {
        foreach (var voice in voices) (voice ?? throw new InvalidDataException("Save preparation has a missing voice.")).Validate();
        if (voices.DistinctBy(voice => (voice.Reference, voice.Generation)).Count() != voices.Count)
            throw new InvalidDataException("Save preparation repeats a source generation.");
    }
}

internal static class RuntimeManualSaveFeedback
{
    internal static string Describe(RuntimeManualSaveReceipt receipt)
    {
        var message = receipt.Disposition switch
        {
            "pending" when receipt.Preparation is null => "Save queued. " + (receipt.DeferredBy ?? "Waiting for its ordered preparation") + ". Gameplay continues until preparation owns the pause.",
            "pending" => receipt.Preparation is { RemainingVoices.Count: > 0 } preparation
                ? $"Saving... Waiting for {preparation.RemainingVoices.Count} original finite sound(s) to report native Finished. Gameplay is paused. Cancel leaves the previous Continue save intact."
                : "Saving... Preparing a complete checkpoint. Gameplay is paused; no new save exists yet.",
            "completed" when receipt.CommittedSlot is { } slot && slot.Id == receipt.Slot.ToString("N") && File.Exists(slot.Path) =>
                "New save committed. Earlier saves are retained.",
            "failed" => "Requested save failed; its slot was not committed: " + receipt.Error,
            "cancelled" => "Requested save cancelled; its slot was not committed: " + receipt.Error,
            _ => throw new InvalidDataException("Manual save has no verified visible disposition.")
        };
        if (receipt.OrderedRequests is { Count: > 0 } ordered)
            message += $" Earlier requests: {ordered.Count(row => row.Disposition == RuntimeSaveRequestDisposition.Completed)} committed, " +
                $"{ordered.Count(row => row.Disposition == RuntimeSaveRequestDisposition.Pending)} pending, " +
                $"{ordered.Count(row => row.Disposition == RuntimeSaveRequestDisposition.Failed)} failed.";
        return message + (receipt.CleanupError is null ? "" : " Cleanup failed: " + receipt.CleanupError);
    }
}
