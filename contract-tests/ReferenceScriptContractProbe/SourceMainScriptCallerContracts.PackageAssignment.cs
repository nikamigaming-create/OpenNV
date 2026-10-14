using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunSourcePackageAssignmentContracts()
    {
        Require(FalloutPackageAssignmentSource.PureProcedure(3) == 5 &&
            FalloutPackageAssignmentSource.PureProcedure(6) == 0 &&
            FalloutPackageAssignmentSource.PureProcedure(28) == 10 &&
            FalloutPackageAssignmentSource.PureProcedure(36) == 46,
            "Pure package procedure selection lost distinct ordinary, shared and terminal kinds.");
        foreach (var kind in new sbyte[] { sbyte.MinValue, -1, 0, 1, 2, 11, 17, 19, 25, 37, sbyte.MaxValue })
            Require(FalloutPackageAssignmentSource.PureProcedure(kind) is null,
                "Package procedure selection invented a return for a dependent or error-report arm.");
        using (var fixture = new FollowingPrefixFixture())
        {
            var process = fixture.Common.ReadSceneQueries(fixture.Player, 1);
            var invocation = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), process.Actor, process.Process, 1, Environment.CurrentManagedThreadId);
            void Guard(FalloutPackageCommandFieldInvocation actual)
            {
                if (actual != invocation) throw new InvalidOperationException("Authored source command scope changed.");
                _ = fixture.Common.ReadSceneQueriesForPackageCommand(actual);
            }
            var before = fixture.Common.Capture(); var json = JsonSerializer.Serialize(before);
            Reject(() => fixture.Common.StorePackageRequestFlag(invocation, true, actual =>
            {
                try { _ = fixture.Common.StorePackageRequestFlag(actual, false, Guard); }
                catch (InvalidOperationException) { }
            }));
            Require(JsonSerializer.Serialize(fixture.Common.Capture()) == json,
                "A swallowed actual reentry committed a package request byte.");
            var priorInhibition = JsonSerializer.Serialize(fixture.Inhibition.Capture());
            Reject(() => fixture.Inhibition.StorePackageAssignmentFourth(invocation, actual =>
            {
                try { _ = fixture.Inhibition.Capture(); }
                catch (InvalidOperationException) { }
            }));
            Require(JsonSerializer.Serialize(fixture.Inhibition.Capture()) == priorInhibition,
                "A swallowed Fourth callback reentry committed the Player byte.");
            fixture.Inhibition.StorePackageAssignmentFourth(invocation, Guard);
            var fourth = fixture.Inhibition.Capture();
            Require(fourth.Bytes[3] is { Value: 1, Available: true } && fourth.History[^1].PackageCommand == invocation,
                "The returned-prefix Fourth store lost its distinct source command history.");
            var set = fixture.Common.StorePackageRequestFlag(invocation, true, Guard);
            var clear = fixture.Common.StorePackageRequestFlag(invocation, false, Guard);
            var start = fixture.Common.StorePackageStartByte(invocation, 1, Guard);
            var cleanupFirst = fixture.Common.ClearPackageStartCacheFlag(invocation, FalloutActorProcessCommonState.PackageStartFirstCacheMask, Guard);
            var cleanupSecond = fixture.Common.ClearPackageStartCacheFlag(invocation, FalloutActorProcessCommonState.PackageStartSecondCacheMask, Guard);
            Require(set.Before == 0 && set.After == 0x20 && clear.Before == 0x20 && clear.After == 0 &&
                start.Before == 0 && start.After == 1 && cleanupFirst.Context == cleanupSecond.Context &&
                cleanupSecond.Changed > cleanupFirst.Changed && JsonSerializer.Serialize(before) == json,
                "Package callbacks aliased distinct bytes, lost independent cached-value reads or mutated a prior capture.");
            var captured = fixture.Common.Capture(); FalloutActorProcessCommonState.Validate(captured);
            var entry = captured.Current.Single();
            Reject(() => FalloutActorProcessCommonState.Validate(captured with { Current = [entry with
            { RequestFlagStores = [set with { After = 0 }, clear] }] }));
            Reject(() => FalloutActorProcessCommonState.Validate(captured with { Current = [entry with
            { SceneFields = entry.SceneFields! with { PackageStart = entry.SceneFields!.PackageStart! with { AdjacentByte = 1 } } }] }));
            Reject(() => FalloutActorProcessCommonState.Validate(captured with { Current = [entry with
            { RequestFlagStores = [set with { Invocation = invocation with { Process = Guid.NewGuid() } }, clear] }] }));
            var saved = fixture.Snapshot();
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                using var cold = new FollowingPrefixFixture(saved);
                var restored = cold.Common.Capture(); FalloutActorProcessCommonState.Validate(restored);
                Require(restored.Current.Single().RequestFlagStores.SequenceEqual(entry.RequestFlagStores) &&
                    JsonSerializer.Serialize(restored.Current.Single().SceneFields!.PackageStart) == JsonSerializer.Serialize(entry.SceneFields!.PackageStart),
                    "Cold source byte fields replayed a command or lost its historical process.");
                saved = cold.Snapshot();
            }
        }
        using (var fixture = new FollowingPrefixFixture())
        {
            var consumers = new PackageAssignmentConsumers(fixture);
            consumers.Execute();
            Require(consumers.Last is { Step: FalloutSourcePackageAssignmentStep.Returned, IsPlayer: true, Result: true } &&
                consumers.Calls[^1] == "fourth" && consumers.Calls.IndexOf("saved-position") < consumers.Calls.IndexOf("package-flag") &&
                consumers.Calls.IndexOf("assignment") < consumers.Calls.IndexOf("fourth"),
                "Source package assignment returned or stored Fourth before its mandatory children.");
        }
        foreach (var missing in new[] { "event-start", "start", "procedure", "saved-position", "assignment" })
        {
            using var fixture = new FollowingPrefixFixture();
            var consumers = new PackageAssignmentConsumers(fixture) { Fail = missing };
            Reject(consumers.Execute);
            Require(consumers.Last is { Step: FalloutSourcePackageAssignmentStep.Failed, Result: null } &&
                consumers.Calls[^1] == missing && !consumers.Calls.Contains("fourth"),
                "Failed synchronous package child executed the later source command suffix or Fourth store.");
        }
        using (var fixture = new FollowingPrefixFixture())
        {
            var consumers = new PackageAssignmentConsumers(fixture) { InitialNull = true };
            consumers.Execute();
            Require(consumers.Calls.SequenceEqual(["selected", "process"]) && consumers.Last?.Result == true,
                "Initial genuine process-null arm reached the post-assignment Player tail.");
        }
        using (var fixture = new FollowingPrefixFixture())
        {
            var consumers = new PackageAssignmentConsumers(fixture) { TailNull = true };
            consumers.Execute();
            Require(consumers.Calls[^1] == "fourth" && !consumers.Calls.Contains("tier"),
                "Post-assignment genuine process-null arm skipped its distinct actual Player tail.");
        }
        Console.WriteLine("SOURCE_PACKAGE_ASSIGNMENT_PASS synchronousOrderedChildren=true partialFailuresRetained=true independentBytes=true initialAndTailNullDistinct=true coldCycles=3 wholePlayer=UNRETURNED");
    }

    private sealed class PackageAssignmentConsumers(FollowingPrefixFixture fixture) : IFalloutSourcePackageAssignmentConsumers
    {
        private readonly FalloutProcessSceneQuery _process = fixture.Common.ReadSceneQueries(fixture.Player, 1);
        private FalloutPackageCommandFieldInvocation? _entered;
        private int _reads;
        internal readonly List<string> Calls = [];
        internal string? Fail;
        internal bool InitialNull, TailNull;
        internal FalloutSourcePackageAssignmentCall? Last;
        public FalloutPackageCommandFieldsSource Source => FalloutPackageCommandFieldsSource.Reviewed(fixture.Source.Controls);
        public FalloutPackageAssignmentSource AssignmentSource => FalloutPackageAssignmentSource.Reviewed(Source);
        public string Stack => fixture.Stack;
        internal void Execute()
        {
            var invocation = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), _process.Actor, _process.Process, 1, Environment.CurrentManagedThreadId);
            _entered = invocation;
            try { _ = FalloutSourcePackageAssignment.Execute(invocation, null, new("Authored.esm", 0x200), "authored-command", this, call => Last = call); }
            finally { _entered = null; }
        }
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation)
        { if (invocation != _entered || invocation.ThreadId != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Command lost its authored scope."); }
        private void Call(string name) { Calls.Add(name); if (name == Fail) throw new NotSupportedException("authored-missing-" + name); }
        public FalloutSourceSelectedPackageRead ReadSelectedPackage(FalloutPackageCommandFieldInvocation invocation,
            Action<FalloutSourceSelectedPackageRead> observe)
        {
            Call("selected");
            var primary = InitialNull ? null : fixture.Common.ReadSourcePackageSlot(fixture.Player, 1, FalloutProcessPackageSlot.Primary);
            observe(new(primary, null));
            var current = InitialNull ? null : fixture.Common.ReadSourcePackageSlot(fixture.Player, 1, FalloutProcessPackageSlot.Current);
            var read = new FalloutSourceSelectedPackageRead(primary, current); observe(read); return read;
        }
        public FalloutProcessPackageRead? ReadCurrentPackage(FalloutPackageCommandFieldInvocation invocation) { Call("current"); return null; }
        public sbyte ReadPackageKind(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) { Call("kind"); return 6; }
        public void SpecialPackageCallback(FalloutPackageCommandFieldInvocation invocation) => Call("special");
        public FalloutProcessSceneQuery? ReadProcess(FalloutPackageCommandFieldInvocation invocation)
        { Call("process"); return ++_reads == 1 ? InitialNull ? null : _process : TailNull ? null : _process; }
        public void MarkEvent(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask) => Call(mask == 0x200 ? "event-start" : "event-change");
        public void PackageChanged(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) => Call("change");
        public void ClearProcessFlag(FalloutPackageCommandFieldInvocation invocation) => Call("clear-flag");
        public void PackageStarted(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) => Call("start");
        public int EnsureProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) { Call("procedure"); return 0; }
        public void SavePosition(FalloutPackageCommandFieldInvocation invocation) => Call("saved-position");
        public void SetPackageFlag(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask) => Call("package-flag");
        public void AssignActorPackage(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, bool primary, bool alternate)
        { Require(!primary && !alternate, "Source command changed its two original mode operands."); Call("assignment"); }
        public int ReadProcessTier(FalloutPackageCommandFieldInvocation invocation) { Call("tier"); return 0; }
        public uint StoreProcessClock(FalloutPackageCommandFieldInvocation invocation) { Call("clock"); return 0; }
        public void UpdateTierOne(FalloutPackageCommandFieldInvocation invocation) => Call("tier-one");
        public bool ReadActualPlayerIdentity(FalloutPackageCommandFieldInvocation invocation) { Call("player"); return true; }
        public void StorePlayerFourth(FalloutPackageCommandFieldInvocation invocation) => Call("fourth");
    }
}
