using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceParentCellPublication(Guid Process, Guid Factory, Guid Constructor,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutReferenceParentCellStore(Guid Process, Guid Factory, Guid Constructor,
    FalloutCombatActorIdentity Actor, long Revision, FalloutFormKey? Before, FalloutFormKey? After,
    bool ConstructorStore, Guid? MainInvocation, FalloutMainPlayerCellStep? MainStep,
    bool Stored, bool Returned, string? Failure);
internal sealed record FalloutReferenceParentCellSnapshot(string Source, string Stack, string ExecutableSha256,
    Guid Process, Guid Factory, Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence,
    FalloutFormKey? Cell, IReadOnlyList<FalloutReferenceParentCellPublication> Publications,
    IReadOnlyList<FalloutReferenceParentCellStore> Stores, bool Retired);

// This records writes to the existing World ParentCELL field. It owns no
// replacement CELL value and cannot supply a pending or rendered destination.
internal sealed class FalloutReferenceParentCellHistory
{
    internal const string Source = "actual-World-source-linked-Player-ParentCELL";
    private readonly string _stack;
    private readonly string _executable;
    private readonly Guid _process, _factory, _constructor;
    private readonly FalloutCombatActorIdentity _actor;
    private readonly List<FalloutReferenceParentCellStore> _stores = [];
    private readonly HashSet<(Guid Main, FalloutMainPlayerCellStep Step)> _calls = [];
    private bool _entered;
    private bool _retired;

    internal FalloutReferenceParentCellHistory(FalloutReferencePositionSource source, string stack,
        Guid process, Guid factory, Guid constructor, FalloutCombatActorIdentity actor,
        long actualRevision, FalloutFormKey? actualCell)
    {
        source.Validate(); actor.Validate();
        if (process == Guid.Empty || factory == Guid.Empty || constructor == Guid.Empty ||
            actualRevision != 1 || actualCell is not null || string.IsNullOrWhiteSpace(stack) ||
            actor.EnginePlayer && actor.ReferenceSha256 != source.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("ParentCELL history has no actual source-qualified null constructor store.");
        _stack = stack; _executable = source.Fields.Controls.Main.EngineSha256; _process = process; _factory = factory;
        _constructor = constructor; _actor = actor;
        _stores.Add(new(process, factory, constructor, actor, 1, null, null, true, null, null, true, true, null));
    }

    internal void BeginStore(Guid main, FalloutMainPlayerCellStep step, long actualRevision,
        FalloutFormKey? before, FalloutFormKey destination)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_entered || !_stores[^1].Returned || main == Guid.Empty || step is not
            (FalloutMainPlayerCellStep.PendingDestination or FalloutMainPlayerCellStep.CellAttach) ||
            _calls.Contains((main, step)) || _stores[^1].Revision != actualRevision ||
            _stores[^1].After != before)
            throw new InvalidOperationException("ParentCELL write left its actual once-only Main child or current field.");
        _calls.Add((main, step)); _entered = true;
        _stores.Add(new(_process, _factory, _constructor, _actor, checked(actualRevision + 1),
            before, destination, false, main, step, false, false, null));
    }

    internal void ObserveStore(long actualRevision, FalloutFormKey? actualCell)
    {
        if (!_entered || _stores[^1].Stored || _stores[^1].Revision != actualRevision ||
            _stores[^1].After != actualCell)
            throw new InvalidDataException("ParentCELL receipt does not match the actual committed World field.");
        _stores[^1] = _stores[^1] with { Stored = true };
    }

    internal void EndStore(Exception? failure)
    {
        if (!_entered || failure is null && !_stores[^1].Stored)
            throw new InvalidOperationException("ParentCELL write returned without its actual field store.");
        _stores[^1] = _stores[^1] with { Returned = failure is null, Failure = failure?.Message };
        _entered = false;
    }

    internal void RequireCurrentField(long actualRevision, FalloutFormKey? actualCell)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        var committed = _stores[^1].Stored ? _stores[^1] : _stores[^2];
        if (committed.Revision != actualRevision || committed.After != actualCell)
            throw new InvalidDataException("Current World ParentCELL has no retained actual writer.");
    }

    internal FalloutReferenceParentCellSnapshot Capture(long actualRevision, FalloutFormKey? actualCell)
    {
        if (_entered) throw new InvalidOperationException("Entered ParentCELL publication cannot be captured.");
        if (!_retired) RequireCurrentField(actualRevision, actualCell);
        var saved = new FalloutReferenceParentCellSnapshot(Source, _stack, _executable, _process, _factory,
            _constructor, _actor, actualRevision, actualCell, [new(_process, _factory, _constructor)],
            Array.AsReadOnly(_stores.ToArray()), _retired);
        _ = new FalloutReferenceParentCellValidation(saved);
        return saved;
    }
    internal void Retire() { if (_entered) throw new InvalidOperationException("Entered ParentCELL field cannot retire."); _retired = true; }
}

// Build the retained writer index once; every package borrow joins in O(1).
internal sealed class FalloutReferenceParentCellValidation
{
    private readonly FalloutReferenceParentCellSnapshot _saved;
    private readonly Dictionary<(Guid Process, Guid Factory, Guid Constructor, long Revision), FalloutFormKey?> _fields = [];
    internal FalloutReferenceParentCellValidation(FalloutReferenceParentCellSnapshot saved)
    {
        saved.Actor.Validate();
        if (saved.Source != FalloutReferenceParentCellHistory.Source || saved.Process == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Sequence < 1 ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.ExecutableSha256.Length != 64 ||
            saved.Actor.EnginePlayer && saved.ExecutableSha256 != saved.Actor.ReferenceSha256 ||
            saved.Publications is not [var publication] || publication.Process != saved.Process ||
            publication.Factory != saved.Factory || publication.Constructor != saved.Constructor ||
            publication.PreviousProcess is not null || publication.PreviousFactory is not null ||
            saved.Stores.Count == 0)
            throw new InvalidDataException("ParentCELL history lost its actual original field publication.");
        long revision = 0; FalloutFormKey? cell = null; var failed = false;
        var calls = new HashSet<(Guid Main, FalloutMainPlayerCellStep Step)>();
        foreach (var store in saved.Stores)
        {
            if (failed || store.Process != saved.Process || store.Factory != saved.Factory ||
                store.Constructor != saved.Constructor || store.Actor != saved.Actor ||
                store.Revision != revision + 1 || store.Before != cell ||
                store.Returned != (store.Failure is null) || store.Returned && !store.Stored ||
                store.ConstructorStore != (revision == 0) ||
                store.ConstructorStore && (store.MainInvocation is not null || store.MainStep is not null ||
                    store.After is not null || !store.Stored || !store.Returned) ||
                !store.ConstructorStore && (store.MainInvocation is not { } main || main == Guid.Empty ||
                    store.MainStep is not (FalloutMainPlayerCellStep.PendingDestination or FalloutMainPlayerCellStep.CellAttach) ||
                    !calls.Add((main, store.MainStep.Value))))
                throw new InvalidDataException("ParentCELL history changed its original stores, Main children or failed prefix.");
            if (store.Stored)
            {
                revision = store.Revision; cell = store.After;
                if (!_fields.TryAdd((store.Process, store.Factory, store.Constructor, revision), cell))
                    throw new InvalidDataException("ParentCELL history repeated a committed writer revision.");
            }
            failed = !store.Returned;
        }
        if (revision != saved.Sequence || cell != saved.Cell)
            throw new InvalidDataException("Current ParentCELL differs from its retained actual writer history.");
        _saved = saved;
    }
    internal void ValidateRead(FalloutReferenceParentCellRead read)
    {
        if (read.Source != _saved.Source || read.Stack != _saved.Stack || read.Actor != _saved.Actor ||
            read.ExecutableSha256 != _saved.ExecutableSha256 ||
            !_fields.TryGetValue((read.Process, read.Factory, read.Constructor, read.Sequence), out var cell) ||
            cell != read.Cell)
            throw new InvalidDataException("ParentCELL borrow has no identical retained actual World field writer.");
    }
}
