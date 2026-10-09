using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellExtraProcessState
{
    internal FalloutCellExtraProcessSnapshot Capture()
    {
        RequireNotBusy();
        return new(Schema, _stack, _source.Contract, _process, _sequence,
            _cells.Values.ToArray(), _invocations.ToArray(), _cold);
    }
    private void Restore(FalloutCellExtraProcessSnapshot saved)
    {
        Validate(saved);
        if (saved.Stack != _stack || saved.Contract != _source.Contract || saved.CapturedProcess == _process)
            throw new InvalidDataException("Counted CELL cold state has a foreign source/new-process owner.");
        foreach (var cell in saved.Cells)
        {
            var actual = Callback(() => _identity(cell.Source.Cell));
            if (actual != cell.Source) throw new InvalidDataException("Counted CELL source/master/winner changed during cold restoration.");
            _cells.Add(cell.Source.Cell, cell);
        }
        _invocations.AddRange(saved.Invocations); _sequence = saved.Sequence;
        _cold = new(saved.CapturedProcess, _process, Next());
        // No original reference walk, reevaluation callback or increment is
        // executed by restoration. A failed prefix stays failed and counted.
    }
    internal static void Validate(FalloutCellExtraProcessSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || string.IsNullOrWhiteSpace(saved.Stack) || saved.Contract is not { Length: 64 } ||
            !saved.Contract.All(Uri.IsHexDigit) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 ||
            saved.Cells is null || saved.Invocations is null || saved.Cells.Any(item => item is null || item.Source is null) ||
            saved.Invocations.Any(item => item is null) ||
            saved.Cells.Select(item => item.Source.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Cells.Count ||
            saved.Invocations.Select(item => item.Identity).Distinct().Count() != saved.Invocations.Count)
            throw new InvalidDataException("Counted CELL continuation has an incomplete/duplicated source graph.");
        var histories = new Dictionary<FalloutFormKey, (bool Present, uint Count, long Generation)>(FalloutFormKeyComparer.Instance);
        var previous = 0L;
        var unfinished = false;
        foreach (var item in saved.Invocations)
        {
            var cell = saved.Cells.SingleOrDefault(entry => entry.Source.Cell == item.Cell) ??
                throw new InvalidDataException("Counted CELL invocation omitted its real source owner.");
            var before = histories.GetValueOrDefault(item.Cell, (Present: false, Count: 0u, Generation: 0L));
            if (item.Identity == Guid.Empty || string.IsNullOrWhiteSpace(item.Owner) || !Enum.IsDefined(item.Phase) ||
                item.Phase == FalloutCellExtraProcessPhase.Failed || item.Entered <= previous || item.Entered > item.Changed ||
                item.Changed > saved.Sequence || item.NextReference < 0 || item.Failure is not null && string.IsNullOrWhiteSpace(item.Failure) ||
                unfinished || item.BeforePresent != before.Present || item.BeforeCount != before.Count ||
                item.Generation != before.Generation + (!before.Present && item.Increase && item.Phase >= FalloutCellExtraProcessPhase.ExtraCreated ? 1 : 0))
                throw new InvalidDataException("Counted CELL continuation changed original call order/count/generation/consumed phase.");
            previous = item.Changed;
            if (item.CurrentReferences is { } references)
            {
                ValidateList(references, cell.Source);
                if (item.NextReference > references.References.Count || item.InFlight is { } current &&
                    (item.NextReference >= references.References.Count || references.References[item.NextReference].Reference != current ||
                        references.References[item.NextReference].Actor is null ||
                        (references.References[item.NextReference].CurrentReferenceFlags & FalloutActorProcessQueueDeclaration.DisabledReferenceFlag) != 0 ||
                        references.References[item.NextReference].Level != FalloutDetectionProcessLevel.Low ||
                        !references.References[item.NextReference].HasProcess!.Require()))
                    throw new InvalidDataException("Counted CELL continuation lost its actual in-flight Low Actor/reference ordinal.");
                foreach (var consumed in references.References.Take(item.NextReference))
                    if (consumed.Actor is not null &&
                        (consumed.CurrentReferenceFlags & FalloutActorProcessQueueDeclaration.DisabledReferenceFlag) == 0)
                        _ = consumed.HasProcess!.Require();
            }
            else if (item.NextReference != 0 || item.InFlight is not null)
                throw new InvalidDataException("Counted CELL has consumed references without a source list.");
            if (!before.Present && item.Increase && item.Phase >= FalloutCellExtraProcessPhase.WalkingReferences && item.CurrentReferences is null ||
                (!item.Increase || before.Present) && (item.CurrentReferences is not null || item.InFlight is not null) ||
                item.Phase >= FalloutCellExtraProcessPhase.ConsumersReturned && !before.Present && item.Increase &&
                    (item.CurrentReferences is null || item.NextReference != item.CurrentReferences.References.Count || item.InFlight is not null))
                throw new InvalidDataException("Counted CELL claims a reference consumer that was not completed.");
            var present = before.Present || item.Increase && item.Phase >= FalloutCellExtraProcessPhase.ExtraCreated;
            var count = before.Count;
            if (item.Phase >= FalloutCellExtraProcessPhase.CountStored && (before.Present || item.Increase))
                count = FalloutActorProcessQueueDeclaration.StoreCount(before.Count, item.Increase);
            if (item.Phase >= FalloutCellExtraProcessPhase.ExtraRemoved && count == 0) present = false;
            if (item.Phase == FalloutCellExtraProcessPhase.Complete && count == 0 && present)
                throw new InvalidDataException("Counted CELL retained the object after its original stored-zero removal.");
            histories[item.Cell] = (present, count, item.Generation);
            unfinished = item.Failure is not null || item.Phase != FalloutCellExtraProcessPhase.Complete;
            if (unfinished && cell.Failure != item.Failure && item.Failure is not null)
                throw new InvalidDataException("Counted CELL failure lost its exact still-owned source prefix.");
        }
        foreach (var cell in saved.Cells)
        {
            RequireCell(cell.Source, cell.Source.Cell);
            var actual = histories.GetValueOrDefault(cell.Source.Cell, (Present: false, Count: 0u, Generation: 0L));
            if (cell.Changed < 1 || cell.Changed > saved.Sequence || cell.Generation < 0 ||
                cell.Present != actual.Present || cell.Count != actual.Count || cell.Generation != actual.Generation ||
                !cell.Present && cell.Count != 0 || cell.Failure is not null && string.IsNullOrWhiteSpace(cell.Failure) ||
                cell.Failure is not null && !saved.Invocations.Any(item => item.Cell == cell.Source.Cell && item.Failure == cell.Failure))
                throw new InvalidDataException("Counted CELL state is not the retained result of its real source transitions.");
        }
        RequireHandoff(saved.ColdHandoff, saved.CapturedProcess, saved.Sequence);
    }
    internal static void RequireHandoff(FalloutActorProcessRuntimeHandoff? cold, Guid process, long sequence)
    {
        if (cold is not null && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != process ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > sequence))
            throw new InvalidDataException("Process queue continuation lost its genuine new-process handoff.");
    }
}
