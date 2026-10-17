using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Only one pre-existing, fully retired source request may precede this manual
// slot. Its own Drain, source validation, self-capture and writer remain owners.
internal sealed class RuntimeManualSaveSourceOrder : IDisposable
{
    private readonly FalloutScriptManualSaveRequests _source;
    private readonly RuntimeManualSaveReceipt _manual;
    private readonly ulong _startedPhase;
    private readonly FalloutScriptManualSaveReceipt? _original;
    private FalloutScriptManualSaveReceipt? _expected;
    private bool _draining, _disposed;
    internal bool Draining => _draining && !_disposed;
    internal FalloutScriptManualSaveReceipt? Receipt => _expected;

    internal RuntimeManualSaveSourceOrder(FalloutScriptManualSaveRequests source, RuntimeManualSaveReceipt manual, ulong phase)
    {
        _source = source; _manual = manual; _startedPhase = phase;
        source.RequireNoFailure();
        if (source.WritingRequestedSlot || source.EnteredInvocations != 0 || source.ObservedEnginePhase != phase)
            throw new NotSupportedException("Source ForceSave has no matching quiescent engine phase for ordered preparation.");
        _original = Copy(source.Receipt); _expected = _original;
        if (source.Pending && (_original is not { Sites.Count: > 0, Invocations.Count: > 0 } ||
            _original.RequestedPhase > phase || _original.Invocations!.Any(invocation => !invocation.Ended)))
            throw new NotSupportedException("Ordered ForceSave requires every original requesting invocation genuinely ended.");
        if (!source.Pending) source.RequireCapture();
    }

    internal RuntimeManualSaveAdmission ObserveAdmission(FalloutScriptManualSaveRequests source,
        RuntimeManualSaveReceipt manual, ulong phase)
    {
        Validate(source, manual, phase);
        if (_source.Pending) return new(RuntimeManualSaveAdmissionKind.Ready);
        return RuntimeManualSaveSourceBoundary.Observe(source);
    }

    internal void Validate(FalloutScriptManualSaveRequests source, RuntimeManualSaveReceipt manual, ulong phase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(source, _source) || manual.Generation != _manual.Generation || manual.Slot != _manual.Slot ||
            manual.Session != _manual.Session || manual.SourceCompatibilityId != _manual.SourceCompatibilityId ||
            manual.Origin != _manual.Origin || manual.Disposition != "pending" || phase < _startedPhase ||
            source.ObservedEnginePhase != phase || source.EnteredInvocations != 0 ||
            source.WritingRequestedSlot && !_draining || !SameReceipt(source.Receipt, _expected))
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Ordered source/manual request, invocation, generation or actual engine phase changed.");
        source.RequireNoFailure();
    }

    internal bool PermitsOriginalSourceDrain(FalloutScriptManualSaveRequests source,
        RuntimeManualSaveReceipt manual, ulong phase)
    {
        var original = _original;
        if (!Draining || original is not { Disposition: "pending" } || phase <= _startedPhase ||
            phase <= _manual.RequestedPhase || phase <= original.RequestedPhase) return false;
        Validate(source, manual, phase);
        return _source.Pending && _source.Receipt!.Slot == original.Slot;
    }

    internal void DrainBeforeManual(RuntimeManualSaveReceipt manual, ulong phase, Func<string?> originalBlocker)
    {
        Validate(_source, manual, phase);
        if (!_source.Pending) return;
        var original = _original;
        if (original is not { Disposition: "pending" } || phase <= _startedPhase ||
            phase <= _manual.RequestedPhase || phase <= original.RequestedPhase)
            throw new NotSupportedException("Ordered ForceSave cannot write before its real later source/manual preparation phase.");
        _draining = true;
        try
        {
            if (!_source.Drain(originalBlocker))
                throw new NotSupportedException(_source.Error is { } error
                    ? "Original ForceSave writer failed: " + error
                    : "Original ForceSave remains deferred: " + (_source.DeferredBy ?? "missing source completion"));
            var completed = _source.Receipt!;
            if (completed.Disposition != "completed" || string.IsNullOrWhiteSpace(completed.SlotPath) ||
                !File.Exists(completed.SlotPath) ||
                !SameReceipt(completed with { Disposition = "pending", SlotPath = null }, original))
                throw new InvalidDataException("Original ForceSave lost its original GUID, hashes, sites, invocation history or committed slot.");
            _expected = Copy(completed);
        }
        finally
        {
            _draining = false;
            // Preserve the owner's real failed receipt too; never reset/retry it.
            if (_source.Error is not null) _expected = Copy(_source.Receipt);
        }
    }

    private static FalloutScriptManualSaveReceipt? Copy(FalloutScriptManualSaveReceipt? receipt) => receipt is null ? null :
        receipt with
        {
            Sites = Array.AsReadOnly(receipt.Sites.ToArray()),
            Invocations = receipt.Invocations is null ? null : Array.AsReadOnly(receipt.Invocations.ToArray())
        };

    private static bool SameReceipt(FalloutScriptManualSaveReceipt? left, FalloutScriptManualSaveReceipt? right) =>
        left is null ? right is null : right is not null &&
        left.Generation == right.Generation && left.Slot == right.Slot && left.RequestedPhase == right.RequestedPhase &&
        left.Disposition == right.Disposition && left.Error == right.Error && left.SlotPath == right.SlotPath &&
        left.Sites.SequenceEqual(right.Sites) && (left.Invocations ?? []).SequenceEqual(right.Invocations ?? []);

    public void Dispose() { _disposed = true; _draining = false; }
}
