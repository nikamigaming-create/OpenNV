using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum RuntimeManualSaveAdmissionKind { Ready, FiniteSourceAudio, Refused }
internal sealed record RuntimeManualSaveAdmission(RuntimeManualSaveAdmissionKind Kind, string? Blocker = null,
    IReadOnlyList<FalloutFiniteSoundVoice>? Voices = null)
{
    internal void Validate()
    {
        if (Kind == RuntimeManualSaveAdmissionKind.Ready && (Blocker is not null || Voices is not null) ||
            Kind == RuntimeManualSaveAdmissionKind.Refused && (string.IsNullOrWhiteSpace(Blocker) || Voices is not null) ||
            Kind == RuntimeManualSaveAdmissionKind.FiniteSourceAudio &&
            (Blocker is not null || Voices is not { Count: > 0 }) || !Enum.IsDefined(Kind))
            throw new InvalidDataException("Manual save admission has no unique authoritative boundary.");
        if (Voices is { } voices)
        {
            foreach (var voice in voices) (voice ?? throw new InvalidDataException("Missing pending voice.")).Validate();
            if (voices.DistinctBy(voice => (voice.Reference, voice.Generation)).Count() != voices.Count)
                throw new InvalidDataException("Manual save wait repeats a source generation.");
        }
    }
}

internal sealed record RuntimeManualSaveReceipt(ulong Generation, Guid Slot, Guid Session,
    string SourceCompatibilityId, ulong RequestedPhase, int RequestCount, string Disposition,
    IReadOnlyList<FalloutFiniteSoundVoice>? DeferredVoices = null, string? Error = null,
    RuntimeSaveSlotMetadata? CommittedSlot = null,
    IReadOnlyList<FalloutFiniteSoundVoice>? AwaitedVoices = null,
    RuntimeManualSaveOrigin Origin = RuntimeManualSaveOrigin.PlayerInput,
    RuntimeManualSavePreparationReceipt? Preparation = null, string? CleanupError = null,
    FalloutScriptManualSaveReceipt? OrderedSourceSave = null);

// Player/manual requests wait for a genuine later, fully captureable phase.
// Source ForceSave retains its separate invocation/cursor and ordering owner.
internal sealed class RuntimeManualSaveRequests
{
    private readonly List<RuntimeManualSaveReceipt> _history = [];
    private ulong _generation;
    private bool _writing;
    private RuntimeManualSavePreparation? _preparation;
    internal RuntimeManualSaveReceipt? Receipt => _history.LastOrDefault();
    internal IReadOnlyList<RuntimeManualSaveReceipt> History => _history.AsReadOnly();
    internal bool Pending => Receipt?.Disposition == "pending";

    internal RuntimeManualSaveReceipt Request(Guid session, string sourceCompatibilityId, ulong phase,
        RuntimeManualSaveOrigin origin = RuntimeManualSaveOrigin.PlayerInput)
    {
        if (_writing || session == Guid.Empty || string.IsNullOrWhiteSpace(sourceCompatibilityId) || !Enum.IsDefined(origin))
            throw new InvalidOperationException("Manual save has no settled engine session/source owner.");
        if (Pending)
        {
            var current = Receipt!;
            if (current.Session != session || current.SourceCompatibilityId != sourceCompatibilityId || phase < current.RequestedPhase)
                throw new InvalidOperationException("Pending manual save belongs to a different engine session or phase.");
            Replace(current with { RequestCount = checked(current.RequestCount + 1) });
        }
        else
        {
            _preparation = null;
            _history.Add(new(checked(++_generation), Guid.NewGuid(), session, sourceCompatibilityId, phase, 1, "pending", Origin: origin));
        }
        return Receipt!;
    }

    internal void Prepare(ulong phase, ulong milliseconds, RuntimeManualSaveAdmission admission,
        IReadOnlyList<FalloutFiniteSoundVoice> voices,
        ulong maximumWaitMilliseconds = RuntimeManualSavePreparation.MaximumWaitMilliseconds)
    {
        if (!Pending || _writing || _preparation is not null || phase < Receipt!.RequestedPhase)
            throw new InvalidOperationException("Manual save cannot acquire another or regressed preparation lease.");
        _preparation = new(phase, milliseconds, admission, voices, maximumWaitMilliseconds);
        Replace(Receipt! with
        {
            Preparation = _preparation.Receipt,
            DeferredVoices = voices.Count == 0 ? null : _preparation.Receipt.Voices,
            AwaitedVoices = _preparation.Receipt.Voices
        });
    }

    internal bool DrainPrepared(Guid session, string sourceCompatibilityId, ulong phase, ulong milliseconds,
        Func<IReadOnlyList<FalloutFiniteSoundVoice>> nativePending,
        Func<RuntimeManualSaveAdmission> admission, Func<Guid, RuntimeSaveSlotMetadata> writer) =>
        Drain(session, sourceCompatibilityId, phase, () =>
        {
            var preparation = _preparation ?? throw new InvalidOperationException("Manual save has no native preparation lease.");
            var state = preparation.Observe(phase, milliseconds, nativePending(), admission());
            Replace(Receipt! with { Preparation = preparation.Receipt });
            return state;
        }, writer);

    internal bool Drain(Guid session, string sourceCompatibilityId, ulong phase,
        Func<RuntimeManualSaveAdmission> admission, Func<Guid, RuntimeSaveSlotMetadata> writer)
    {
        if (_writing) throw new InvalidOperationException("Manual slot writer is already executing.");
        if (!Pending) return false;
        var current = Receipt!;
        if (current.Session != session || current.SourceCompatibilityId != sourceCompatibilityId)
        { Cancel("Engine session or selected source stack changed before manual save committed."); return false; }
        if (phase <= current.RequestedPhase) return false;
        try
        {
            var state = admission(); state.Validate();
            current = Receipt!;
            if (state.Kind == RuntimeManualSaveAdmissionKind.Refused)
            { Fail(state.Blocker!); return false; }
            if (state.Kind == RuntimeManualSaveAdmissionKind.FiniteSourceAudio)
            {
                var voices = Array.AsReadOnly(state.Voices!.ToArray());
                if (current.DeferredVoices?.SequenceEqual(voices) == true) return false;
                var awaited = (current.AwaitedVoices ?? []).Concat(voices)
                    .DistinctBy(voice => (voice.NativeOwner, voice.Reference, voice.Generation)).ToArray();
                Replace(current with { DeferredVoices = voices, AwaitedVoices = Array.AsReadOnly(awaited) });
                return false;
            }
            _writing = true;
            var slot = writer(current.Slot);
            current = Receipt!;
            if (slot.Id != current.Slot.ToString("N") || string.IsNullOrWhiteSpace(slot.Path) || !File.Exists(slot.Path))
                throw new InvalidDataException("Manual writer returned no matching committed slot.");
            Replace(current with
            {
                Disposition = "completed",
                DeferredVoices = null,
                CommittedSlot = slot,
                Preparation = current.Preparation is { } preparation
                    ? preparation with { Phase = RuntimeManualSavePreparationPhase.Completed } : null
            });
            return true;
        }
        catch (FalloutFiniteSoundSaveDrainInvalidatedException error)
        {
            _writing = false;
            Cancel(error.Message);
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException or System.Text.Json.JsonException)
        {
            ReplaceFailed(error.Message);
            return false;
        }
        finally { _writing = false; }
    }

    internal void Cancel(string reason)
    {
        if (_writing) throw new InvalidOperationException("Cannot cancel a writing manual slot.");
        if (!Pending) return;
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Cancellation needs its actual boundary.", nameof(reason));
        Replace(Receipt! with
        {
            Disposition = "cancelled",
            Error = reason,
            DeferredVoices = null,
            Preparation = Receipt!.Preparation is { } preparation
                ? preparation with { Phase = RuntimeManualSavePreparationPhase.Cancelled } : null
        });
    }

    internal void Fail(string reason)
    {
        if (_writing) throw new InvalidOperationException("Cannot fail a writing manual slot.");
        if (!Pending) throw new InvalidOperationException("Manual save failure has no pending request.");
        ReplaceFailed(reason);
    }

    private void ReplaceFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Save failure needs its actual boundary.", nameof(reason));
        Replace(Receipt! with
        {
            Disposition = "failed",
            Error = reason,
            DeferredVoices = null,
            Preparation = Receipt!.Preparation is { } preparation
                ? preparation with { Phase = RuntimeManualSavePreparationPhase.Failed } : null
        });
    }

    internal void ReportCleanupFailure(string reason)
    {
        var receipt = Receipt;
        if (string.IsNullOrWhiteSpace(reason) || receipt is null)
            throw new InvalidOperationException("Save cleanup failure has no request or actual cause.");
        Replace(receipt with { CleanupError = reason });
    }

    internal void ObserveOrderedSourceSave(FalloutScriptManualSaveReceipt? source)
    {
        if (!Pending) throw new InvalidOperationException("Ordered source observation has no pending manual request.");
        Replace(Receipt! with { OrderedSourceSave = source });
    }

    private void Replace(RuntimeManualSaveReceipt receipt) => _history[^1] = receipt;
}
