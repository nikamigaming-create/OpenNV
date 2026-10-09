using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    internal FalloutCellProcessesSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new NotSupportedException("Actual CELL source/native operation is still in flight.");
        return new(Schema, _stack, _declaration.Contract, _process, _sequence,
            _cells.Values.Select(cell => cell with { Data = cell.Data is { } data ? data with { References = data.References.ToArray() } : null }).ToArray(),
            _transitions.ToArray(), _attachments.Values.Select(CopyAttachment).ToArray(),
            _cold is { } cold ? cold with { AwaitingNativeAttachments = cold.AwaitingNativeAttachments.ToArray(),
                PreviousAttachments = cold.PreviousAttachments.Select(CopyAttachment).ToArray() } : null);
    }
    private void Restore(FalloutCellProcessesSnapshot saved)
    {
        if (saved.Schema != Schema || saved.Stack != _stack || saved.Contract != _declaration.Contract ||
            saved.CapturedProcess == Guid.Empty || saved.CapturedProcess == _process || saved.Sequence is < 0 or long.MaxValue ||
            saved.Cells is null || saved.Transitions is null || saved.Attachments is null)
            throw new InvalidDataException("CELL continuation has an incomplete/foreign source or process identity.");
        var cells = new Dictionary<FalloutFormKey, FalloutCellProcessEntry>(FalloutFormKeyComparer.Instance);
        foreach (var cell in saved.Cells)
        {
            if (cell.Source != _source.ReadIdentity(cell.Source.Cell) || !Enum.IsDefined(cell.Phase) || cell.Epoch < 1 ||
                cell.Failure is not null && string.IsNullOrWhiteSpace(cell.Failure) || !cells.TryAdd(cell.Source.Cell, cell))
                throw new InvalidDataException("CELL continuation lost its exact winning source/phase/epoch.");
            if (cell.Data is { } data) _source.ValidateRetained(data);
            if (cell.Data is { } retained && retained.Source != cell.Source ||
                cell.Phase is FalloutCellProcessPhase.DataLoaded or FalloutCellProcessPhase.Detaching or FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached && cell.Data is null ||
                cell.Phase == FalloutCellProcessPhase.Constructed && cell.Data is not null ||
                cell.Phase is FalloutCellProcessPhase.LoadingData or FalloutCellProcessPhase.ReleasingData && cell.Failure is null)
                throw new InvalidDataException("CELL current phase discarded its real source data or incomplete operation.");
        }
        var attachments = new Dictionary<Guid, FalloutCellProcessAttachment>();
        var activeCells = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var actualObjects = new HashSet<ulong>();
        foreach (var attachment in saved.Attachments)
        {
            ValidateAttachment(attachment, saved.CapturedProcess, cells);
            if (!attachments.TryAdd(attachment.Identity, attachment))
                throw new InvalidDataException("CELL continuation duplicated a native attachment lease.");
            if (attachment.Retired) continue;
            foreach (var (cell, epoch) in attachment.CellEpochs)
                if (!activeCells.Add(cell) || cells[cell].Epoch != epoch || cells[cell].Phase is not
                    (FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached or FalloutCellProcessPhase.Detaching))
                    throw new InvalidDataException("CELL continuation crossed active native phase/epoch ownership.");
            foreach (var identity in attachment.Children.SelectMany(child => child.NativeObjects).Prepend(attachment.NativeRoot).Where(identity => identity != 0))
                if (!actualObjects.Add(identity)) throw new InvalidDataException("CELL continuation duplicated an actual native object owner.");
            if (attachment.RootPublished && (attachment.NativeRoot == 0 || attachment.Failure is not null ||
                attachment.CellEpochs.Keys.Any(cell => cells[cell].Phase != FalloutCellProcessPhase.Attached) ||
                attachment.Children.Any(child => child.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.Failed or FalloutCellProcessChildPhase.Retired)))
                throw new InvalidDataException("CELL continuation claims publication without complete actual child/phase owners.");
        }
        foreach (var cell in cells.Values)
            if (cell.Phase is FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached or FalloutCellProcessPhase.Detaching &&
                !activeCells.Contains(cell.Source.Cell))
                throw new InvalidDataException("CELL live phase has no native attachment source lease.");
        ValidateTransitions(saved, cells, attachments);
        if (saved.ColdHandoff is { } previous)
        {
            if (previous.PreviousProcess == Guid.Empty || previous.CurrentProcess != saved.CapturedProcess ||
                previous.PreviousProcess == previous.CurrentProcess || previous.Sequence <= 0 || previous.Sequence > saved.Sequence ||
                previous.AwaitingNativeAttachments is null || previous.PreviousAttachments is null ||
                previous.AwaitingNativeAttachments.Distinct().Count() != previous.AwaitingNativeAttachments.Count)
                throw new InvalidDataException("CELL previous cold process handoff is malformed.");
            var prior = new HashSet<Guid>();
            foreach (var attachment in previous.PreviousAttachments)
            {
                ValidateAttachment(attachment, previous.PreviousProcess, cells);
                if (!prior.Add(attachment.Identity) || !attachments.ContainsKey(attachment.Identity))
                    throw new InvalidDataException("CELL previous native evidence has a foreign/duplicate source attachment.");
            }
            if (previous.AwaitingNativeAttachments.Any(identity => !prior.Contains(identity) ||
                !attachments.TryGetValue(identity, out var current) || current.Retired || current.RootPublished))
                throw new InvalidDataException("CELL cold pending list discarded an original source/native owner.");
        }
        foreach (var attachment in attachments.Values)
            if (!attachment.Retired && attachment.NativeRoot == 0 &&
                saved.ColdHandoff?.AwaitingNativeAttachments.Contains(attachment.Identity) != true)
                throw new InvalidDataException("CELL attachment has no actual native root or cold rebind owner.");
        // Only after every source/history/native assertion passes does the new
        // process acquire the retained graph. No source callback is replayed.
        foreach (var (cell, state) in cells) _cells.Add(cell, state);
        _transitions.AddRange(saved.Transitions); _sequence = saved.Sequence;
        foreach (var (identity, attachment) in attachments)
            _attachments.Add(identity, attachment with { Process = _process, NativeRoot = 0, RootPublished = false,
                Children = attachment.Children.Select(child => child with { Placement = child.Placement.Copy(), NativeObjects = [],
                    Owner = attachment.Retired ? child.Owner : null,
                    Phase = attachment.Retired ? FalloutCellProcessChildPhase.Retired : FalloutCellProcessChildPhase.Pending }).ToArray() });
        _cold = new(saved.CapturedProcess, _process, Next(), attachments.Values.Where(attachment => !attachment.Retired)
            .Select(attachment => attachment.Identity).ToArray(), attachments.Values.Select(CopyAttachment).ToArray());
    }
    internal static void RequireCommittedSnapshot(FalloutCellProcessesSnapshot saved)
    {
        if (saved.Cells.Any(cell => cell.Failure is not null || cell.Phase is FalloutCellProcessPhase.LoadingData or
                FalloutCellProcessPhase.ReleasingData or FalloutCellProcessPhase.Detaching or FalloutCellProcessPhase.Attaching) ||
            saved.ColdHandoff?.AwaitingNativeAttachments.Count > 0 ||
            saved.Attachments.Any(attachment => !attachment.Retired && (attachment.Failure is not null ||
                !attachment.RootPublished || attachment.NativeRoot == 0 || attachment.Children.Any(child => child.Failure is not null ||
                    child.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.Failed or FalloutCellProcessChildPhase.Retired))))
            throw new NotSupportedException("Current committed save retained an unfinished/faulted CELL source/native operation.");
    }
    private void ValidateAttachment(FalloutCellProcessAttachment attachment, Guid process,
        IReadOnlyDictionary<FalloutFormKey, FalloutCellProcessEntry> cells)
    {
        if (attachment.Identity == Guid.Empty || attachment.Process != process || attachment.CellEpochs is null ||
            attachment.CellEpochs.Count == 0 || attachment.Children is null ||
            attachment.Failure is not null && string.IsNullOrWhiteSpace(attachment.Failure) ||
            attachment.Retired && (attachment.NativeRoot != 0 || attachment.RootPublished))
            throw new InvalidDataException("CELL attachment has invalid retained source/native ownership.");
        foreach (var (cell, epoch) in attachment.CellEpochs)
            if (!cells.TryGetValue(cell, out var state) || epoch < 2 || epoch > state.Epoch)
                throw new InvalidDataException("CELL attachment has a foreign source epoch.");
        var references = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        foreach (var child in attachment.Children)
        {
            _source.ValidateChildSource(child);
            if (!references.Add(child.Source.Reference) || !attachment.CellEpochs.ContainsKey(child.Placement.Cell) ||
                !Enum.IsDefined(child.Phase) || child.NativeObjects is null || child.NativeObjects.Any(identity => identity == 0) ||
                child.NativeObjects.Distinct().Count() != child.NativeObjects.Count ||
                child.Failure is not null && string.IsNullOrWhiteSpace(child.Failure) ||
                child.Phase == FalloutCellProcessChildPhase.Failed && child.Failure is null ||
                child.Phase != FalloutCellProcessChildPhase.Pending && string.IsNullOrWhiteSpace(child.Owner) ||
                child.Phase == FalloutCellProcessChildPhase.Published && child.NativeObjects.Count == 0 ||
                child.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.SourceDisabled or FalloutCellProcessChildPhase.SourceNoDraw or FalloutCellProcessChildPhase.Retired && child.NativeObjects.Count != 0 ||
                attachment.Retired && child.Phase != FalloutCellProcessChildPhase.Retired)
                throw new InvalidDataException("CELL child continuation lost its exact source/native disposition.");
        }
    }
    private static void ValidateTransitions(FalloutCellProcessesSnapshot saved,
        IReadOnlyDictionary<FalloutFormKey, FalloutCellProcessEntry> cells,
        IReadOnlyDictionary<Guid, FalloutCellProcessAttachment> attachments)
    {
        var replay = new Dictionary<FalloutFormKey, (FalloutCellProcessPhase Phase, long Epoch, Guid? Attachment)>(FalloutFormKeyComparer.Instance);
        var previous = 0L;
        foreach (var transition in saved.Transitions)
        {
            if (transition.Sequence <= previous || transition.Sequence > saved.Sequence || !cells.ContainsKey(transition.Cell) ||
                string.IsNullOrWhiteSpace(transition.Owner) || !Enum.IsDefined(transition.Operation))
                throw new InvalidDataException("CELL operation history lost its real source/order.");
            var constructed = replay.TryGetValue(transition.Cell, out var before);
            if (transition.Operation == FalloutCellProcessOperation.Construct)
            {
                if (constructed || transition.Before != FalloutCellProcessPhase.Constructed || transition.After != transition.Before ||
                    transition.CellEpoch != 1 || transition.Attachment is not null)
                    throw new InvalidDataException("CELL operation history invented a constructor.");
                replay.Add(transition.Cell, (transition.After, 1, null)); previous = transition.Sequence; continue;
            }
            if (!constructed || before.Phase != transition.Before)
                throw new InvalidDataException("CELL operation history skipped its consumed phase prefix.");
            var next = before;
            switch (transition.Operation)
            {
                case FalloutCellProcessOperation.BeginLoad when before.Phase == FalloutCellProcessPhase.Constructed:
                    next.Phase = FalloutCellProcessPhase.LoadingData; break;
                case FalloutCellProcessOperation.CompleteLoad when before.Phase == FalloutCellProcessPhase.LoadingData:
                    next.Phase = FalloutCellProcessPhase.DataLoaded; break;
                case FalloutCellProcessOperation.BeginAttach when before.Phase == FalloutCellProcessPhase.DataLoaded && before.Attachment is null:
                case FalloutCellProcessOperation.AttachmentRebind when before.Phase == FalloutCellProcessPhase.Attached && before.Attachment == transition.Attachment:
                    next = (FalloutCellProcessPhase.Attaching, checked(before.Epoch + 1), transition.Attachment); break;
                case FalloutCellProcessOperation.CompleteAttach when before.Phase == FalloutCellProcessPhase.Attaching:
                    next.Phase = FalloutCellProcessPhase.Attached; break;
                case FalloutCellProcessOperation.BeginDetach when before.Phase is FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached:
                    next.Phase = FalloutCellProcessPhase.Detaching; break;
                case FalloutCellProcessOperation.CompleteDetach when before.Phase == FalloutCellProcessPhase.Detaching:
                    next = (FalloutCellProcessPhase.DataLoaded, before.Epoch, null); break;
                case FalloutCellProcessOperation.BeginRelease when before.Phase is FalloutCellProcessPhase.DataLoaded or FalloutCellProcessPhase.Constructed && before.Attachment is null:
                    next.Phase = FalloutCellProcessPhase.ReleasingData; break;
                case FalloutCellProcessOperation.CompleteRelease when before.Phase == FalloutCellProcessPhase.ReleasingData:
                    next.Phase = FalloutCellProcessPhase.Constructed; break;
                default: throw new InvalidDataException("CELL operation history changed an original source phase branch.");
            }
            var attaching = transition.Operation is FalloutCellProcessOperation.BeginAttach or FalloutCellProcessOperation.AttachmentRebind;
            var nativeOperation = attaching || transition.Operation is FalloutCellProcessOperation.CompleteAttach or FalloutCellProcessOperation.BeginDetach or FalloutCellProcessOperation.CompleteDetach;
            if (transition.After != next.Phase || transition.CellEpoch != next.Epoch ||
                nativeOperation && (transition.Attachment is not { } identity || !attachments.TryGetValue(identity, out var owner) ||
                    !owner.CellEpochs.ContainsKey(transition.Cell) || !attaching && before.Attachment != transition.Attachment) ||
                !nativeOperation && transition.Attachment is not null)
                throw new InvalidDataException("CELL operation history lost its original native attachment/epoch.");
            replay[transition.Cell] = next; previous = transition.Sequence;
        }
        if (replay.Count != cells.Count || cells.Any(pair => !replay.TryGetValue(pair.Key, out var final) ||
            final.Phase != pair.Value.Phase || final.Epoch != pair.Value.Epoch))
            throw new InvalidDataException("CELL current phase differs from its complete consumed operation prefix.");
    }
    private static FalloutCellProcessAttachment CopyAttachment(FalloutCellProcessAttachment attachment) => attachment with
    {
        CellEpochs = attachment.CellEpochs.ToDictionary(pair => pair.Key, pair => pair.Value, FalloutFormKeyComparer.Instance),
        Children = attachment.Children.Select(child => child with { Placement = child.Placement.Copy(), NativeObjects = child.NativeObjects.ToArray() }).ToArray(),
    };
}
