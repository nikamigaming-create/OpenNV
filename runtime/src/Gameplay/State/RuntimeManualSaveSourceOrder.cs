using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// The paused preparation leases one queued player request. Every earlier
// source/native writer keeps its own order, destination and capture admission.
internal sealed class RuntimeManualSaveSourceOrder : IDisposable
{
    private readonly FalloutScriptManualSaveRequests _source;
    private readonly RuntimeManualSaveReceipt _manual;
    private readonly ulong _startedPhase;
    private readonly Guid _epoch;
    private ulong? _drainingOrder;
    private bool _disposed;
    internal bool Draining => _drainingOrder is not null && !_disposed;
    internal FalloutScriptManualSaveReceipt? Receipt => _source.Receipt;

    internal RuntimeManualSaveSourceOrder(FalloutScriptManualSaveRequests source, RuntimeManualSaveReceipt manual, ulong phase)
    {
        _source = source; _manual = manual; _startedPhase = phase; _epoch = source.Order.Epoch;
        Validate(source, manual, phase);
        if (source.PreparationBlocker(manual.Order) is { } blocker)
            throw new NotSupportedException("Ordered save preparation is deferred: " + blocker);
        source.RequireCapture();
    }

    internal RuntimeManualSaveAdmission ObserveAdmission(FalloutScriptManualSaveRequests source,
        RuntimeManualSaveReceipt manual, ulong phase)
    {
        Validate(source, manual, phase);
        return new(RuntimeManualSaveAdmissionKind.Ready);
    }

    internal void Validate(FalloutScriptManualSaveRequests source, RuntimeManualSaveReceipt manual, ulong phase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var actual = source.Order.Find(_manual.Order);
        if (!ReferenceEquals(source, _source) || source.Order.Epoch != _epoch || manual.Order != _manual.Order ||
            manual.Generation != _manual.Generation || manual.Slot != _manual.Slot || manual.Session != _manual.Session ||
            manual.SourceCompatibilityId != _manual.SourceCompatibilityId || manual.Origin != _manual.Origin || manual.Disposition != "pending" ||
            phase < _startedPhase || source.ObservedEnginePhase != phase || source.EnteredInvocations != 0 ||
            actual.Request != manual.Slot || actual.Disposition != RuntimeSaveRequestDisposition.Pending ||
            actual.Origin != (manual.Origin == RuntimeManualSaveOrigin.PlayerInput ? RuntimeSaveRequestOrigin.PlayerInput : RuntimeSaveRequestOrigin.SessionMenu) ||
            source.WritingRequestedSlot && source.Order.Writing!.Order != _drainingOrder ||
            source.PreparationBlocker(manual.Order) is not null)
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Ordered preparation lost its exact queued request, invocation, epoch or native phase.");
        source.RequireNoFailure();
    }

    internal bool PermitsOriginalSourceDrain(FalloutScriptManualSaveRequests source,
        RuntimeManualSaveReceipt manual, ulong phase)
    {
        if (!Draining || phase <= _startedPhase || phase <= _manual.RequestedPhase ||
            source.Order.Head is not { } head || head.Order != _drainingOrder || head.Order >= _manual.Order ||
            head.Origin is RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu ||
            phase <= (head.HandoffPhase ?? head.RequestedPhase)) return false;
        Validate(source, manual, phase); return true;
    }

    internal void DrainBeforeManual(RuntimeManualSaveReceipt manual, ulong phase, Func<string?> originalBlocker)
    {
        Validate(_source, manual, phase);
        if (phase <= _startedPhase || phase <= _manual.RequestedPhase)
            throw new NotSupportedException("Ordered save cannot write before its actual later preparation phase.");
        while (_source.Order.Head is { } head && head.Order < manual.Order)
        {
            if (head.Origin is RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu)
                throw new NotSupportedException("An earlier player request still owns its separate preparation.");
            _drainingOrder = head.Order;
            try
            {
                if (!_source.Drain(originalBlocker))
                    throw new NotSupportedException(_source.Error is { } error ? "Earlier save failed: " + error :
                        "Earlier save is deferred: " + (_source.DeferredBy ?? "requesting invocation"));
                var committed = _source.Order.Find(head.Order);
                if (committed.Disposition != RuntimeSaveRequestDisposition.Completed || committed.Request != head.Request ||
                    committed.DestinationPath != head.DestinationPath || committed.Script != head.Script || committed.Native != head.Native)
                    throw new InvalidDataException("Earlier writer changed its exact request origin/destination.");
            }
            finally { _drainingOrder = null; }
            Validate(_source, manual, phase);
        }
        if (_source.Order.Head?.Order != manual.Order)
            throw new NotSupportedException("Player save does not own the next persistent writer order.");
    }

    internal RuntimeSaveSlotMetadata WriteManual(RuntimeManualSaveReceipt manual, ulong phase, Func<Guid, RuntimeSaveSlotMetadata> writer)
    {
        Validate(_source, manual, phase);
        _drainingOrder = manual.Order;
        try { return _source.Order.Write(manual.Order, request => writer(request.Request)); }
        finally { _drainingOrder = null; }
    }

    public void Dispose() { _disposed = true; _drainingOrder = null; }
}
