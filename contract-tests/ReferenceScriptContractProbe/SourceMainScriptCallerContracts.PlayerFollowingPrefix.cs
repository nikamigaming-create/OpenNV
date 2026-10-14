using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    // Authored component scope, not a whole Main/Player success fixture.
    private static void RunPlayerFollowingPrefixContracts()
    {
        RunPlayerPackageLineageContracts();
        using var warm = new FollowingPrefixFixture();
        var before = warm.Common.Capture();
        Reject(warm.Execute);
        var first = warm.Last ?? throw new InvalidDataException("Entered following call omitted its failed source prefix.");
        FalloutPlayerFollowingContinuation.Validate(first, warm.Source, warm.Stack);
        Require(first is { Inhibited: false, Life.Result: false, CurrentClearSelected: false, PrimaryClearSelected: false,
            ScalarSkipped: true, Timer.Value: 0, FailedAt: FalloutPlayerFollowingContinuationStep.ReadyItem,
            Failure: FalloutPlayerFollowingContinuation.ReadyItemBoundary } &&
            warm.Calls.SequenceEqual(["inhibition", "life", "current", "primary", "timer"]) &&
            first.InhibitionReads.Count == 4 && JsonSerializer.Serialize(before) == JsonSerializer.Serialize(warm.Common.Capture()),
            "Null package getters ran clears, time/refcount/native children or skipped real source read order.");
        var current = first.Current ?? throw new InvalidDataException("Current package source getter was omitted.");
        var primary = first.Primary ?? throw new InvalidDataException("Primary package source getter was omitted.");
        Require(current.Fields.Fields == FalloutProcessPackageWords.Constructed && primary.Fields.Fields == FalloutProcessPackageWords.Constructed &&
            current.Fields.Publication.Factory != primary.Fields.Publication.Factory && before.Current.Single().Packages is
                { ConstructedEpoch: 1, ConstructionKind: FalloutProcessPackageConstructionKind.InitialPlayerConstructor,
                  RetiredInitialLow.InitialMove: { DestinationStores: 4, SourceStores: 4 } },
            "Package state replaced full source construction and initial four-word transfer with two semantic nulls.");
        var constructed = warm.Inhibition.Capture();
        Reject(() => FalloutPlayerInhibitionState.Validate(constructed with
        { History = constructed.History.Select((entry, index) => index == 0 ? null! : entry).ToArray() }, warm.Source, warm.Stack));
        Require(constructed.History.Take(6).Select(entry => entry.Lane).SequenceEqual([
            FalloutPlayerInhibitionLane.Third, FalloutPlayerInhibitionLane.Fourth, FalloutPlayerInhibitionLane.Second,
            FalloutPlayerInhibitionLane.Second, FalloutPlayerInhibitionLane.First, FalloutPlayerInhibitionLane.First]) &&
            constructed.History[3] is { CallbackArgument: null, CallbackReturned: true } &&
            constructed.History[5] is { CallbackArgument: true, CallbackReturned: true },
            "Selected void callbacks guessed an unwritten constructor argument or omitted actual ordered stores.");
        foreach (var raw in new[] { int.MinValue, -1, 0, 1, 2, 3, 4, 5, 6, 7, int.MaxValue })
            foreach (var argument in new[] { false, true })
                Require(FalloutPlayerFollowingContinuation.LifePredicate(raw, argument) ==
                    (raw is 1 or 2 || raw == 6 && !argument), "Actual pure Actor life predicate changed its argument-dependent source truth table.");
        foreach (var lane in Enum.GetValues<FalloutPlayerInhibitionLane>())
        {
            using var changed = new FollowingPrefixFixture(); var observation = Guid.NewGuid();
            changed.Inhibition.ObserveUnownedWriter(observation, "authored-actual-observed-source-byte-writer", lane);
            Reject(() => changed.Inhibition.ObserveUnownedWriter(observation, "authored-actual-observed-source-byte-writer", lane));
            Reject(changed.Execute);
            var failed = changed.Last ?? throw new InvalidDataException("Unowned inhibition writer lost its entered prefix.");
            FalloutPlayerFollowingContinuation.Validate(failed, changed.Source, changed.Stack);
            Require(failed.FailedAt == FalloutPlayerFollowingContinuationStep.Inhibition && failed.InhibitionReads.Count == (int)lane &&
                changed.Calls.SequenceEqual(["inhibition"]) && changed.Inhibition.SaveBlocker is not null,
                "Unknown current writer admitted stale ctor0 or touched a following package/life consumer.");
            changed.ReadMovement();
        }
        foreach (var target in Enum.GetValues<FalloutProcessPackageWriterTarget>())
        {
            using var assigned = new FollowingPrefixFixture(); var observation = Guid.NewGuid();
            assigned.Common.ObserveSourcePackageAssignment(assigned.Player, 1, observation, "authored-source-script-package-election",
                new("WeaponFixture.esm", 0x301), new("WeaponFixture.esm", 0x302), false, target);
            var unavailable = assigned.Common.Capture();
            Reject(() => assigned.Common.ObserveSourcePackageAssignment(assigned.Player, 1, observation,
                "authored-source-script-package-election", null, null, false, target));
            Reject(assigned.Execute);
            var failure = assigned.Last ?? throw new InvalidDataException("Observed assignment omitted its source getter refusal.");
            FalloutPlayerFollowingContinuation.Validate(failure, assigned.Source, assigned.Stack);
            Require(failure.FailedAt == ((target & FalloutProcessPackageWriterTarget.Current) != 0
                ? FalloutPlayerFollowingContinuationStep.CurrentPackage : FalloutPlayerFollowingContinuationStep.PrimaryPackage) &&
                assigned.Common.SaveBlocker is not null && JsonSerializer.Serialize(unavailable) == JsonSerializer.Serialize(assigned.Common.Capture()),
                "Executed script package assignment silently retained a current constructor-null getter or ran retrospective effects.");
            var beforeInhibition = JsonSerializer.Serialize(assigned.Inhibition.Capture());
            var publication = Guid.NewGuid();
            assigned.Common.ObserveSourcePackageAssignment(assigned.Player, 1, publication,
                "authored-actual-world-script-package-begin-publication", null, new("WeaponFixture.esm", 0x302), false, target);
            var repeated = assigned.Common.Capture();
            var repeatedSlots = repeated.Current.Single().Packages ?? throw new InvalidDataException("Repeated actual package ingress lost its field owners.");
            var observed = (target & FalloutProcessPackageWriterTarget.Current) != 0 ? repeatedSlots.Current :
                repeatedSlots.Primary ?? throw new InvalidDataException("Repeated primary ingress lost its independent slot.");
            Require(observed.Observations is { Count: 2 } && observed.Observations[0].Observation == observation &&
                observed.Observations[1].Observation == publication &&
                observed.Observations[1].RequestedPackage == observed.Observations[0].RequestedPackage &&
                unavailable.Current.Single().Packages is { } prior &&
                (((target & FalloutProcessPackageWriterTarget.Current) != 0 ? prior.Current : prior.Primary)?.Observations.Count == 1) &&
                JsonSerializer.Serialize(assigned.Inhibition.Capture()) == beforeInhibition,
                "Distinct executed package publication replayed a pending command receipt, changed old snapshots or inferred a Player inhibition writer.");
            using var coldAssigned = new FollowingPrefixFixture(assigned.Snapshot());
            Reject(coldAssigned.Execute);
            var restored = coldAssigned.Common.Capture().Current.Single().Packages ?? throw new InvalidDataException("Cold assignment omitted source package fields.");
            var restoredObserved = (target & FalloutProcessPackageWriterTarget.Current) != 0 ? restored.Current :
                restored.Primary ?? throw new InvalidDataException("Cold assignment omitted its primary slot.");
            Require(restoredObserved.Observations.SequenceEqual(observed.Observations) && !restoredObserved.Available &&
                restoredObserved.Publication.Factory != observed.Publication.Factory &&
                coldAssigned.Inhibition.Capture().History.SequenceEqual(assigned.Inhibition.Capture().History),
                "Cold package publication replayed an executed begin/removal ingress or repaired an unowned assignment.");
        }
        var saved = warm.Snapshot();
        var dormant = saved;
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new FollowingPrefixFixture(dormant);
            var oldTimer = first.Timer ?? throw new InvalidDataException("Warm timer read was omitted.");
            FalloutPlayerFollowingTimerFlagState.RequireHistoricalPublication(cold.Timer.Capture(), cold.Source, cold.Stack, oldTimer.Process, oldTimer.Factory);
            foreach (var read in first.InhibitionReads)
                FalloutPlayerInhibitionState.RequireHistoricalPublication(cold.Inhibition.Capture(), cold.Source, cold.Stack, read.Process, read.Factory);
            Reject(() => FalloutPlayerFollowingTimerFlagState.RequireHistoricalPublication(cold.Timer.Capture(), cold.Source, cold.Stack, oldTimer.Process, Guid.NewGuid()));
            Require(JsonSerializer.Serialize(cold.Last) == JsonSerializer.Serialize(first) && cold.Calls.Count == 0 &&
                cold.Inhibition.Capture().History.SequenceEqual(constructed.History),
                "Cold publication without a new call replayed field reads/stores or lost an older genuine factory.");
            dormant = cold.Snapshot();
        }
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var historical = JsonSerializer.Serialize(saved.Last);
            using var cold = new FollowingPrefixFixture(saved); Reject(cold.Execute);
            var next = cold.Last ?? throw new InvalidDataException("Cold component omitted its fresh entered source call.");
            FalloutPlayerFollowingContinuation.Validate(next, cold.Source, cold.Stack);
            Require(next is { FailedAt: FalloutPlayerFollowingContinuationStep.ReadyItem, ScalarSkipped: true } &&
                next.PlayerProcess != first.PlayerProcess && next.Current?.Process.Process != first.Current?.Process.Process &&
                next.Current?.Fields.Publication.Factory != first.Current?.Fields.Publication.Factory &&
                next.Primary?.Fields.Publication.Factory != first.Primary?.Fields.Publication.Factory &&
                cold.Inhibition.Capture().History.SequenceEqual(constructed.History) &&
                JsonSerializer.Serialize(saved.Last) == historical,
                "Repeated cold construction replayed original source stores, aliased factories or overwrote a historical caller.");
            saved = cold.Snapshot();
        }
        Reject(() => FalloutPlayerFollowingContinuation.Validate(first with { Primary = current }, warm.Source, warm.Stack));
        Reject(() => FalloutPlayerFollowingContinuation.Validate(first with { ScalarSkipped = false }, warm.Source, warm.Stack));
        Reject(() => FalloutProcessPackageState.Construct(warm.Source.Controls.Main.EngineSha256, Guid.NewGuid(), 2,
            FalloutDetectionProcessLevel.High, FalloutProcessPackageConstructionKind.InitialPlayerConstructor));
        Require(!JsonSerializer.Serialize(warm.Timer.Capture()).Contains("Scalar", StringComparison.Ordinal),
            "Zero source timer byte invented the independent uninitialized scalar in persistence.");
        var original = JsonSerializer.Serialize(warm.Inhibition.Capture());
        Task.Run(() => Reject(() => warm.Inhibition.Capture())).GetAwaiter().GetResult();
        Require(original == JsonSerializer.Serialize(warm.Inhibition.Capture()), "Foreign thread changed current inhibition fields.");
        Console.WriteLine("OPENNV_PLAYER_FOLLOWING_PREFIX_PASS fullIndependentPackageFields=true sharedInhibition=true " +
            "unwrittenConstructorArgumentPreserved=true borrowedLife=true actualAssignmentRefused=true noScalarFabrication=true coldCycles=3 wholePlayer=UNRETURNED");
    }

    private sealed record FollowingPrefixCold(FalloutProcessCommonSnapshot Common, FalloutActorProcessRuntimeSnapshot Life,
        FalloutPlayerInhibitionSnapshot Inhibition, FalloutPlayerFollowingTimerFlagSnapshot Timer, FalloutPlayerFollowingContinuationCall? Last);

    private sealed class FollowingPrefixFixture : IFalloutPlayerFollowingContinuationConsumers, IDisposable
    {
        private readonly WeaponContinuationFixture _actual;
        private readonly FalloutActorProcessRuntimeState _life;
        internal readonly FalloutPlayerFollowingContinuationSource Source;
        internal readonly FalloutPlayerInhibitionState Inhibition;
        internal readonly FalloutPlayerFollowingTimerFlagState Timer;
        internal readonly FalloutPlayerSourceCaller Caller = new(Guid.NewGuid(), 1, Guid.NewGuid());
        internal FalloutFormKey Player => _actual.Player;
        internal FalloutActorProcessCommonState Common => _actual.Common;
        internal string Stack => _actual.Stack;
        internal readonly List<string> Calls = [];
        internal FalloutPlayerFollowingContinuationCall? Last;
        private readonly Guid _process = Guid.NewGuid(), _constructor = Guid.NewGuid();
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly FalloutDirectInputState _input;
        private bool _entered;

        internal FollowingPrefixFixture(FollowingPrefixCold? saved = null)
        {
            _actual = new(saved?.Common); Source = FalloutPlayerFollowingContinuationSource.Reviewed(_actual.Source.Controls);
            _life = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(Source.Controls.Main.EngineSha256), Stack, Player,
                actor => Common.ReadSceneQueries(actor, 1).Actor, saved?.Life);
            Inhibition = new(Source, Stack, _process, _constructor, (_, caller) => RequireCurrent(caller), saved?.Inhibition);
            Timer = new(Source, Stack, _process, _constructor, RequireCurrent, saved?.Timer);
            _input = FalloutDirectInputState.CreateHeadless(Stack, new(_ => null));
            Inhibition.BindActualInput(_input); Timer.BindActualInput(_input);
            Last = saved?.Last;
        }
        public void RequireCurrent(FalloutPlayerSourceCaller caller)
        {
            if (!_entered || _thread != Environment.CurrentManagedThreadId || caller != Caller)
                throw new InvalidOperationException("Authored following component changed its actual entered caller/thread.");
        }
        internal void Execute()
        {
            if (_entered) throw new InvalidOperationException("Component already entered.");
            _entered = true; Calls.Clear();
            try { FalloutPlayerFollowingContinuation.Execute(Source, Stack, _process, Caller, this, call => Last = call); }
            finally { _entered = false; }
        }
        internal void ReadMovement()
        {
            _entered = true;
            try { Reject(() => Inhibition.ReadCurrent(FalloutPlayerInhibitionConsumer.Movement, Caller)); }
            finally { _entered = false; }
        }
        public FalloutPlayerInhibitionPredicate ReadInhibition(FalloutPlayerSourceCaller caller, Action<FalloutPlayerInhibitionRead> observe)
        { Calls.Add("inhibition"); return Inhibition.ReadCurrent(FalloutPlayerInhibitionConsumer.Following, caller, observe); }
        public FalloutPlayerFollowingLifeRead ReadLife(bool argument)
        {
            Calls.Add("life"); var process = Common.ReadSceneQueries(Player, 1); var fact = _life.Life(Player);
            return new(process, fact, argument, fact.Value is { } raw && fact.Failure is null
                ? FalloutPlayerFollowingContinuation.LifePredicate(raw, argument) : null);
        }
        public FalloutProcessPackageRead ReadCurrentPackage() { Calls.Add("current"); return Common.ReadSourcePackageSlot(Player, 1, FalloutProcessPackageSlot.Current); }
        public FalloutProcessPackageRead ReadPrimaryPackage() { Calls.Add("primary"); return Common.ReadSourcePackageSlot(Player, 1, FalloutProcessPackageSlot.Primary); }
        public FalloutPlayerFollowingTimerFlagRead ReadTimerByte(FalloutPlayerSourceCaller caller) { Calls.Add("timer"); return Timer.ReadCurrent(caller); }
        internal FollowingPrefixCold Snapshot() => JsonSerializer.Deserialize<FollowingPrefixCold>(JsonSerializer.Serialize(
            new FollowingPrefixCold(Common.Capture(), _life.Capture(), Inhibition.Capture(), Timer.Capture(), Last))) ??
            throw new InvalidDataException("Following source cold component could not serialize all owners/history.");
        public void Dispose() { Inhibition.Retire(); Timer.Retire(); _input.Retire(); _life.Dispose(); _actual.Dispose(); }
    }
}
