using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunActorMoverFrameStores(FalloutPluginStack records, FalloutActorMoverSource source)
    {
        using var common = new PackageMutationFixture(records);
        var query = common.Common.ReadSceneQueries(common.Player, 1);
        FalloutActorMoverFrameInvocation? entered = null;
        var thread = Environment.CurrentManagedThreadId;
        var alive = true;
        var mover = new FalloutActorMoverState(source, common.Stack, Guid.NewGuid(), Guid.NewGuid(), Lifetime);
        mover.BindPlayerFrame(query.Actor, Guard);
        var invocation = Frame(1);
        Reject(() => mover.StorePlayerFrameDefaultVector(invocation));
        entered = invocation;
        var flags = mover.StorePlayerFrameFlags(invocation, 0);
        var pending = mover.Capture();
        Require(flags is { Returned: true, PreviousFlags: 0, RequestedWord: 0, Stores.Count: 1 } &&
            pending.PlayerFrame is { Sequence: 3, FlagCalls: 1, VectorCalls: 0, FlagStores: 1 } &&
            pending.Operations.Count == 0,
            "PlayerMover frame flags did not store the real word or impersonated a compiled package command.");
        Reject(() => mover.StorePlayerFrameFlags(invocation, 0));
        var vector = mover.StorePlayerFrameDefaultVector(invocation);
        entered = null;
        var complete = mover.Capture();
        Require(vector is { Returned: true, Stores.Count: 2 } &&
            vector.Stores.Select(store => store.Field).SequenceEqual([FalloutActorMoverField.PlayerVectorXY, FalloutActorMoverField.PlayerVectorZ]) &&
            complete.PlayerFrame is { Sequence: 7, FlagCalls: 1, VectorCalls: 1, FlagStores: 1, VectorXYStores: 1, VectorZStores: 1 } &&
            complete.Values.Player == FalloutPlayerMoverFields.Constructed && complete.Operations.Count == 0,
            "PlayerMover frame vector reordered XY/Z or fabricated the separate movement virtual's return.");
        var unchanged = JsonSerializer.Serialize(complete);
        Reject(() => mover.StorePlayerFrameDefaultVector(invocation));
        Reject(() => mover.StorePlayerFrameFlags(invocation with { PlayerProcess = Guid.NewGuid() }, 0));
        Reject(() => mover.StorePlayerFrameFlags(invocation with { Common = query with { Epoch = 2 } }, 0));
        Require(JsonSerializer.Serialize(mover.Capture()) == unchanged,
            "Rejected PlayerMover replay/foreign owner changed actual frame stores.");
        for (var ordinal = 2; ordinal <= 12; ++ordinal)
        {
            entered = Frame(ordinal);
            _ = mover.StorePlayerFrameFlags(entered, 0);
            _ = mover.StorePlayerFrameDefaultVector(entered);
            entered = null;
        }
        var saved = RoundTripPackage(mover.Capture());
        Require(saved.PlayerFrame is { Sequence: 84, FlagCalls: 12, VectorCalls: 12, Flags.CallOrdinal: 12, Vector.CallOrdinal: 12 } &&
            saved.ConstructorStores.Count == 11 && saved.Operations.Count == 0,
            "Repeated PlayerMover frames accumulated a per-frame journal, replayed constructors or lost exact counters.");
        var actualFrame = saved.PlayerFrame!;
        Reject(() => FalloutActorMoverState.Validate(saved with { PlayerFrame = actualFrame with { FlagStores = 11 } }, source, common.Stack));
        Reject(() => FalloutActorMoverState.Validate(saved with { PlayerFrame = null }, source, common.Stack));
        Reject(() => FalloutActorMoverState.Validate(saved with { PlayerFrame = actualFrame with
        { Vector = actualFrame.Vector! with { Stores = actualFrame.Vector!.Stores.Reverse().ToArray() } } }, source, common.Stack));
        Reject(() => FalloutActorMoverState.Validate(saved with { PlayerFrame = actualFrame with
        { Flags = actualFrame.Flags! with { Invocation = actualFrame.Flags!.Invocation with { Caller = new(Guid.NewGuid(), 12, Guid.NewGuid()) } } } }, source, common.Stack));
        Reject(() => FalloutActorMoverState.Validate(saved with { PlayerFrame = actualFrame with
        { Vector = actualFrame.Vector! with { Factory = Guid.NewGuid() } } }, source, common.Stack));
        mover.Retire();
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var previous = saved; var historical = JsonSerializer.Serialize(previous.PlayerFrame);
            mover = new(source, common.Stack, Guid.NewGuid(), Guid.NewGuid(), Lifetime, saved);
            mover.BindPlayerFrame(query.Actor, Guard);
            Require(mover.Factory != previous.Factory && mover.Process != previous.Process &&
                JsonSerializer.Serialize(mover.Capture().PlayerFrame) == historical,
                "Cold PlayerMover frame replayed stores or promoted its historical factory.");
            entered = Frame(previous.PlayerFrame!.FlagCalls + 1);
            _ = mover.StorePlayerFrameFlags(entered, 0);
            _ = mover.StorePlayerFrameDefaultVector(entered);
            entered = null;
            saved = RoundTripPackage(mover.Capture());
            Require(saved.PlayerFrame!.Flags!.Factory == mover.Factory && saved.PlayerFrame.Vector!.Invocation.PlayerProcess == mover.Process &&
                saved.PlayerFrame.Sequence == previous.PlayerFrame.Sequence + 7 && JsonSerializer.Serialize(previous.PlayerFrame) == historical,
                "Fresh cold frame reused an old source factory/caller or mutated prior returned history.");
            mover.Retire();
        }
        alive = false;
        FailedFrameStore(afterStore: false);
        FailedFrameStore(afterStore: true);
        UnsupportedFrameWord();
        Console.WriteLine("OPENNV_ACTOR_MOVER_FRAME_PASS sameMover=true actualCommonScope=true zeroWordStore=true " +
            "pairedXYThenZ=true boundedLatestHistory=true repeat=true coldCycles=3 failedPrefix=true movementVirtual=UNEXECUTED gameplay=UNEXECUTED");

        FalloutActorMoverFrameInvocation Frame(long ordinal) => new(new(Guid.NewGuid(), ordinal, Guid.NewGuid()), mover.Process, query, thread);
        void Lifetime()
        {
            if (!alive || Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("Authored PlayerMover frame lost its actual source lifetime/thread.");
        }
        void Guard(FalloutActorMoverFrameInvocation current)
        {
            Lifetime();
            if (current != entered || current.Common != common.Common.ReadSceneQueries(common.Player, 1))
                throw new InvalidOperationException("Authored PlayerMover frame lost its actual entered Main/Prelude/Common scope.");
        }
        void FailedFrameStore(bool afterStore)
        {
            alive = true;
            var owner = new FalloutActorMoverState(source, common.Stack, Guid.NewGuid(), Guid.NewGuid(), Lifetime);
            var call = new FalloutActorMoverFrameInvocation(new(Guid.NewGuid(), 1, Guid.NewGuid()), owner.Process, query, thread);
            var vectorGuard = 0; var enteringVector = false;
            owner.BindPlayerFrame(query.Actor, current =>
            {
                if (current != call) throw new InvalidOperationException("Authored failed frame lost its exact scope.");
                _ = common.Common.ReadSceneQueries(common.Player, 1);
                if (!enteringVector || ++vectorGuard != (afterStore ? 3 : 2)) return;
                // The actual owner records swallowed reentry; a caller cannot
                // turn that failure into a successful field-store receipt.
                try { _ = owner.Capture(); } catch (InvalidOperationException) { }
            });
            _ = owner.StorePlayerFrameFlags(call, 0); enteringVector = true;
            Reject(() => owner.StorePlayerFrameDefaultVector(call));
            var failed = RoundTripPackage(owner.Capture());
            Require(failed.PlayerFrame!.Vector is { Returned: false, Failure: not null } partial &&
                partial.Stores.Count == (afterStore ? 1 : 0) && failed.PlayerFrame.VectorZStores == 0 && owner.SaveBlocker is not null,
                "Failed PlayerMover frame erased XY or fabricated later Z/return.");
            var failureText = JsonSerializer.Serialize(failed.PlayerFrame);
            var cold = new FalloutActorMoverState(source, common.Stack, Guid.NewGuid(), Guid.NewGuid(), Lifetime, failed);
            cold.BindPlayerFrame(query.Actor, _ => throw new InvalidOperationException("Failed cold frame replayed its scope."));
            Reject(() => cold.StorePlayerFrameFlags(new(new(Guid.NewGuid(), 2, Guid.NewGuid()), cold.Process, query, thread), 0));
            Require(JsonSerializer.Serialize(cold.Capture().PlayerFrame) == failureText && cold.SaveBlocker == owner.SaveBlocker,
                "Failed cold PlayerMover frame replayed stores or erased its failed source prefix.");
            Reject(() => FalloutActorMoverState.Validate(failed with { PlayerFrame = failed.PlayerFrame with
            { Vector = failed.PlayerFrame.Vector! with { Returned = true, Failure = null }, Failure = null } }, source, common.Stack));
            owner.Retire(); cold.Retire();
        }
        void UnsupportedFrameWord()
        {
            var owner = new FalloutActorMoverState(source, common.Stack, Guid.NewGuid(), Guid.NewGuid(), Lifetime);
            var call = new FalloutActorMoverFrameInvocation(new(Guid.NewGuid(), 1, Guid.NewGuid()), owner.Process, query, thread);
            owner.BindPlayerFrame(query.Actor, current =>
            {
                if (current != call) throw new InvalidOperationException("Authored nonzero frame lost its actual scope.");
            });
            Reject(() => owner.StorePlayerFrameFlags(call, 0x400));
            Require(owner.Capture().PlayerFrame!.Flags is { Returned: false, Stores.Count: 0, PreviousFlags: 0,
                RequestedWord: 0x400, Failure: FalloutActorMoverState.FrameWordBoundary },
                "An unowned nonzero mover branch changed fields, reduced its argument or lost the actual current flags read.");
            owner.Retire();
        }
    }
}
