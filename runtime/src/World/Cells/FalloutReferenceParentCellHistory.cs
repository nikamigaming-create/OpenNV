using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceParentCellPublication(Guid Process, Guid Factory, Guid Constructor,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null, long? PublishedRevision = null);
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
internal sealed partial class FalloutReferenceParentCellHistory
{
    internal const string Source = "actual-World-source-linked-Player-ParentCELL";
    private readonly string _stack;
    private readonly string _executable;
    private readonly Guid _process, _factory, _constructor;
    private readonly FalloutCombatActorIdentity _actor;
    private readonly List<FalloutReferenceParentCellStore> _stores = [];
    private readonly List<FalloutReferenceParentCellPublication> _publications = [];
    private readonly HashSet<(Guid Main, FalloutMainPlayerCellStep Step)> _calls = [];
    private bool _entered;
    private bool _retired;

    internal FalloutReferenceParentCellHistory(FalloutReferencePositionSource source, string stack,
        Guid process, Guid factory, Guid constructor, FalloutCombatActorIdentity actor,
        long actualRevision, FalloutFormKey? actualCell, FalloutReferenceParentCellSnapshot? saved = null)
    {
        source.Validate(); actor.Validate();
        if (process == Guid.Empty || factory == Guid.Empty || constructor == Guid.Empty ||
            saved is null && (actualRevision != 1 || actualCell is not null) || string.IsNullOrWhiteSpace(stack) ||
            actor.EnginePlayer && actor.ReferenceSha256 != source.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("ParentCELL history has no actual source-qualified null constructor store.");
        _stack = stack; _executable = source.Fields.Controls.Main.EngineSha256; _process = process; _factory = factory;
        _constructor = constructor; _actor = actor;
        if (saved is null)
        {
            _publications.Add(new(process, factory, constructor, PublishedRevision: 0));
            _stores.Add(new(process, factory, constructor, actor, 1, null, null, true, null, null, true, true, null));
        }
        else RestoreHistory(saved, actualRevision, actualCell);
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
            _constructor, _actor, actualRevision, actualCell, Array.AsReadOnly(_publications.ToArray()),
            Array.AsReadOnly(_stores.ToArray()), _retired);
        _ = new FalloutReferenceParentCellValidation(saved);
        return saved;
    }
    internal void Retire() { if (_entered) throw new InvalidOperationException("Entered ParentCELL field cannot retire."); _retired = true; }
}
