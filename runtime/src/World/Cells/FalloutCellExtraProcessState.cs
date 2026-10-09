using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A real CELL extra owns its own count. Its first creation walks the current
// source reference list before incrementing; an interrupted call retains that
// created object and exact consumed prefix, including a still-zero count.
internal sealed partial class FalloutCellExtraProcessState : IDisposable
{
    internal const string Schema = "opennv-cell-extra-process/v1";
    private readonly FalloutActorProcessQueueDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutCellProcessIdentity> _identity;
    private readonly Func<FalloutFormKey, FalloutCellProcessReferenceList> _references;
    private readonly Action<FalloutCellProcessListReference, Guid, string> _reevaluate;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutCellExtraProcessEntry> _cells = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutCellExtraProcessInvocation> _invocations = [];
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _busy, _disposed;

    internal FalloutCellExtraProcessState(FalloutActorProcessQueueDeclaration source, string stack,
        Func<FalloutFormKey, FalloutCellProcessIdentity> identity,
        Func<FalloutFormKey, FalloutCellProcessReferenceList> references,
        Action<FalloutCellProcessListReference, Guid, string> reevaluate, FalloutCellExtraProcessSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(references); ArgumentNullException.ThrowIfNull(reevaluate);
        _source = source; _stack = stack; _identity = identity; _references = references; _reevaluate = reevaluate;
        if (restore is not null) Restore(restore);
    }

    internal string? SaveBlocker => _busy ? "actual-CELL-extra-process-consumer-in-flight" :
        _invocations.FirstOrDefault(item => item.Phase != FalloutCellExtraProcessPhase.Complete) is { } pending ?
            "actual-CELL-extra-process:" + pending.Cell + ":" + (pending.Failure ?? pending.Phase.ToString()) : null;
    internal object State => new
    {
        source = _source.Contract,
        process = _process,
        _sequence,
        cells = _cells.Values.ToArray(),
        invocations = _invocations.ToArray(),
        cold = _cold,
        saveBlocker = SaveBlocker
    };

    internal void Construct(FalloutFormKey cell)
    {
        RequireNotBusy();
        if (_cells.ContainsKey(cell)) return;
        var source = Callback(() => _identity(cell)); RequireCell(source, cell);
        _cells.Add(cell, new(source, false, 0, 0, Next(), null));
    }
    internal FalloutActorProcessFact<bool> ReadPresence(FalloutFormKey? cell)
    {
        RequireNotBusy();
        if (cell is null) return new(false, "actual-null-CELL-has-no-extra-process-owner");
        Construct(cell.Value); var state = _cells[cell.Value];
        return new(state.Present, "actual-CELL-ExtraProcessMiddleLow:" + cell + "/" + state.Generation, state.Failure);
    }

    internal Guid Change(FalloutFormKey cell, bool increase, string owner)
    {
        Construct(cell); RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var state = _cells[cell];
        if (state.Failure is not null || _invocations.Any(item => item.Phase != FalloutCellExtraProcessPhase.Complete))
            throw new NotSupportedException("An earlier counted CELL consumer retains its original prefix/failure.");
        var identity = Guid.NewGuid(); var sequence = Next();
        _invocations.Add(new(identity, cell, owner, increase, state.Present, state.Count,
            state.Generation, FalloutCellExtraProcessPhase.Entered, null, 0, null, sequence, sequence, null));
        Operation(identity, () =>
        {
            if (!state.Present && !increase) { Set(identity, FalloutCellExtraProcessPhase.Complete); return; }
            if (!state.Present)
            {
                state = state with { Present = true, Count = 0, Generation = checked(state.Generation + 1), Changed = Next() };
                _cells[cell] = state;
                Set(identity, FalloutCellExtraProcessPhase.ExtraCreated, generation: state.Generation);
                var references = Callback(() => _references(cell)); ValidateList(references, state.Source);
                Set(identity, FalloutCellExtraProcessPhase.WalkingReferences, references: references);
                for (var index = 0; index < references.References.Count; index++)
                {
                    var reference = references.References[index];
                    Set(identity, FalloutCellExtraProcessPhase.WalkingReferences, next: index);
                    if (reference.Actor is not null && (reference.CurrentReferenceFlags & FalloutActorProcessQueueDeclaration.DisabledReferenceFlag) == 0 &&
                        reference.HasProcess!.Require() && reference.Level == FalloutDetectionProcessLevel.Low)
                    {
                        Set(identity, FalloutCellExtraProcessPhase.WalkingReferences, inFlight: reference.Reference);
                        Callback(() => { _reevaluate(reference, identity, owner); return true; });
                    }
                    Set(identity, FalloutCellExtraProcessPhase.WalkingReferences, next: checked(index + 1));
                }
                Set(identity, FalloutCellExtraProcessPhase.ConsumersReturned);
            }
            var count = FalloutActorProcessQueueDeclaration.StoreCount(_cells[cell].Count, increase);
            _cells[cell] = _cells[cell] with { Count = count, Changed = Next() };
            Set(identity, FalloutCellExtraProcessPhase.CountStored);
            if (count == 0)
            {
                _cells[cell] = _cells[cell] with { Present = false, Changed = Next() };
                Set(identity, FalloutCellExtraProcessPhase.ExtraRemoved);
            }
            Set(identity, FalloutCellExtraProcessPhase.Complete);
        });
        return identity;
    }

    // A CELL release has a separate actual source loop that removes this
    // counted object. It does not impose a guessed retry/iteration budget.
    internal void RemoveForSourceRelease(FalloutFormKey cell, string owner)
    {
        Construct(cell); RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        while (_cells[cell].Present) Change(cell, false, owner);
    }
    private void Set(Guid identity, FalloutCellExtraProcessPhase phase, long? generation = null,
        FalloutCellProcessReferenceList? references = null, int? next = null, FalloutFormKey? inFlight = null)
    {
        var index = _invocations.FindIndex(item => item.Identity == identity);
        var state = _invocations[index];
        _invocations[index] = state with
        {
            Phase = phase,
            Generation = generation ?? state.Generation,
            CurrentReferences = references ?? state.CurrentReferences,
            NextReference = next ?? state.NextReference,
            InFlight = inFlight,
            Changed = Next()
        };
    }
    private void Operation(Guid identity, Action action)
    {
        RequireNotBusy(); _busy = true; var faults = _callbackFault;
        try
        {
            action();
            if (_callbackFault != faults) throw new InvalidOperationException("Counted CELL callback caught an actual reentry failure.");
        }
        catch (Exception error)
        {
            var index = _invocations.FindIndex(item => item.Identity == identity); var state = _invocations[index];
            var failure = Message(error);
            _invocations[index] = state with { Failure = state.Failure ?? failure, Changed = Next() };
            _cells[state.Cell] = _cells[state.Cell] with { Failure = _cells[state.Cell].Failure ?? failure, Changed = Next() };
            throw;
        }
        finally { _busy = false; }
    }
    private T Callback<T>(Func<T> callback)
    {
        var previous = _busy; _busy = true; var faults = _callbackFault;
        try
        {
            var result = callback();
            if (_callbackFault != faults) throw new InvalidOperationException("Counted CELL producer caught an actual reentry failure.");
            return result;
        }
        finally { _busy = previous; }
    }
    private void RequireNotBusy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Counted CELL source operation reentered."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static string Message(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    internal static void RequireCell(FalloutCellProcessIdentity source, FalloutFormKey cell)
    {
        if (source is null || source.Cell != cell || cell.ObjectId == 0 || string.IsNullOrWhiteSpace(cell.OwnerPlugin) ||
            source.Sha256 is not { Length: 64 } || !source.Sha256.All(Uri.IsHexDigit) ||
            (source.Worldspace is null) != (source.WorldspaceSha256 is null) || source.WorldspaceSha256 is { } world &&
            (world.Length != 64 || !world.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Counted CELL owner lost its exact winning CELL/world/master source.");
    }
    internal static void ValidateList(FalloutCellProcessReferenceList list, FalloutCellProcessIdentity source)
    {
        if (list is null || list.Cell != source || list.Revision < 1 || string.IsNullOrWhiteSpace(list.Owner) || list.References is null ||
            list.References.Any(item => item is null || item.Source is null && item.Actor is not { EnginePlayer: true }) ||
            list.References.Select(item => item.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != list.References.Count ||
            list.References.Select(item => item.Membership).Distinct().Count() != list.References.Count)
            throw new InvalidDataException("Counted CELL iteration lost its genuine complete current reference-list identity/order.");
        foreach (var item in list.References)
        {
            if (item.Source is null)
            {
                if (item.Actor is not { EnginePlayer: true } player || item.CurrentCell != source.Cell || item.Membership < 1 ||
                    item.ProcessEpoch is null or < 1 || item.HasProcess is null || string.IsNullOrWhiteSpace(item.HasProcess.Owner) ||
                    item.HasProcess.Value == true && item.HasProcess.Failure is null && item.Level is null ||
                    item.HasProcess.Value != true && item.Level is not null || item.Level is { } level && !Enum.IsDefined(level))
                    throw new InvalidDataException("Counted CELL canonical Player lost its real source/current process owner.");
                player.Validate(); continue;
            }
            if (item.Source.Reference.ObjectId == 0 || item.CurrentCell != source.Cell || item.Membership < 1 ||
                item.Source.SourceCell != source.Cell ||
                (item.Source.Signature is "ACHR" or "ACRE") != (item.Actor is not null) ||
                item.Source.Sha256 is not { Length: 64 } || !item.Source.Sha256.All(Uri.IsHexDigit) ||
                (item.Actor is null ? item.Level is not null || item.ProcessEpoch is not null || item.HasProcess is not null :
                    item.Actor.Reference != item.Source.Reference || item.Actor.ReferenceSha256 != item.Source.Sha256 ||
                    item.Actor.ReferenceSignature != item.Source.Signature || item.Actor.ReferenceFlags != item.Source.Flags ||
                    item.Actor.Base != item.Source.Base || item.Actor.BaseSha256 != item.Source.BaseSha256 || item.ProcessEpoch is null or < 1 ||
                    item.HasProcess is null || string.IsNullOrWhiteSpace(item.HasProcess.Owner) ||
                    item.HasProcess.Value == false && item.Level is not null || item.HasProcess.Value is null && item.Level is not null ||
                    item.HasProcess.Value == true && item.HasProcess.Failure is null && (item.ProcessEpoch is null or < 1 || item.Level is null) ||
                    item.ProcessEpoch is < 1 || item.Level is not null && !Enum.IsDefined(item.Level.Value)))
                throw new InvalidDataException("Counted CELL child has a foreign source/current placement/Actor process.");
            item.Actor?.Validate();
        }
    }
    public void Dispose()
    {
        if (_disposed) return; RequireNotBusy();
        if (_invocations.Any(item => item.Phase != FalloutCellExtraProcessPhase.Complete))
            throw new NotSupportedException("Counted CELL retirement retains an unfinished real consumer.");
        _disposed = true; _cells.Clear();
    }
}
