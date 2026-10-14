using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunActorMoverContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-mover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "WeaponFixture.esm");
        try
        {
            File.WriteAllBytes(file, PackageStateFixturePlugin());
            using var records = FalloutPluginStack.Load(directory, ["WeaponFixture.esm"]);
            using var packages = new PackageMutationFixture(records);
            var main = MoverMain(); var controls = FalloutPlayerMovementControlSource.Reviewed(main);
            var source = FalloutActorMoverSource.Reviewed(controls);
            var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), packages.Stack, Guid.NewGuid());
            var mover = scene.ActorMover; var initial = mover.Capture();
            Require(initial.Values is { InheritedFlags: 0x101, ResetByte: 0, Player.Flags: 0, Player.TimerBits: 0xbf800000 } &&
                initial.Values.InheritedVector == FalloutActorMoverVector.Default && initial.ConstructorStores.Count == 11 &&
                initial.Operations.Count == 0 && scene.Capture().MoverFields == mover.ReadPlayerFields(),
                "Logical mover replaced inherited cells with the independent Player movement word.");
            var input = FalloutDirectInputState.CreateHeadless(packages.Stack, new(_ => null));
            scene.PublishControlCommandInput(input); scene.PublishControlCommandInput(input);
            var foreignInput = FalloutDirectInputState.CreateHeadless(packages.Stack, new(_ => null));
            Reject(() => mover.BindActualInput(foreignInput)); foreignInput.Retire();
            var currentJoin = MoverJoin(packages.Set(FalloutProcessPackageSlot.Current, 0x100));
            scene.BindActorMoverPackageReset(currentJoin.PackageStore.Invocation.Actor, Current, _ => currentJoin);
            scene.ResetActorMoverForPackageAssignment(currentJoin.PackageStore.Invocation);
            var current = mover.Capture();
            Require(current.Operations[^1] is { Returned: true, Stores.Count: 3, Join.PackageStore.Slot: FalloutProcessPackageSlot.Current } &&
                current.Operations[^1].Stores.Select(store => store.Field).SequenceEqual([FalloutActorMoverField.InheritedVectorXY,
                    FalloutActorMoverField.InheritedVectorZ, FalloutActorMoverField.ResetByte]) &&
                current.Values.Player == initial.Values.Player && current.Values.InheritedFlags == 0x101 &&
                scene.Capture().ActorMover!.Factory == mover.Factory,
                "Current assignment did not reset the same logical owner or reordered/aliased its inherited stores.");
            currentJoin = MoverJoin(packages.Set(FalloutProcessPackageSlot.Primary, 0x101));
            scene.ResetActorMoverForPackageAssignment(currentJoin.PackageStore.Invocation);
            var complete = mover.Capture(); var text = JsonSerializer.Serialize(complete);
            Reject(() => scene.ResetActorMoverForPackageAssignment(currentJoin.PackageStore.Invocation));
            Reject(() => scene.ResetActorMoverForPackageAssignment(currentJoin.PackageStore.Invocation with { Epoch = 2 }));
            Reject(() => scene.ResetActorMoverForPackageAssignment(currentJoin.PackageStore.Invocation with { Actor = currentJoin.PackageStore.Invocation.Actor with { Base = new("WeaponFixture.esm", 8) } }));
            Require(JsonSerializer.Serialize(mover.Capture()) == text, "Rejected logical mover caller changed its source fields/history.");
            var saved = RoundTripPackage(scene.Capture()); var operationCount = complete.Operations.Count;
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                var cold = new FalloutStandalonePlayerSceneState(scene.Source, packages.Stack, Guid.NewGuid(), saved);
                var restored = cold.Capture(); var state = restored.ActorMover!;
                Require(state.Factory != saved.ActorMover!.Factory && state.Process != saved.ActorMover.Process &&
                    state.Values == complete.Values && state.Operations.Count == operationCount && state.ConstructorStores.Count == 11,
                    "Cold logical mover replayed constructor/reset stores or lost historical publications.");
                FalloutActorMoverState.Validate(state, source, packages.Stack, restored.PointOfView!.Lifetimes);
                saved = RoundTripPackage(restored); cold.Retire();
            }
            Reject(() => FalloutActorMoverState.Validate(complete with { Values = complete.Values with { InheritedFlags = 0 } }, source, packages.Stack));
            Reject(() => FalloutActorMoverState.Validate(complete with { ConstructorStores = complete.ConstructorStores.Reverse().ToArray() }, source, packages.Stack));
            Reject(() => FalloutActorMoverState.Validate(complete with { Operations = complete.Operations.Select(operation => operation with
            { Stores = operation.Stores.Reverse().ToArray() }).ToArray() }, source, packages.Stack));
            Reject(() => FalloutActorMoverState.Validate(saved.ActorMover!, source, packages.Stack,
                saved.PointOfView!.Lifetimes.Skip(1).ToArray()));
            FailedMoverPrefix(records, source);
            MissingMoverPredecessor(records, source);
            MoverPreparationHandoff(source, packages.Stack);
            RunActorMoverFrameStores(records, source);
            scene.Retire(); input.Retire();
            Console.WriteLine("OPENNV_ACTOR_MOVER_PASS sameSceneOwner=true inherited101Player0=true " +
                "actualCurrentPrimarySetters=true predecessorReceipts=authored orderedXY_Z_byte=true failedPrefix=true " +
                "explicitThreadPublication=true repeatedInput=true coldCycles=3 noStoreReplay=true physicalController=UNIMPLEMENTED gameplay=UNEXECUTED");

            void Current(FalloutPackageCommandFieldInvocation invocation)
            {
                var actual = packages.Common.ReadSceneQueriesForPackageCommand(invocation);
                if (actual.Actor != invocation.Actor || actual.Process != invocation.Process)
                    throw new InvalidOperationException("Authored mover lost its real Common Actor.");
            }
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    private static void FailedMoverPrefix(FalloutPluginStack records, FalloutActorMoverSource source)
    {
        using var packages = new PackageMutationFixture(records);
        var join = MoverJoin(packages.Set(FalloutProcessPackageSlot.Current, 0x100));
        var mover = new FalloutActorMoverState(source, packages.Stack, Guid.NewGuid(), Guid.NewGuid(), () => { });
        var guardCount = 0; var fail = false;
        mover.BindPackageReset(join.PackageStore.Invocation.Actor, invocation =>
        {
            _ = packages.Common.ReadSceneQueriesForPackageCommand(invocation);
            if (fail && ++guardCount == 5) throw new NotSupportedException("authored-after-XY-lifetime-failure");
        }, _ => join);
        mover.ObserveUnownedWriter(packages.Invocation(), FalloutActorMoverLane.InheritedVector, "authored-current-vector-writer-unbound");
        fail = true;
        Reject(() => mover.ResetForPackageAssignment(join.PackageStore.Invocation));
        var saved = RoundTripPackage(mover.Capture());
        Require(saved.Operations[^1] is { Returned: false, Stores.Count: 1 } &&
            saved.Operations[^1].Stores[0].Field == FalloutActorMoverField.InheritedVectorXY &&
            !saved.Availability.Single(value => value.Lane == FalloutActorMoverLane.InheritedVector).Available && mover.SaveBlocker is not null,
            "Failed logical mover reset discarded XY or fabricated later Z/byte authority.");
        var calls = guardCount;
        var cold = new FalloutActorMoverState(source, packages.Stack, Guid.NewGuid(), Guid.NewGuid(), () => { }, saved);
        Require(cold.Capture().Operations.Count == saved.Operations.Count && guardCount == calls && cold.SaveBlocker == mover.SaveBlocker,
            "Failed cold logical mover replayed a source child or erased its missing vector producer.");
        var foreign = saved.Operations[^1] with { Join = saved.Operations[^1].Join! with
        { HighReset = saved.Operations[^1].Join!.HighReset with { Invocation = join.PackageStore.Invocation with { SourceCommand = Guid.NewGuid() } } } };
        Reject(() => FalloutActorMoverState.Validate(saved with { Operations = saved.Operations.Take(saved.Operations.Count - 1).Append(foreign).ToArray() }, source, packages.Stack));
        mover.Retire(); cold.Retire();
    }
    private static void MissingMoverPredecessor(FalloutPluginStack records, FalloutActorMoverSource source)
    {
        using var packages = new PackageMutationFixture(records);
        var join = MoverJoin(packages.Set(FalloutProcessPackageSlot.Primary, 0x100));
        var mover = new FalloutActorMoverState(source, packages.Stack, Guid.NewGuid(), Guid.NewGuid(), () => { });
        mover.BindPackageReset(join.PackageStore.Invocation.Actor,
            invocation => { _ = packages.Common.ReadSceneQueriesForPackageCommand(invocation); },
            _ => join with { HighReset = join.HighReset with { Returned = false, Failure = "authored-High-reset-unbound" } });
        Reject(() => mover.ResetForPackageAssignment(join.PackageStore.Invocation));
        Require(mover.Capture().Operations[^1] is { Returned: false, Stores.Count: 0, Join: null },
            "Logical mover accepted a missing real High reset or performed a premature store.");
        mover.Retire();
    }
    private static void MoverPreparationHandoff(FalloutActorMoverSource source, string stack)
    {
        var mover = Task.Run(() => new FalloutActorMoverState(source, stack, Guid.NewGuid(), Guid.NewGuid(), () => { })).GetAwaiter().GetResult();
        var prepared = mover.Capture();
        Reject(() => mover.ReadPlayerFields());
        var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
        mover.BindActualInput(input); mover.BindActualInput(input);
        Require(mover.ReadPlayerFields() == prepared.Values.Player && mover.Capture().Sequence == prepared.Sequence,
            "Real runtime thread publication replayed source stores or rejected immutable worker preparation.");
        Task.Run(() => Reject(() => mover.ReadPlayerFields())).GetAwaiter().GetResult();
        mover.Retire(); input.Retire(); Reject(() => mover.ReadPlayerFields());
    }
    private static FalloutActorMoverResetJoin MoverJoin(FalloutPackageStateMutation package)
    {
        // These preceding child receipts are authored inputs to this focused
        // mover contract. They do not qualify actual High/link reset behavior.
        var linked = new FalloutActorMoverResetChildReceipt(FalloutActorMoverResetChild.LinkedReference, Guid.NewGuid(),
            package.Invocation, package.Process, package.Changed, package.Changed, true, null);
        var high = new FalloutActorMoverResetChildReceipt(FalloutActorMoverResetChild.HighReset, Guid.NewGuid(),
            package.Invocation, package.Process, package.Changed, package.Changed, true, null);
        return new(package, linked, high);
    }
    private static FalloutMainScriptCallerSource MoverMain() => FalloutMainScriptCallerSource.Read(new(
        FalloutSourceMainFamily.Fallout3, new('1', 64), FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
}
