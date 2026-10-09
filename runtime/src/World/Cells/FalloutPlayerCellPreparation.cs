using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerCellPreparationHandoff(Guid PreviousProcess, Guid CurrentProcess,
    Guid PreviousPreparation, Guid CurrentPreparation);
internal sealed record FalloutPlayerCellPreparationSnapshot(string Schema, Guid Identity,
    FalloutPlayerRawTransferSource Source, string Stack, Guid CapturedProcess,
    FalloutPlayerPendingRequest Request, FalloutMainPlayerSourceCell Cell,
    FalloutPlayerCellPreparationHandoff? Handoff);

// Preload projects already captured value cells. It neither enters the source
// Main child nor supplies a worldspace conversion, callback or null store.
internal sealed class FalloutPlayerCellPreparation : IDisposable
{
    internal const string Schema = "opennv-Player-interior-CELL-preparation/v1";
    private readonly FalloutPlayerPendingSlot _slot;
    private readonly FalloutPlayerCellPreparationSnapshot _value;
    private bool _retired;
    internal FalloutPlayerPendingRequest Request => _value.Request;
    internal FalloutPlayerTransferPayload Payload => Request.SourcePayload!;
    internal FalloutMainPlayerSourceCell SourceCell { get { RequireLive(); return _value.Cell; } }
    internal FalloutReferencePlacement Placement
    {
        get
        {
            RequireLive(); _slot.RequireSourcePreparation(Request);
            return new(_value.Cell.Source.Cell,
                [BitConverter.UInt32BitsToSingle(Payload.PositionX), BitConverter.UInt32BitsToSingle(Payload.PositionY),
                    BitConverter.UInt32BitsToSingle(Payload.PositionZ)],
                [BitConverter.UInt32BitsToSingle(Payload.RotationX), BitConverter.UInt32BitsToSingle(Payload.RotationY),
                    BitConverter.UInt32BitsToSingle(Payload.RotationZ)]);
        }
    }

    private FalloutPlayerCellPreparation(FalloutPlayerPendingSlot slot, FalloutPlayerCellPreparationSnapshot value)
    { _slot = slot; _value = value; }

    internal static FalloutPlayerCellPreparation Create(FalloutPlayerRawTransferSource source, string stack,
        Guid process, FalloutPlayerPendingSlot slot, FalloutPlayerPendingRequest request, FalloutMainPlayerSourceCell cell)
    {
        ArgumentNullException.ThrowIfNull(slot); ArgumentNullException.ThrowIfNull(request);
        slot.RequireSourcePreparation(request);
        var value = new FalloutPlayerCellPreparationSnapshot(Schema, Guid.NewGuid(), source, stack, process, request, cell, null);
        Validate(value); return new(slot, value);
    }

    internal void RequireCurrent(FalloutPlayerRawTransferSource source, string stack, Guid process,
        FalloutPlayerPendingSlot slot, FalloutMainPlayerSourceCell cell)
    {
        RequireLive(); ArgumentNullException.ThrowIfNull(slot); slot.RequireSourcePreparation(Request);
        if (!ReferenceEquals(_slot, slot) || source != _value.Source || stack != _value.Stack ||
            process != _value.CapturedProcess || cell != _value.Cell)
            throw new InvalidDataException("Interior preload changed its actual pending/source/CELL/process owner.");
    }

    internal FalloutPlayerCellPreparationSnapshot Capture()
    {
        RequireLive(); _slot.RequireSourcePreparation(Request);
        Validate(_value); return _value;
    }

    internal static FalloutPlayerCellPreparation Restore(FalloutPlayerCellPreparationSnapshot saved,
        FalloutPlayerRawTransferSource source, string stack, Guid process, FalloutPlayerPendingSlot slot,
        FalloutMainPlayerSourceCell cell)
    {
        Validate(saved); ArgumentNullException.ThrowIfNull(slot);
        var request = slot.Next ?? throw new InvalidDataException("Cold interior preload lost its actual still-pending allocation.");
        slot.RequireSourcePreparation(request);
        if (source != saved.Source || stack != saved.Stack || process == Guid.Empty || process == saved.CapturedProcess ||
            cell != saved.Cell || !SameRequest(saved.Request, request))
            throw new InvalidDataException("Interior preload cold continuation changed source, captured values or actual process epoch.");
        var identity = Guid.NewGuid();
        var current = saved with
        {
            Identity = identity,
            CapturedProcess = process,
            Request = request,
            Handoff = new(saved.CapturedProcess, process, saved.Identity, identity)
        };
        Validate(current); return new(slot, current);
    }

    // The return is supplied only by the living shared Main owner. A native
    // preload return or a bare PlayerMoves.Complete is insufficient evidence.
    internal void RequireReturned(FalloutMainPlayerCellSource source, string stack, Guid process,
        FalloutMainPlayerCellCall? returned, FalloutPlayerPendingSlot slot)
    {
        RequireLive(); source.Validate();
        slot.RequireSourcePreparationCompleted(Request);
        if (!ReferenceEquals(_slot, slot) || source != _value.Source.Pending.Player || stack != _value.Stack ||
            process != _value.CapturedProcess || returned is not { Disposition: FalloutMainPlayerCellDisposition.PendingReturned } call ||
            call.Process != _value.CapturedProcess || call.Pending != Request.Identity || call.PendingKind != Request.Kind ||
            call.Error is not null || call.FailureType is not null ||
            call.Children.SingleOrDefault(child => child.Step == FalloutMainPlayerCellStep.PendingDestination) is not { Returned: not null, Boolean: true } ||
            call.Children.SingleOrDefault(child => child.Step == FalloutMainPlayerCellStep.PendingNullStore) is not { Returned: not null })
            throw new InvalidDataException("Initial placement has not reached its actual shared Main pending return/null store.");
    }

    internal static void Validate(FalloutPlayerCellPreparationSnapshot value)
    {
        if (value is null || value.Schema != Schema || value.Identity == Guid.Empty || value.Source is null ||
            string.IsNullOrWhiteSpace(value.Stack) || value.CapturedProcess == Guid.Empty || value.Request is null || value.Cell is null)
            throw new InvalidDataException("Interior preload omitted its mandatory current source/value identity.");
        value.Source.Validate();
        FalloutPlayerPendingSlot.Validate(new(value.Request.Revision, value.Request, null, null, null, null));
        var request = value.Request;
        if (request.Kind != FalloutPlayerPendingKind.MoveTo || request.Move is not { } move || request.SourcePayload is not { } payload)
            throw new NotSupportedException("Initial CELL preparation requires an actual captured MoveTo allocation.");
        FalloutPlayerRawTransferFactory.Require(payload, value.Source);
        if (payload.Target != FalloutPlayerTransferTarget.Cell)
            throw new NotSupportedException("Initial worldspace/reference preparation requires its actual Main conversion/consumer.");
        var receipt = payload.FactoryReceipt!;
        if (receipt.Writer != FalloutPlayerRawTransferWriter.MoveTo || receipt.RequestSource != move.Source || receipt.Target != move.Destination ||
            receipt.Offsets != FalloutPlayerTransferVector.Read([move.X, move.Y, move.Z]) ||
            value.Cell.Source != receipt.ParentCell || value.Cell.CellFlags != receipt.ParentCellFlags ||
            (value.Cell.CellFlags & 1) == 0 || payload.Cell != value.Cell.Source.Cell)
            throw new InvalidDataException("Interior preload no longer projects the exact writer/parent CELL allocation.");
        FalloutPlayerTransferVector.Read([BitConverter.UInt32BitsToSingle(payload.PositionX),
            BitConverter.UInt32BitsToSingle(payload.PositionY), BitConverter.UInt32BitsToSingle(payload.PositionZ)]);
        FalloutPlayerTransferVector.Read([BitConverter.UInt32BitsToSingle(payload.RotationX),
            BitConverter.UInt32BitsToSingle(payload.RotationY), BitConverter.UInt32BitsToSingle(payload.RotationZ)]);
        if (value.Handoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != value.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.PreviousPreparation == Guid.Empty ||
            cold.CurrentPreparation != value.Identity || cold.PreviousPreparation == cold.CurrentPreparation))
            throw new InvalidDataException("Interior preload lost its genuine new-process value handoff.");
    }

    private static bool SameRequest(FalloutPlayerPendingRequest before, FalloutPlayerPendingRequest after) =>
        before == after && before.Move is { } first && after.Move is { } second &&
        FalloutPlayerTransferVector.Read([first.X, first.Y, first.Z]) == FalloutPlayerTransferVector.Read([second.X, second.Y, second.Z]);
    private void RequireLive() => ObjectDisposedException.ThrowIf(_retired, this);
    public void Dispose() => _retired = true;
}
