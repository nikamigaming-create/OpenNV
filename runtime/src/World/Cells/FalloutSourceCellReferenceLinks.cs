using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal interface IFalloutSourceCellReferenceIngestion
{
    string EngineSha256 { get; }
    string Owner { get; }
    // This enumerates entered source insertions, including repeated updates.
    // Neither a final winning set nor a sorted scene supplies the invocation order.
    IEnumerable<FalloutFormKey> ReadEnteredInsertions(FalloutCellProcessData cell);
}
internal sealed record FalloutSourceCellLinkedMember(FalloutCellProcessReference Source, long Membership);
internal sealed record FalloutSourceCellRuntimeActorMember(FalloutCombatActorIdentity Source, long Membership, string Caller);
internal sealed record FalloutSourceCellLinkedList(FalloutCellProcessIdentity Source, string GraphSha256,
    long Revision, bool InitialInsertionsReturned, IReadOnlyList<FalloutSourceCellLinkedMember> Members,
    FalloutSourceCellIngestionEvidence? InitialEvidence = null,
    IReadOnlyList<FalloutSourceCellRuntimeActorMember>? RuntimeActors = null)
{
    internal IEnumerable<(FalloutFormKey Reference, long Membership)> OrderedReferences =>
        Members.Select(member => (member.Source.Reference, member.Membership)).Concat(
            (RuntimeActors ?? []).Select(member => (member.Source.Reference, member.Membership)))
        .OrderByDescending(member => member.Membership);
}
internal enum FalloutSourceCellLinkPhase { Entered, PreviousUnlinked, HeadInserted, ParentPublished, LocalReturned, Failed }
internal sealed record FalloutSourceCellLinkAttempt(long Identity, string Operation, string Owner,
    FalloutFormKey? Reference, FalloutFormKey? PreviousCell, FalloutFormKey Cell,
    FalloutSourceCellLinkPhase Phase, long Changed, string? Failure,
    FalloutSourceCellLinkPhase? FailedAtPhase = null);
internal sealed record FalloutSourceCellReferenceLinksSnapshot(string Schema,
    FalloutSourceCellReferenceLinksDeclaration Source, string Stack, Guid CapturedProcess, long Sequence,
    long Membership, IReadOnlyList<FalloutSourceCellLinkedList> Cells, IReadOnlyList<FalloutSourceCellLinkAttempt> Attempts,
    FalloutActorProcessRuntimeHandoff? ColdHandoff);

// This is one living mutable list for queue walkers and Sandbox discovery.
// The immutable graph authenticates members; it never supplies runtime order.
internal sealed class FalloutSourceCellReferenceLinks : IDisposable
{
    private const string Schema = "opennv-source-cell-reference-links/v2";
    private readonly FalloutSourceCellReferenceLinksDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutCellProcessData> _cell;
    private readonly Func<FalloutFormKey, FalloutCellProcessReference> _reference;
    private readonly Guid _process = Guid.NewGuid();
    private int _thread = Environment.CurrentManagedThreadId;
    private object? _nativeOwner;
    private readonly IFalloutSourceCellReferenceIngestion? _ingestion;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity>? _runtimeActor;
    private readonly Dictionary<FalloutFormKey, FalloutSourceCellLinkedList> _cells = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutSourceCellLinkAttempt> _attempts = [];
    private long _sequence, _membership;
    private bool _busy, _disposed;
    private FalloutActorProcessRuntimeHandoff? _cold;

    internal FalloutSourceCellReferenceLinks(FalloutSourceCellReferenceLinksDeclaration source, string stack,
        Func<FalloutFormKey, FalloutCellProcessData> cell, Func<FalloutFormKey, FalloutCellProcessReference> reference,
        IFalloutSourceCellReferenceIngestion? ingestion = null, FalloutSourceCellReferenceLinksSnapshot? saved = null,
        Func<FalloutFormKey, FalloutCombatActorIdentity>? runtimeActor = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(cell); ArgumentNullException.ThrowIfNull(reference);
        if (ingestion is not null && (ingestion.EngineSha256 != source.EngineSha256 || string.IsNullOrWhiteSpace(ingestion.Owner)))
            throw new InvalidDataException("CELL insertion reader changed its real selected source/caller.");
        _source = source; _stack = stack; _cell = cell; _reference = reference; _ingestion = ingestion; _runtimeActor = runtimeActor;
        if (saved is not null) Restore(saved);
    }

    internal string? SaveBlocker => _busy ? "source-CELL-reference-link-writer-entered" :
        _attempts.FirstOrDefault(value => value.Phase != FalloutSourceCellLinkPhase.LocalReturned) is { } attempt ?
            "source-CELL-reference-link:" + attempt.Operation + ":" + (attempt.Failure ?? attempt.Phase.ToString()) : null;
    internal object State => new
    {
        source = _source,
        process = _process,
        sequence = _sequence,
        cells = _cells.Values.ToArray(),
        attempts = _attempts.ToArray(),
        nativeThreadPublished = _nativeOwner is not null,
        saveBlocker = SaveBlocker
    };
    internal void PublishNativeOwner(object actualOwner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(actualOwner);
        if (_busy) throw new InvalidOperationException("Native CELL publication cannot move an entered source link writer.");
        if (_nativeOwner is not null)
        {
            if (!ReferenceEquals(_nativeOwner, actualOwner) || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("CELL link native publication changed its actual current root/thread.");
            return;
        }
        if (_attempts.Count != 0 && _cold is null)
            throw new InvalidOperationException("A warm source insertion already entered before its native owner publication.");
        // Background preparation may authenticate immutable source/cold rows.
        // The actual native world owner binds before the first new mutation.
        _thread = Environment.CurrentManagedThreadId; _nativeOwner = actualOwner;
    }

    private FalloutSourceCellLinkedList Construct(FalloutFormKey cell)
    {
        var actual = _cell(cell);
        FalloutCellExtraProcessState.RequireCell(actual.Source, cell);
        if (_cells.TryGetValue(cell, out var retained))
        {
            if (retained.Source != actual.Source || retained.GraphSha256 != actual.GraphSha256)
                throw new InvalidDataException("Mutable CELL list changed its immutable source graph.");
            return retained;
        }
        var result = new FalloutSourceCellLinkedList(actual.Source, actual.GraphSha256, Next(),
            actual.References.Count == 0 && _ingestion is not IFalloutSourceOwnedCellReferenceIngestion, [], RuntimeActors: []);
        _cells.Add(cell, result); return result;
    }

    private FalloutSourceCellLinkedList RequireInitial(FalloutFormKey cell)
    {
        var retained = Construct(cell);
        if (retained.InitialInsertionsReturned) return retained;
        var index = Enter("initial-source-insertions", _ingestion?.Owner ?? "source-CELL-initial-insertion-caller-order-unbound", null, null, cell);
        try
        {
            var producer = _ingestion ?? throw new NotSupportedException("Original CELL initial insertion caller/order is absent; winning/sorted records cannot replace it.");
            var actual = _cell(cell);
            if (producer is IFalloutSourceOwnedCellReferenceIngestion sourceOwner)
                _cells[cell] = retained with { InitialEvidence = sourceOwner.ReadEvidence(actual), Revision = Next() };
            foreach (var form in producer.ReadEnteredInsertions(actual))
            {
                var row = _reference(form);
                if (row.SourceCell != cell || !actual.References.Contains(row))
                    throw new InvalidDataException("Initial CELL insertion has a foreign winning source member.");
                InsertHead(cell, row); Set(index, FalloutSourceCellLinkPhase.HeadInserted);
            }
            retained = _cells[cell];
            if (!retained.Members.Select(member => member.Source.Reference).ToHashSet(FalloutFormKeyComparer.Instance)
                .SetEquals(actual.References.Select(row => row.Reference)))
                throw new InvalidDataException("Actual CELL insertion reader omitted a winning source member.");
            _cells[cell] = retained with { InitialInsertionsReturned = true, Revision = Next() };
            Set(index, FalloutSourceCellLinkPhase.LocalReturned); return _cells[cell];
        }
        catch (Exception error) { Fail(index, error); throw; }
    }

    // Canonical runtime actors have real source constructors, not placed ACHR
    // rows. Their insertion uses the same source head and ParentCELL prefix.
    internal void InsertRuntimeActor(FalloutCombatActorIdentity actor, FalloutFormKey? previousCell,
        FalloutFormKey cell, Action publishActualParentCell, string caller)
    {
        Require(); RequireNativePublication(); ArgumentNullException.ThrowIfNull(publishActualParentCell); actor.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        if (!actor.EnginePlayer || _runtimeActor is null || _runtimeActor(actor.Reference) != actor)
            throw new InvalidDataException("Runtime CELL insertion changed the canonical source Player factory.");
        _busy = true; var index = Enter("canonical-runtime-actor-CELL-insertion", caller, actor.Reference, previousCell, cell);
        try
        {
            var destination = RequireInitial(cell);
            if ((destination.Source.Flags & 0x400) != 0)
                throw new NotSupportedException("Canonical Player persistent CELL insertion requires its distinct source extra-parent consumer.");
            if (previousCell is { } old)
            {
                _ = RequireInitial(old); RemoveLink(old, actor.Reference);
                Set(index, FalloutSourceCellLinkPhase.PreviousUnlinked);
            }
            else if (_cells.Values.Any(value => value.OrderedReferences.Any(member => member.Reference == actor.Reference)))
                throw new InvalidDataException("Canonical null ParentCELL retains a current linked Player.");
            destination = _cells[cell];
            var membership = _membership = checked(_membership + 1);
            _cells[cell] = destination with
            {
                RuntimeActors = [new(actor, membership, caller), .. (destination.RuntimeActors ?? [])],
                Revision = Next()
            };
            Set(index, FalloutSourceCellLinkPhase.HeadInserted);
            publishActualParentCell(); Set(index, FalloutSourceCellLinkPhase.ParentPublished);
            Set(index, FalloutSourceCellLinkPhase.LocalReturned);
        }
        catch (Exception error) { Fail(index, error); throw; }
        finally { _busy = false; }
    }

    internal void RequireRuntimeActorParent(FalloutCombatActorIdentity actor, FalloutFormKey? currentCell)
    {
        Require(); actor.Validate();
        var memberships = _cells.Values.SelectMany(list => (list.RuntimeActors ?? [])
            .Where(member => member.Source.Reference == actor.Reference).Select(member => (list.Source.Cell, Member: member))).ToArray();
        if (currentCell is null ? memberships.Length != 0 : memberships.Length != 1 ||
            memberships[0].Cell != currentCell || memberships[0].Member.Source != actor)
            throw new InvalidDataException("Actual canonical Player ParentCELL disagrees with its retained source linked membership.");
    }

    internal FalloutFormKey? RuntimeActorParent(FalloutCombatActorIdentity actor)
    {
        Require(); actor.Validate();
        var cells = _cells.Values.Where(list => (list.RuntimeActors ?? []).Any(member => member.Source == actor)).ToArray();
        if (cells.Length > 1) throw new InvalidDataException("Canonical Player has more than one current source ParentCELL.");
        return cells.SingleOrDefault()?.Source.Cell;
    }

    internal FalloutSourceCellLinkedList Read(FalloutFormKey cell)
    {
        Require(); RequireNativePublication(); _busy = true;
        try { return Copy(RequireInitial(cell)); }
        finally { _busy = false; }
    }

    internal void Insert(FalloutFormKey reference, FalloutFormKey? previousCell, FalloutFormKey cell,
        Action publishActualParentCell, string owner)
    {
        Require(); RequireNativePublication(); ArgumentNullException.ThrowIfNull(publishActualParentCell); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        _busy = true; var index = Enter("ordinary-CELL-insertion", owner, reference, previousCell, cell);
        try
        {
            var destination = RequireInitial(cell);
            if ((destination.Source.Flags & 0x400) != 0)
                throw new NotSupportedException("Persistent CELL insertion has an independent source parent/extra/process consumer.");
            var row = _reference(reference);
            if (previousCell is { } old)
            {
                _ = RequireInitial(old);
                RemoveLink(old, reference); Set(index, FalloutSourceCellLinkPhase.PreviousUnlinked);
            }
            else if (_cells.Values.Any(value => value.Members.Any(member => member.Source.Reference == reference)))
                throw new InvalidDataException("Null ParentCELL insertion retained another current source link.");
            InsertHead(cell, row); Set(index, FalloutSourceCellLinkPhase.HeadInserted);
            publishActualParentCell(); Set(index, FalloutSourceCellLinkPhase.ParentPublished);
            // Only this linked-list/setter prefix returned. The original source
            // transfer's other scene/process/native consumers remain independent.
            Set(index, FalloutSourceCellLinkPhase.LocalReturned);
        }
        catch (Exception error) { Fail(index, error); throw; }
        finally { _busy = false; }
    }

    internal void Unlink(FalloutFormKey cell, FalloutFormKey reference, string owner)
    {
        Require(); RequireNativePublication(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        _busy = true; var index = Enter("source-CELL-unlink", owner, reference, cell, cell);
        try
        {
            _ = RequireInitial(cell); RemoveLink(cell, reference);
            Set(index, FalloutSourceCellLinkPhase.PreviousUnlinked);
            Set(index, FalloutSourceCellLinkPhase.LocalReturned);
        }
        catch (Exception error) { Fail(index, error); throw; }
        finally { _busy = false; }
    }

    private void InsertHead(FalloutFormKey cell, FalloutCellProcessReference row)
    {
        var list = _cells[cell];
        var members = list.Members.Where(member => member.Source.Reference != row.Reference).ToArray();
        if (_cells.Values.Any(other => other.Source.Cell != cell && other.OrderedReferences.Any(member => member.Reference == row.Reference)))
            throw new InvalidDataException("CELL head insertion retained another linked membership.");
        _cells[cell] = list with { Members = [new(row, _membership = checked(_membership + 1)), .. members], Revision = Next() };
    }
    private void RemoveLink(FalloutFormKey cell, FalloutFormKey reference)
    {
        var list = _cells[cell];
        if (!list.OrderedReferences.Any(member => member.Reference == reference))
            throw new InvalidDataException("Actual ParentCELL unlink has no matching linked reference.");
        _cells[cell] = list with
        {
            Members = list.Members.Where(member => member.Source.Reference != reference).ToArray(),
            RuntimeActors = (list.RuntimeActors ?? []).Where(member => member.Source.Reference != reference).ToArray(),
            Revision = Next()
        };
    }

    internal FalloutSourceCellReferenceLinksSnapshot Capture()
    {
        Require(); RequireNativePublication();
        var result = new FalloutSourceCellReferenceLinksSnapshot(Schema, _source, _stack, _process, _sequence, _membership,
            _cells.Values.OrderBy(value => value.Revision).Select(Copy).ToArray(), _attempts.ToArray(), _cold);
        Validate(result); return result;
    }
    private void Restore(FalloutSourceCellReferenceLinksSnapshot saved)
    {
        Validate(saved);
        if (saved.Source != _source || saved.Stack != _stack || saved.CapturedProcess == _process)
            throw new InvalidDataException("Cold CELL links changed the actual selected source/process lifetime.");
        foreach (var list in saved.Cells)
        {
            var actual = _cell(list.Source.Cell);
            if (actual.Source != list.Source || actual.GraphSha256 != list.GraphSha256 ||
                list.Members.Any(member => _reference(member.Source.Reference) != member.Source) ||
                (list.RuntimeActors ?? []).Any(member => _runtimeActor is null || _runtimeActor(member.Source.Reference) != member.Source))
                throw new InvalidDataException("Cold linked membership changed its immutable source bytes/ancestry.");
            if (_ingestion is IFalloutSourceOwnedCellReferenceIngestion sourceOwner)
                sourceOwner.RequireEvidence(list.InitialEvidence ?? throw new InvalidDataException("Cold product CELL list omitted loader source evidence."), actual);
            _cells.Add(list.Source.Cell, Copy(list));
        }
        _sequence = saved.Sequence; _membership = saved.Membership; _attempts.AddRange(saved.Attempts);
        _cold = new(saved.CapturedProcess, _process, Next());
    }
    internal static void Validate(FalloutSourceCellReferenceLinksSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Source is null) throw new InvalidDataException("Saved CELL links omitted their selected source.");
        saved.Source.Validate();
        if (saved.Schema != Schema || string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty ||
            saved.Sequence is < 0 or long.MaxValue || saved.Membership is < 0 or long.MaxValue || saved.Cells is null || saved.Attempts is null ||
            saved.Cells.Any(value => value is null || value.Source is null || value.Members is null ||
                value.Members.Any(member => member is null || member.Source is null) ||
                (value.RuntimeActors ?? []).Any(member => member is null || member.Source is null)) ||
            saved.Cells.Select(value => value.Source.Cell).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Cells.Count ||
            saved.Cells.Select(value => value.Revision).Distinct().Count() != saved.Cells.Count ||
            saved.Cells.SelectMany(value => value.OrderedReferences).Select(member => member.Membership).Distinct().Count() != saved.Cells.Sum(value => value.Members.Count + (value.RuntimeActors?.Count ?? 0)) ||
            saved.Cells.SelectMany(value => value.OrderedReferences).Select(member => member.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Cells.Sum(value => value.Members.Count + (value.RuntimeActors?.Count ?? 0)) ||
            saved.Attempts.Any(value => value is null || value.Identity < 1 || value.Identity > value.Changed || value.Changed > saved.Sequence ||
                string.IsNullOrWhiteSpace(value.Operation) || string.IsNullOrWhiteSpace(value.Owner) || !Enum.IsDefined(value.Phase) ||
                (value.Phase == FalloutSourceCellLinkPhase.Failed) != (value.Failure is not null) ||
                (value.FailedAtPhase is not null) != (value.Failure is not null) ||
                value.FailedAtPhase is { } prefix && (!Enum.IsDefined(prefix) || prefix == FalloutSourceCellLinkPhase.Failed) ||
                value.Failure is not null && string.IsNullOrWhiteSpace(value.Failure)) ||
            saved.Attempts.Select(value => value.Identity).Distinct().Count() != saved.Attempts.Count)
            throw new InvalidDataException("Saved CELL links lost their current order/membership/entered prefix.");
        foreach (var list in saved.Cells)
        {
            FalloutCellExtraProcessState.RequireCell(list.Source, list.Source.Cell);
            if (!FalloutAdvancementRuntimeReceipt.Digest(list.GraphSha256) || list.Revision < 1 || list.Revision > saved.Sequence ||
                list.OrderedReferences.Any(member => member.Membership < 1 || member.Membership > saved.Membership) ||
                (list.RuntimeActors ?? []).Any(member => member is null || member.Source is null || !member.Source.EnginePlayer || string.IsNullOrWhiteSpace(member.Caller)))
                throw new InvalidDataException("Saved CELL linked list has an invalid member/source revision.");
            foreach (var member in list.RuntimeActors ?? []) member.Source.Validate();
            if (list.InitialEvidence is { } evidence)
            {
                FalloutSourceCellReferenceIngestion.Validate(evidence);
                if (evidence.Cell != list.Source || evidence.GraphSha256 != list.GraphSha256 || evidence.Stack != saved.Stack)
                    throw new InvalidDataException("Saved CELL loader evidence has a different linked owner.");
            }
        }
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Cold CELL linked list lost its actual new process handoff.");
    }
    private static FalloutSourceCellLinkedList Copy(FalloutSourceCellLinkedList list) => list with
    {
        Members = list.Members.ToArray(),
        RuntimeActors = (list.RuntimeActors ?? []).ToArray(),
        InitialEvidence = list.InitialEvidence is { } evidence ?
            evidence with { Inputs = evidence.Inputs.ToArray() } : null
    };
    private long Next() => _sequence = checked(_sequence + 1);
    private int Enter(string operation, string owner, FalloutFormKey? reference, FalloutFormKey? previous, FalloutFormKey cell)
    {
        var sequence = Next(); _attempts.Add(new(sequence, operation, owner, reference, previous, cell,
            FalloutSourceCellLinkPhase.Entered, sequence, null)); return _attempts.Count - 1;
    }
    private void Set(int index, FalloutSourceCellLinkPhase phase) => _attempts[index] = _attempts[index] with { Phase = phase, Changed = Next() };
    private void Fail(int index, Exception error) => _attempts[index] = _attempts[index] with
    { FailedAtPhase = _attempts[index].Phase, Phase = FalloutSourceCellLinkPhase.Failed, Changed = Next(), Failure = error.ToString() };
    private void RequireNativePublication()
    {
        if (_ingestion is IFalloutSourceOwnedCellReferenceIngestion && _nativeOwner is null)
            throw new InvalidOperationException("Mutable source CELL insertion requires its actual native-thread publication.");
    }
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId || _busy)
            throw new InvalidOperationException("Source CELL links changed their actual owner thread or reentered an in-flight mutation.");
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_thread != Environment.CurrentManagedThreadId || _busy)
            throw new InvalidOperationException("Source CELL linked owner cannot retire an entered/foreign-thread writer.");
        _disposed = true; _cells.Clear();
    }
}
