using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorProcessQueueDeclaration? _processQueueDeclaration;
    private string? _processQueueStack;
    private FalloutQueuedReferences? _queuedReferences;
    private FalloutQueuedReferenceWorkRegistry? _queuedReferenceWork;
    private FalloutActorLoaderFields? _actorLoaderFields;
    private FalloutProcessReevaluationState? _processReevaluation;
    private FalloutCellExtraProcessState? _cellExtraProcess;
    private FalloutProcessQueueSnapshots? _processQueueRestore;
    private readonly Dictionary<FalloutFormKey, (string Graph, long Revision,
        IReadOnlyDictionary<FalloutFormKey, long> Membership)> _currentCellReferenceLists = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, string> _unownedCellReferenceLists = new(FalloutFormKeyComparer.Instance);
    private long _currentCellReferenceRevision, _currentCellReferenceMembership;

    internal bool SourceProcessQueuesConfigured => _queuedReferences is not null && _queuedReferenceWork is not null && _actorLoaderFields is not null &&
        _processReevaluation is not null && _cellExtraProcess is not null && _sourceQueuePriority is not null;
    private FalloutQueuedReferences QueuedReferences => _queuedReferences ??
        throw new NotSupportedException("Actual selected source queued-reference loader is absent.");
    private FalloutActorLoaderFields ActorLoaderFields => _actorLoaderFields ??
        throw new NotSupportedException("Actual source Actor loader word owner is absent.");
    private FalloutProcessReevaluationState ProcessReevaluation => _processReevaluation ??
        throw new NotSupportedException("Original manager pending process request owner is absent.");
    private FalloutCellExtraProcessState CellExtraProcess => _cellExtraProcess ??
        throw new NotSupportedException("Original counted CELL extra process owner is absent.");
    internal object? SourceProcessQueueState => _queuedReferences is null && _actorLoaderFields is null &&
        _processReevaluation is null && _cellExtraProcess is null ? null : new
    {
        loader = _queuedReferences?.State, reevaluation = _processReevaluation?.State,
        cells = _cellExtraProcess?.State, mainFrame = SourceMainFrameState, unownedCurrentLists = _unownedCellReferenceLists.ToArray()
    };
    internal string? SourceProcessQueueSaveBlocker => !SourceProcessQueuesConfigured ? "actual-source-process-queue-owner-absent" :
        QueuedReferences.SaveBlocker ?? ActorLoaderFields.SaveBlocker ?? ProcessReevaluation.SaveBlocker ?? CellExtraProcess.SaveBlocker ??
        _sourceQueuePriority?.SaveBlocker ?? _unownedCellReferenceLists.Values.FirstOrDefault();
    internal string? SourceProcessQueueRuntimeBoundary => !SourceProcessQueuesConfigured ? "actual-source-process-queue-owner-absent" :
        QueuedReferences.TaskPriorities.RuntimeBoundary ?? ProcessReevaluation.RuntimeBoundary;

    internal void ConfigureSourceProcessQueues(FalloutActorProcessQueueDeclaration declaration, string stack,
        FalloutProcessQueueSnapshots? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); declaration.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (restore is not null)
        {
            ValidateSourceProcessQueueShape(restore);
            if (restore.Loader.Stack != stack || restore.Loader.Contract != declaration.Contract)
                throw new InvalidDataException("Current process queue continuation changed its selected source binding.");
        }
        if (SourceProcessQueuesConfigured)
        {
            RequireSourceProcessQueueBinding(declaration, stack, restore); return;
        }
        if (_queuedReferences is not null || _queuedReferenceWork is not null || _actorLoaderFields is not null || _processReevaluation is not null || _cellExtraProcess is not null ||
            _processRuntimeDeclaration?.ExecutableSha256 != declaration.ExecutableSha256 || _processRuntimeStack != stack ||
            !ActorProcessesConfigured || !CellProcessesConfigured)
            throw new InvalidDataException("Source loader/CELL/pending request owners lost their actual complete current actor process factory.");
        FalloutQueuedReferences? loader = null; FalloutActorLoaderFields? fields = null;
        FalloutProcessReevaluationState? pending = null; FalloutCellExtraProcessState? cells = null;
        try
        {
            ConstructSourceFrameDispatch(stack, restore?.FrameDispatch);
            loader = new(declaration, stack, ReadQueuedReferenceSource, restore?.Loader);
            fields = new(declaration, stack, ReadCombatActorIdentity, restore?.ActorFields);
            pending = new(declaration, stack, ReadCombatActorIdentity, ReadActualProcessReevaluation,
                ActorProcesses.Reevaluate, restore?.Reevaluation);
            cells = new(declaration, stack, CellProcesses.ReadSourceQueueCell, ReadActualCurrentCellReferences,
                ReevaluateCurrentCellChild, restore?.Cells);
            var actors = ActorPerception.ConstructedSourceActors;
            if (restore is null)
                foreach (var actor in actors)
                {
                    fields.Construct(actor); pending.Construct(actor, ActorProcesses.Read(actor).Epoch);
                }
            else
            {
                fields.RequireActors(actors); pending.RequireActors(ActorProcesses.Capture().Actors);
            }
            if (restore is not null) RestoreSourceCurrentCellLists(restore.CurrentLists);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            foreach (var owner in new IDisposable?[] { cells, pending, fields, loader })
                try { owner?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { RetireSourceFrameDispatch(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 1)
            {
                _queuedReferences = loader; _actorLoaderFields = fields; _processReevaluation = pending; _cellExtraProcess = cells;
                _processQueueDeclaration = declaration; _processQueueStack = stack; _processQueueRestore = restore;
                throw new AggregateException("Source process queue setup retains original failure and every still-owned child.", failures);
            }
            throw;
        }
        _queuedReferences = loader; _actorLoaderFields = fields; _processReevaluation = pending; _cellExtraProcess = cells;
        _queuedReferenceWork = new(loader ?? throw new InvalidOperationException("Actual source queued loader constructor did not return."));
        _processQueueDeclaration = declaration; _processQueueStack = stack; _processQueueRestore = restore;
    }
    internal void RequireSourceProcessQueueBinding(FalloutActorProcessQueueDeclaration declaration, string stack,
        FalloutProcessQueueSnapshots? restore)
    {
        if (!SourceProcessQueuesConfigured || declaration != _processQueueDeclaration || stack != _processQueueStack ||
            !ReferenceEquals(restore, _processQueueRestore))
            throw new InvalidDataException("Attached process queues differ from their selected current/cold original source owner.");
    }
    private void ConstructSourceProcessQueueActor(FalloutReferenceInstance instance)
    {
        if (!SourceProcessQueuesConfigured || records.GetEffective(instance.Reference).Signature is not ("ACHR" or "ACRE")) return;
        ActorLoaderFields.Construct(instance.Reference);
        ProcessReevaluation.Construct(instance.Reference, ActorProcesses.Read(instance.Reference).Epoch);
    }
    internal FalloutActorProcessElection JoinSourceProcessQueues(FalloutActorProcessElection election)
    {
        if (!SourceProcessQueuesConfigured) return election;
        var extra = election.Actor == _enginePlayer && _currentPlayerProcessCell is null ?
            new FalloutActorProcessFact<bool>(null, "actual-current-Player-CELL-producer-absent") :
            ReadSourceCellExtraPresence(CurrentActorProcessCell(election.Actor));
        return election with { RegisteredProcessingTree = QueuedReferences.Membership(election.Actor), CellHasExtraProcessingOwner = extra };
    }
    private FalloutActorProcessFact<bool> ReadSourceCellExtraPresence(FalloutFormKey? cell)
    {
        if (cell is { } actual) CellProcesses.Construct(actual);
        return CellExtraProcess.ReadPresence(cell);
    }
    private FalloutFormKey? CurrentActorProcessCell(FalloutFormKey actor)
    {
        if (actor != _enginePlayer) return Placement(actor).Cell;
        return _currentPlayerProcessCell is { } read ? read() :
            throw new NotSupportedException("Actual current Player CELL producer is absent.");
    }
    internal FalloutQueuedReferenceRequest RequestSourceQueuedReference(FalloutFormKey reference, int priority, string owner)
    {
        var source = ReadQueuedReferenceSource(reference);
        var hasFlag = source.ReferenceSignature is "ACHR" or "ACRE" || source.EnginePlayer;
        var flags = hasFlag ? ActorLoaderFields.Read(reference) : new FalloutActorProcessFact<uint>(null,
            "source-reference-class-does-not-enter-Actor-loader-word");
        // Both independent flags come from the same actual source Main
        // word. Its window bit is consumed only on the source forced arm.
        return QueuedReferences.Request(reference, priority, new(ProcessRuntime.MainForcedProcessing,
            ProcessRuntime.MainPermitsForcedQueue,
            new(source.BaseSignature == "SCPT", "actual-source-base-SCPT-factory-early-return"),
            new(hasFlag, "actual-selected-source-Actor-reference-queue-word-guard"), flags, owner));
    }
    internal FalloutQueuedReferenceRead<T> BeginSourceQueuedRead<T>(Guid identity, string owner,
        Func<CancellationToken, Task<T>> read, Action retireEnteredNative, CancellationToken cancellation) =>
        (_queuedReferenceWork ?? throw new NotSupportedException("Actual source queued read registry is absent."))
            .Begin(identity, owner, read, retireEnteredNative, cancellation);
    internal IReadOnlyList<Task> StopActualQueuedSourceReads() =>
        (_queuedReferenceWork ?? throw new NotSupportedException("Actual source queued read registry is absent.")).StopAndReadTasks();
    internal void RetireReturnedActualQueuedSourceReads() =>
        (_queuedReferenceWork ?? throw new NotSupportedException("Actual source queued read registry is absent.")).RetireReturnedWork();
    private FalloutQueuedReferenceSource ReadQueuedReferenceSource(FalloutFormKey reference) =>
        FalloutQueuedReferenceSourceReader.Read(records, FalloutActorProcessQueueDeclaration.ForExecutable(
            _processRuntimeDeclaration?.ExecutableSha256 ?? throw new NotSupportedException("Actual selected process source is absent.")), reference);
    private FalloutProcessReevaluationObservation ReadActualProcessReevaluation(FalloutFormKey actor, long epoch)
    {
        var process = ActorProcesses.Read(actor);
        if (process.Epoch != epoch) throw new InvalidDataException("Actual process request observes a foreign current actor epoch.");
        return new(ReadCombatActorIdentity(actor), epoch, process.Level,
            process.Level is null ? null : ReadProcessElection(actor, epoch), ProcessRuntime.Life(actor),
            process.Level is null ? new(null, "actual-null-process-has-no-common-request-byte") : ProcessCommon.ReadSourceRequestFlags(actor, epoch),
            actor == _enginePlayer ? new(null, "actual-current-Player-reference-flags-producer-unbound") :
                new(ReadActualCurrentReferenceFlags(actor), "actual-current-reference-source-flags"), ProcessRuntime.PlayerTravelCounter,
            "actual-selected-manager-process-reevaluation");
    }
    private uint ReadActualCurrentReferenceFlags(FalloutFormKey actor)
    {
        if (actor == _enginePlayer)
            throw new NotSupportedException("Original current Player reference flags are distinct from its canonical source identity.");
        var instance = Get(actor); var flags = records.GetEffective(actor).Flags;
        flags = instance.Enabled ? flags & ~FalloutActorProcessQueueDeclaration.DisabledReferenceFlag :
            flags | FalloutActorProcessQueueDeclaration.DisabledReferenceFlag;
        if (instance.Deleted) flags |= FalloutActorProcessQueueDeclaration.DeletedReferenceFlag;
        if (_processReevaluation is not null && records.GetEffective(actor).Signature is "ACHR" or "ACRE" &&
            ProcessReevaluation.ReadPendingReferenceFlag(actor, ActorProcesses.Read(actor).Epoch).Value == true)
            flags |= FalloutActorProcessQueueDeclaration.PendingReferenceFlag;
        return flags;
    }
    private void ReevaluateCurrentCellChild(FalloutCellProcessListReference child, Guid invocation, string owner)
    {
        if (child.Actor is null || child.ProcessEpoch is not { } epoch || child.Level != FalloutDetectionProcessLevel.Low ||
            !child.HasProcess!.Require() || CurrentActorProcessCell(child.Actor.Reference) != child.CurrentCell)
            throw new InvalidDataException("Counted CELL consumer lost its exact current Low Actor reference.");
        _ = ProcessReevaluation.Request(child.Actor.Reference, epoch, owner + ":CELL-extra/" + invocation);
    }
    internal Guid ChangeSourceCellExtraProcess(FalloutFormKey cell, bool increase, string owner)
    {
        CellProcesses.Construct(cell); return CellExtraProcess.Change(cell, increase, owner);
    }
    internal void ReleaseSourceCellExtraProcess(FalloutFormKey cell, string owner)
    {
        CellProcesses.Construct(cell); CellExtraProcess.RemoveForSourceRelease(cell, owner);
    }
    private void RebindSourceProcessQueueActor(FalloutFormKey actor, long before, long after)
    {
        if (_processReevaluation is not null) ProcessReevaluation.RebindProcess(actor, before, after);
    }
    private void RequireSourceProcessQueueActorRetirement(FalloutFormKey actor, long before)
    {
        if (!SourceProcessQueuesConfigured) return;
        if (QueuedReferences.Membership(actor).Require())
            throw new NotSupportedException("Actual Actor retirement still owns source queued-reference loading work.");
        ProcessReevaluation.RequireActorRetirement(actor, before);
    }
    private void RetireSourceProcessQueueActor(FalloutFormKey actor, long before, long after)
    {
        if (!SourceProcessQueuesConfigured) return;
        var actual = ActorProcesses.Read(actor);
        if (!actual.Retired || actual.Epoch != after)
            throw new InvalidDataException("Queue Actor retirement preceded the actual process invalidation.");
        ProcessReevaluation.RetireActor(actor, before, after, "actual-source-Actor-retirement"); ActorLoaderFields.RetireActor(actor);
    }
    private void RetireSourceProcessQueues()
    {
        // All native read/assembly consumers retire before their C# provider.
        // An exception retains the failed owner and every dependent provider.
        _queuedReferenceWork?.Dispose(); _queuedReferenceWork = null;
        _queuedReferences?.Dispose(); _queuedReferences = null;
        RetireSourceFrameDispatch();
        _cellExtraProcess?.Dispose(); _cellExtraProcess = null;
        _processReevaluation?.Dispose(); _processReevaluation = null;
        _actorLoaderFields?.Dispose(); _actorLoaderFields = null;
        _currentCellReferenceLists.Clear(); _unownedCellReferenceLists.Clear();
        _processQueueDeclaration = null; _processQueueStack = null; _processQueueRestore = null;
    }
}
