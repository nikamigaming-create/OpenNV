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
    FalloutScriptManualSaveReceipt? OrderedSourceSave = null, ulong Order = 0,
    IReadOnlyList<RuntimeSaveRequest>? OrderedRequests = null, string? DeferredBy = null);

// Player requests retain their individual order in the same persistent queue
// as source and native requests. A preparation owns one exact request only.
internal sealed class RuntimeManualSaveRequests
{
    private readonly List<RuntimeManualSaveReceipt> _history = [];
    private FalloutScriptManualSaveRequests? _source;
    private Guid _session;
    private string? _identity;
    private int _active = -1;
    private bool _writing;
    private RuntimeManualSavePreparation? _preparation;
    internal RuntimeManualSaveReceipt? Receipt => _active >= 0 ? _history[_active] : _history.LastOrDefault();
    internal IReadOnlyList<RuntimeManualSaveReceipt> History => _history.AsReadOnly();
    internal bool Pending => Receipt?.Disposition == "pending";

    internal RuntimeManualSaveReceipt Find(ulong order) => _history.Single(row => row.Order == order);
    internal void ObserveQueueDeferral(string reason)
    {
        if (!Pending || _preparation is not null || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Queue deferral lacks its unprepared request.");
        Replace(Receipt! with { DeferredBy = reason });
    }

    internal bool Queued => _history.Any(row => row.Disposition == "pending");
    internal FalloutScriptManualSaveRequests Source => _source ?? throw new InvalidOperationException("Player saves have no joined source queue.");

    internal void Bind(FalloutScriptManualSaveRequests source, Guid session, string sourceCompatibilityId)
    {
        if (ReferenceEquals(_source, source))
        {
            if (_session != session || _identity != sourceCompatibilityId || source.Order.SourceCompatibilityId != sourceCompatibilityId)
                throw new InvalidDataException("Player-save binding changed its actual session or source selection.");
            return;
        }
        if (_source is not null || _history.Count != 0 || session == Guid.Empty)
            throw new InvalidOperationException("Player-save queue cannot switch source/session owners.");
        if (source.Order.SourceCompatibilityId != sourceCompatibilityId) throw new InvalidDataException("Player/source save selections differ.");
        _source = source; _session = session; _identity = sourceCompatibilityId;
        foreach (var row in source.Order.Requests.Where(row => row.Origin is RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu))
            _history.Add(new(row.Order, row.Request, session, sourceCompatibilityId, row.HandoffPhase ?? row.RequestedPhase,
                1, row.Disposition.ToString().ToLowerInvariant(), Error: row.Error, CommittedSlot: row.Committed,
                Origin: row.Origin == RuntimeSaveRequestOrigin.SessionMenu ? RuntimeManualSaveOrigin.SessionMenu : RuntimeManualSaveOrigin.PlayerInput,
                Order: row.Order));
        ActivateNext();
    }

    internal RuntimeManualSaveReceipt Request(Guid session, string sourceCompatibilityId, ulong phase,
        RuntimeManualSaveOrigin origin = RuntimeManualSaveOrigin.PlayerInput, RuntimeSaveNativeSite? site = null)
    {
        if (_writing || session == Guid.Empty || string.IsNullOrWhiteSpace(sourceCompatibilityId) || !Enum.IsDefined(origin) ||
            site is null || site.Session != session || session != _session || sourceCompatibilityId != _identity || Source.ObservedEnginePhase != phase)
            throw new InvalidOperationException("Manual save has no actual engine/input/session/source identity.");
        var request = Source.RequestNative(origin == RuntimeManualSaveOrigin.PlayerInput ? RuntimeSaveRequestOrigin.PlayerInput : RuntimeSaveRequestOrigin.SessionMenu, site);
        var receipt = new RuntimeManualSaveReceipt(request.Order, request.Request, session, sourceCompatibilityId, phase, 1, "pending", Origin: origin, Order: request.Order);
        _history.Add(receipt);
        if (_active < 0 || !Pending && _preparation is null) ActivateNext();
        return receipt;
    }

    private void ActivateNext()
    {
        _active = _history.FindIndex(row => row.Disposition == "pending");
        _preparation = null;
    }

    internal void RetirePreparation()
    {
        if (_writing || Pending) throw new InvalidOperationException("Pending/writing player save preparation cannot retire.");
        ActivateNext();
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
            DeferredBy = null,
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
            var committedRequest = Source.Order.Find(current.Order);
            if (committedRequest.Disposition != RuntimeSaveRequestDisposition.Completed || committedRequest.Committed != slot)
                throw new InvalidDataException("Manual writer bypassed the actual common head-order commitment.");
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
        Source.Order.Cancel(Receipt!.Order, reason);
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

    internal void CancelQueued(string reason)
    {
        if (_writing || _preparation is not null || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Queued player cancellation lacks its retired preparation and actual boundary.");
        foreach (var current in _history.Where(row => row.Disposition == "pending").ToArray())
        {
            Source.Order.Cancel(current.Order, reason);
            _history[_history.FindIndex(row => row.Order == current.Order)] = current with { Disposition = "cancelled", Error = reason, DeferredBy = null };
        }
        ActivateNext();
    }

    private void ReplaceFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Save failure needs its actual boundary.", nameof(reason));
        var request = Source.Order.Find(Receipt!.Order);
        if (request.Disposition == RuntimeSaveRequestDisposition.Completed)
        {
            Replace(Receipt! with
            {
                Disposition = "completed",
                CommittedSlot = request.Committed,
                CleanupError = "The destination committed before later feedback failed: " + reason,
                DeferredVoices = null
            });
            return;
        }
        if (request.Disposition == RuntimeSaveRequestDisposition.Pending) Source.Order.Fail(request.Order, reason);
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

    internal void ObserveOrderedRequests()
    {
        if (!Pending) throw new InvalidOperationException("Ordered receipt publication lacks its current player request.");
        Replace(Receipt! with { OrderedRequests = Source.Order.Requests.Where(row => row.Order < Receipt!.Order).ToArray() });
    }

    private void Replace(RuntimeManualSaveReceipt receipt)
    {
        var index = _history.FindIndex(row => row.Order == receipt.Order);
        if (index < 0 || index != _active) throw new InvalidOperationException("Player-save mutation differs from its active exact order.");
        _history[index] = receipt;
    }
}
