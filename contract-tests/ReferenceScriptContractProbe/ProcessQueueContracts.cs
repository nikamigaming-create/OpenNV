using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class ProcessQueueContracts
{
    private const string Stack = "authored-actual-queue-CELL-selection";
    private static readonly FalloutFormKey Cell = Key(0x901), Npc = Key(0x902), Creature = Key(0x903), Other = Key(0x904);
    internal static void Run()
    {
        foreach (var image in new[] { "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" })
        {
            var declaration = FalloutActorProcessQueueDeclaration.ForExecutable(image);
            QueueMap(declaration); ActualTaskLifetime(declaration); CellCount(declaration); PendingOrder(declaration); KnownNull3D(declaration);
        }
        Console.WriteLine("OPENNV_PROCESS_QUEUE_CONTRACT_PASS exactMapValue=true callerAndConsumersRetained=true cancellationNotCompletion=true actualTaskReturned=true nativeOwningThread=true failedRetirementPrefix=true loaderRejectsCreationDuringRetirement=true CELLFirstCreationBeforeWalk=true rawDisabledLowGuard=true processUnknownRefused=true storedCountWrap=true pendingUniqueOrder=true independentSourceFields=true retirementEpoch=true coldNewProcessNoReplay=true originalDispatchAndNativeExecution=unexecuted");
    }
    private static void QueueMap(FalloutActorProcessQueueDeclaration declaration)
    {
        using var queue = new FalloutQueuedReferences(declaration, Stack, Queued);
        Require(!queue.Membership(Npc).Require(), "An empty source queue inherited scene/cohort membership.");
        var refused = queue.Request(Npc, 0, Inputs() with { MainForced = new(true, "authored-source-main"),
            MainPermitsForcedQueue = new(false, "authored-source-secondary-bit"), SourceBaseExcluded = new(null, "unconsumed") });
        Require(refused.Disposition == FalloutQueuedReferenceRequestDisposition.SourceRefused && !queue.Membership(Npc).Require(),
            "Original factory refusal consumed a later field or constructed a map value.");
        var first = queue.Request(Npc, 0, Inputs()); var identity = first.Identity!.Value;
        var same = queue.Request(Npc, 0, Inputs());
        Require(same.Identity == identity && !same.PriorityChanged && queue.Membership(Npc).Require(), "A duplicate replaced the exact source map object.");
        var read = queue.EnterConsumer(identity, FalloutQueuedReferenceConsumerKind.Read, "read"); queue.ReturnConsumer(identity, read, "read");
        var assembly = queue.EnterConsumer(identity, FalloutQueuedReferenceConsumerKind.Assembly, "assembly");
        queue.RequestCancellation(identity, "actual-cancel");
        var cancel = queue.EnterConsumer(identity, FalloutQueuedReferenceConsumerKind.Cancellation, "cancel");
        Reject(() => queue.ReturnConsumer(identity, cancel, "cancel"));
        Require(queue.Membership(Npc).Require(), "Cancellation retired a still-held native assembly.");
        queue.ReturnConsumer(identity, assembly, "assembly"); queue.ReturnConsumer(identity, cancel, "cancel");
        queue.ConsumersRetired(identity, "children-returned");
        Require(!queue.RemoveMatched(Npc, Guid.NewGuid(), "stale-job") && queue.Membership(Npc).Require(), "A stale job removed a different map value.");
        Require(queue.RemoveMatched(Npc, identity, "actual-value"), "The actual returned source value was not removed.");
        queue.ReleaseCaller(identity, "caller-returned");
        var saved = RoundTrip(queue.Capture()); FalloutQueuedReferences.Validate(saved);
        using var cold = new FalloutQueuedReferences(declaration, Stack, Queued, saved);
        Require(cold.Capture().CapturedProcess != saved.CapturedProcess && cold.Capture().ColdHandoff?.PreviousProcess == saved.CapturedProcess &&
            !cold.Membership(Npc).Require(), "Cold restoration replayed work or reused a process/map owner.");
        Reject(() => new FalloutQueuedReferences(declaration, Stack, key => Queued(key) with { BaseSha256 = Digest("foreign winner") }, saved));
        Reject(() => FalloutQueuedReferences.Validate(saved with { Objects = saved.Objects.Select(item => item with
        { Consumers = item.Consumers.Select(consumer => consumer.Kind == FalloutQueuedReferenceConsumerKind.Cancellation ?
            consumer with { Changed = consumer.Entered } : consumer).ToArray() }).ToArray() }));

        var retiring = new FalloutQueuedReferences(declaration, Stack, Queued);
        var held = retiring.Request(Creature, 0, Inputs()).Identity!.Value;
        Reject(retiring.Dispose); Reject(() => retiring.Request(Other, 0, Inputs()));
        retiring.RetireUnstarted(held, "actual-never-entered-read-cancel"); retiring.Dispose();
        var priority = new FalloutQueuedReferences(declaration, Stack, Queued);
        var priorityIdentity = priority.Request(Npc, 0, Inputs()).Identity!.Value;
        Reject(() => priority.Request(Npc, 256, Inputs()));
        Require(priority.SaveBlocker is not null, "An absent original child/task-manager priority consumer became a byte-only success.");
        priority.RetireUnstarted(priorityIdentity, "actual-unstarted-priority-failure-retirement"); priority.Dispose();
    }
    private static void ActualTaskLifetime(FalloutActorProcessQueueDeclaration declaration)
    {
        var queue = new FalloutQueuedReferences(declaration, Stack, Queued);
        var identity = queue.Request(Npc, 0, Inputs()).Identity!.Value;
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeReturns = 0;
        var registry = new FalloutQueuedReferenceWorkRegistry(queue);
        var work = registry.Begin(identity, "authored-real-read-invocation", _ => completion.Task,
            () => { if (++nativeReturns == 1) throw new IOException("authored-native-child-release-failure"); }, default);
        _ = registry.StopAndReadTasks(); Reject(registry.RetireReturnedWork);
        Require(nativeReturns == 0 && queue.Membership(Npc).Value == true, "Token cancellation completed the still-running actual Task.");
        completion.SetResult(41); Require(work.ReadTask.GetAwaiter().GetResult() == 41, "Actual owned read did not return its result.");
        Task.Run(() => Reject(() => work.BeginAssembly())).GetAwaiter().GetResult();
        Reject(registry.RetireReturnedWork);
        Require(nativeReturns == 1 && !work.Retired && queue.SaveBlocker is not null, "A failed native child was discarded or became cold-ready.");
        registry.RetireReturnedWork();
        Require(nativeReturns == 2 && work.Retired && !queue.Membership(Npc).Require() && queue.SaveBlocker is not null,
            "Native child retry lost its true consumed prefix or cleared the original failure.");
        registry.Dispose(); queue.Dispose();
    }
    private static void CellCount(FalloutActorProcessQueueDeclaration declaration)
    {
        var callbacks = new List<FalloutFormKey>(); FalloutCellExtraProcessState? extra = null;
        var list = new FalloutCellProcessReferenceList(CellSource(), 1, [Child(Npc, true, FalloutDetectionProcessLevel.Low),
            Child(Creature, true, FalloutDetectionProcessLevel.MiddleHigh), Child(Other, null, null, disabled: true)], "authored-current-source-list");
        extra = new(declaration, Stack, _ => CellSource(), _ => list, (child, _, _) =>
        {
            var state = JsonSerializer.SerializeToElement(extra!.State).GetProperty("cells")[0];
            Require(state.GetProperty("Present").GetBoolean() && state.GetProperty("Count").GetUInt32() == 0,
                "First source CELL count was stored before its actual reference consumer."); callbacks.Add(child.Source.Reference);
        });
        _ = extra.Change(Cell, false, "absent-decrease");
        Require(callbacks.Count == 0 && !extra.ReadPresence(Cell).Require(), "Absent decrement created an extra or walked references.");
        _ = extra.Change(Cell, true, "actual-first-increase");
        Require(callbacks.SequenceEqual([Npc]) && extra.Capture().Cells.Single().Count == 1,
            "CELL walk changed raw-disabled/current-Low guards or source order.");
        _ = extra.Change(Cell, true, "existing-increase"); _ = extra.Change(Cell, false, "decrease");
        Require(callbacks.Count == 1 && extra.Capture().Cells.Single().Count == 1, "An existing extra replayed its initial reference walk.");
        var saved = RoundTrip(extra.Capture()); FalloutCellExtraProcessState.Validate(saved);
        using var cold = new FalloutCellExtraProcessState(declaration, Stack, _ => CellSource(),
            _ => throw new InvalidDataException("Cold load replayed initial list."), (_, _, _) => throw new InvalidDataException("Cold load replayed reevaluation."), saved);
        Require(cold.Capture().CapturedProcess != saved.CapturedProcess && cold.ReadPresence(Cell).Require(), "Cold count lost a real owner or replayed its source writer.");
        extra.RemoveForSourceRelease(Cell, "actual-CELL-release"); Require(!extra.ReadPresence(Cell).Require(), "CELL release retained its stored-zero extra.");
        extra.Dispose();
        Require(FalloutActorProcessQueueDeclaration.StoreCount(0, false) == uint.MaxValue &&
            FalloutActorProcessQueueDeclaration.StoreCount(uint.MaxValue, true) == 0, "Original UInt32 count arithmetic was clamped.");

        var unknown = new FalloutCellExtraProcessState(declaration, Stack, _ => CellSource(), _ => list with
        { References = [Child(Npc, null, null)] }, (_, _, _) => throw new InvalidDataException("Unknown process entered callback."));
        Reject(() => unknown.Change(Cell, true, "actual-unowned-process")); var failed = RoundTrip(unknown.Capture());
        FalloutCellExtraProcessState.Validate(failed);
        Require(failed.Cells.Single() is { Present: true, Count: 0, Failure: not null }, "Unknown process lost the created-before-walk source prefix.");
        Reject(() => FalloutCellExtraProcessState.Validate(failed with { Invocations = failed.Invocations.Select(call => call with { NextReference = 1 }).ToArray() }));
        Reject(unknown.Dispose);
    }
    private static void PendingOrder(FalloutActorProcessQueueDeclaration declaration)
    {
        var observations = 0;
        using var state = new FalloutProcessReevaluationState(declaration, Stack, Identity, (actor, epoch) =>
        {
            observations++;
            return new(Identity(actor), epoch, FalloutDetectionProcessLevel.Low, Election(actor, epoch),
                new(2, "actual-neutral-life"), new(null, "unconsumed-common"), new(null, "unconsumed-flags"),
                new(null, "unconsumed-player-count"), "authored-source-request");
        }, _ => FalloutDetectionProcessLevel.High);
        state.Construct(Npc, 1); Require(state.ReadPendingReferenceFlag(Npc, 1).Value is null, "An empty manager list fabricated an Actor reference flag.");
        var first = state.Request(Npc, 1, "actual-manager-request"); var duplicate = state.Request(Npc, 1, "actual-duplicate-request");
        Require(first.Enqueued && first.LifeGuard == true && first.TierGuard is null && observations == 1 &&
            duplicate.Phase == FalloutProcessReevaluationPhase.AlreadyPending && state.Pending.SequenceEqual([Npc]),
            "Original request order lost unique insertion or consumed short-circuited source inputs.");
        var saved = RoundTrip(state.Capture()); FalloutProcessReevaluationState.Validate(saved);
        Reject(() => FalloutProcessReevaluationState.Validate(saved with { Invocations = saved.Invocations.Select(call => call.Identity == first.Identity ?
            call with { Desired = null } : call).ToArray() }));
        Reject(() => FalloutProcessReevaluationState.Validate(saved with { Invocations = saved.Invocations.Select(call => call.Identity == duplicate.Identity ?
            call with { LifeGuard = false } : call).ToArray() }));
        Reject(() => state.RequireActorRetirement(Npc, 1));
        using var empty = new FalloutProcessReevaluationState(declaration, Stack, Identity, (_, _) => throw new InvalidDataException("Unused"), _ => throw new InvalidDataException("Unused"));
        empty.Construct(Creature, 1); empty.RebindProcess(Creature, 1, 2); empty.RequireActorRetirement(Creature, 2);
        empty.RetireActor(Creature, 2, 3, "actual-invalidated-process");
        Require(empty.Capture().Actors.Single() is { Epoch: 3, Retired: true }, "Queue actor retirement kept its stale process epoch.");
    }
    private static void KnownNull3D(FalloutActorProcessQueueDeclaration declaration)
    {
        var player = Key(0x14);
        bool Actor(FalloutFormKey key) => key == player || key == Npc;
        FalloutCombatActorIdentity Source(FalloutFormKey key) => key == player ?
            new(key, Key(7), "ENGINE_PLAYER", 0, declaration.ExecutableSha256, "NPC_", 0, Digest("player-base"), true) : Identity(key);
        NotSupportedException Missing() => new("Authored source fixture has no native/perception producer.");
        using var perception = new FalloutActorPerception(FalloutActorPerceptionDeclaration.ForExecutable(declaration.ExecutableSha256),
            Stack, player, 3, Actor, Source,
            new(_ => throw Missing(), (_, _, _, _) => throw Missing(), _ => throw Missing(), _ => throw Missing(), () => throw Missing(), () => throw Missing()));
        perception.Construct(Npc);
        using var common = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(declaration.ExecutableSha256),
            Stack, Source, actor => new(actor, null, null, null, null, null, null, null, null, null, null, null,
                new Dictionary<string, FalloutActorValue> { ["health"] = new(37, 0, 0, 0) }),
            _ => throw new InvalidDataException("Known-null source 3D entered a skeleton/BPTD getter."));
        common.BindSource3DInitializer(actor => new(actor, new(false, "authored-actual-source-3D-null"), null, "actual-getter-return"));
        common.Construct(player, 1, FalloutDetectionProcessLevel.High); common.Construct(Npc, 1, FalloutDetectionProcessLevel.Low);
        using var manager = new FalloutActorProcessManager(FalloutActorProcessDeclaration.ForExecutable(declaration.ExecutableSha256),
            Stack, player, perception, Source, Actor,
            new((_, _) => throw Missing(), (_, _) => throw Missing(), (_, _) => throw Missing(), () => throw Missing(),
                () => throw Missing(), _ => throw Missing(), _ => throw Missing(), _ => throw Missing(), (_, _, _) => throw Missing(),
                _ => throw Missing(), () => throw Missing(), _ => throw Missing(), common.Copy, common.RetireOld, common.InitializeNew,
                actor => new(actor, new(true, "authored-source-registration-argument"), new(false, "authored-normal-mode"), "actual-constructor")));
        manager.Construct(Npc); manager.EnsureHigh(Npc, 1, "actual-source-common-factory");
        var saved = RoundTrip(common.Capture()); FalloutActorProcessCommonState.Validate(saved);
        Require(saved.Current.Single(actor => actor.Source.Reference == Npc) is
            { Phase: FalloutProcessCommonPhase.Initialized, Body: null, Source3D.Value: false } &&
            saved.Transfer is { Initialized: true }, "Original known-null source 3D was confused with an unavailable body provider.");
        Reject(() => FalloutActorProcessCommonState.Validate(saved with { Current = saved.Current.Select(actor => actor.Source.Reference == Npc ?
            actor with { Source3D = null } : actor).ToArray() }));
        using var cold = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(declaration.ExecutableSha256),
            Stack, Source, _ => throw new InvalidDataException("Cold state replayed a common copy."), _ => throw Missing(), saved);
        Require(cold.Capture().CapturedProcess != saved.CapturedProcess, "Known-null source initializer reused its old process lifetime.");
    }
    private static FalloutQueuedReferenceFactoryInputs Inputs() => new(new(false, "actual-main"), new(null, "unconsumed-secondary"),
        new(false, "actual-base"), new(true, "actual-Actor-class"), new(0, "actual-Actor-constructor-word"), "authored-source-factory");
    private static FalloutActorProcessElection Election(FalloutFormKey actor, long epoch) => new(actor, epoch,
        new(0, "player-count"), new(false, "main"), new(false, "map"), new(0, "CELL-phase"), new(false, "Extra9"), "authored-election");
    private static FalloutCellProcessIdentity CellSource() => new(Cell, 0, Digest("source-CELL"), null, null);
    private static FalloutCellProcessListReference Child(FalloutFormKey actor, bool? process, FalloutDetectionProcessLevel? level, bool disabled = false)
    {
        var identity = Identity(actor);
        return new(new(actor, Cell, identity.ReferenceSignature, identity.ReferenceFlags, identity.ReferenceSha256, identity.Base, identity.BaseSha256),
            Cell, actor.ObjectId, disabled ? 0x800u : 0, identity, 1, level,
            new(process, "authored-actual-process-pointer"));
    }
    private static FalloutCombatActorIdentity Identity(FalloutFormKey actor) => new(actor, Key(actor.ObjectId + 0x100),
        actor == Creature ? "ACRE" : "ACHR", 0, Digest("reference:" + actor), actor == Creature ? "CREA" : "NPC_", 0, Digest("base:" + actor), false);
    private static FalloutQueuedReferenceSource Queued(FalloutFormKey actor)
    {
        var identity = Identity(actor);
        return new(actor, identity.ReferenceSignature, identity.ReferenceSha256, identity.Base, identity.BaseSignature,
            identity.BaseSha256, actor == Creature ? FalloutQueuedReferenceKind.Creature : FalloutQueuedReferenceKind.Character, false);
    }
    private static FalloutFormKey Key(uint id) => new("Authored.esm", id);
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ?? throw new InvalidDataException("Authored cold state disappeared.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or AggregateException) { return; }
        throw new InvalidDataException("A malformed/unfinished actual source owner was admitted.");
    }
}
