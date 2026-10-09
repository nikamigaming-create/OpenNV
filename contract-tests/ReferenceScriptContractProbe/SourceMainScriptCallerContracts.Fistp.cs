using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    // Actual first-party native ABI execution on this probe's calling thread,
    // joined to the real C# Main/Player token owner with authored source inputs.
    // This is neither a retail observation nor ordinary Godot gameplay proof.
    internal static void RunCallingThreadFistp(string firstPartyAdapter)
    {
        using var native = new FalloutCallingThreadFistpHost(firstPartyAdapter); native.Initialize();
        var initial = native.Binding;
        using var warm = new Fixture();
        var source = FalloutMainPlayerCellSource.Read(warm.Host.Source);
        warm.Owner.ConstructMainPlayerCell(source, new FalloutPlayerPendingSlot(), null);
        warm.Host.AwaitPlayer = warm.Owner.ExecuteMainPlayerCell;
        using (warm.Owner.BindCallingThreadFistp(native))
        using (warm.Owner.BindMainPlayerCell(new FistpPlayerConsumer(source), "authored-actual-native-FISTP-thread"))
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(81, .02f).GetAwaiter().GetResult();
        var saved = warm.Owner.Capture();
        Require(saved.Fistp is { Bound: false, Pairs: 2, Conversions: 4, Last.Returned: true } &&
            saved.Fistp.Last!.Context.Site == FalloutSourceFistpSite.PlayerTargetCell && saved.Fistp.Last.Host == initial &&
            saved.Fistp.Last.Operands.All(value => value.Native?.Outcome == FalloutFistpOutcome.Converted && value.Integer is not null),
            "Actual C# source child did not execute both source sites on its real native calling thread.");
        var actual = saved.Fistp.Last!;
        FalloutSourceFistpState.Validate(saved.Fistp);
        Reject(() => FalloutSourceFistpState.Validate(saved.Fistp with { Last = actual with { Operands = actual.Operands
            .Select((value, index) => index == 0 ? value with { Integer = value.Integer ^ 1 } : value).ToArray() } }));
        Reject(() => FalloutSourceFistpState.Validate(saved.Fistp with { Last = actual with { Context = actual.Context with { Child = FalloutMainPlayerCellStep.PositionRead } } }));
        var beforeThread = native.ObservationOrdinal;
        Task.Run(() => Reject(() => native.Convert(0x3f000000))).GetAwaiter().GetResult();
        Require(native.ObservationOrdinal == beforeThread, "Cross-thread refusal executed a native probe or FISTP on another thread.");
        var beforeRestore = native.ObservationOrdinal;
        using var cold = new Fixture(saved, warm.Owner.CaptureMainScriptFrameEvidence(), warm.Owner.CaptureMainScriptCaller(), warm.Fade.Capture());
        Require(cold.Owner.Capture().Fistp is { Bound: false, Conversions: 4 } &&
            cold.Owner.Capture().Fistp.CapturedProcess != saved.Fistp.CapturedProcess && native.ObservationOrdinal == beforeRestore,
            "Cold source restore executed a native conversion or reused the prior process/thread lease.");
        Reject(() => cold.Owner.BindCallingThreadFistp(native));
        using var fresh = new FalloutCallingThreadFistpHost(firstPartyAdapter); fresh.Initialize();
        Require(fresh.Binding.Identity != initial.Identity, "Cold native host reused its former actual construction identity.");
        cold.Owner.ConstructMainPlayerCell(source, new FalloutPlayerPendingSlot(), warm.Owner.CaptureMainPlayerCell());
        cold.Host.AwaitPlayer = cold.Owner.ExecuteMainPlayerCell;
        using (cold.Owner.BindCallingThreadFistp(fresh))
        using (cold.Owner.BindMainPlayerCell(new FistpPlayerConsumer(source), "authored-cold-actual-native-FISTP-thread"))
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
        var continued = cold.Owner.Capture();
        Require(continued.Fistp.Pairs == 4 && continued.Fistp.Conversions == 8 && continued.Fistp.Last!.Host == fresh.Binding &&
            continued.Fistp.Last.Context.MainOrdinal == 2 && continued.Fistp.Last.Context.Process == continued.CapturedProcess &&
            continued.Fistp.ColdHandoff?.PreviousProcess == saved.CapturedProcess,
            "Cold native continuation rewound counters, replayed history or left the new actual Main/native lease.");
        Console.WriteLine("OPENNV_CALLING_THREAD_FISTP_NATIVE_PASS sourceSites=2 actualConversions=8 currentNativeThread=true wrongThreadNoInstruction=true coldFreshLease=true authoredMainInputs=true godotGameplay=UNEXECUTED newOsProcess=UNEXECUTED");
    }
    private sealed class FistpPlayerConsumer(FalloutMainPlayerCellSource source) : IFalloutMainPlayerCellConsumers
    {
        public FalloutMainPlayerCellSource Source => source;
        public string Owner => "authored-native-FISTP-Player-child";
        public bool SceneMode(FalloutMainPlayerCellInvocation invocation) { invocation.Require(FalloutMainPlayerCellStep.SceneMode); return false; }
        public bool ScenePresent(FalloutMainPlayerCellInvocation invocation) { invocation.Require(FalloutMainPlayerCellStep.SceneRead); return false; }
        public FalloutMainPlayerSourceCell? ParentCell(FalloutMainPlayerCellInvocation invocation)
        {
            invocation.Require(FalloutMainPlayerCellStep.ParentCellRead);
            return new(new(new("Authored.esm", 41), 0, new('c', 64), new("Authored.esm", 40), new('d', 64)), 0, 777, 555);
        }
        public FalloutMainPlayerSourcePosition Position(FalloutMainPlayerCellInvocation invocation)
        {
            invocation.Require(FalloutMainPlayerCellStep.PositionRead);
            return new(0x457ff800, 0xc5800400, 0, new(null, "no-construction-rounding-used-before-actual-source-conversion"));
        }
        public FalloutMainPlayerCellTarget? TargetCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerSourceCell before, FalloutMainPlayerSourcePosition position)
        {
            invocation.Require(FalloutMainPlayerCellStep.TargetCellRead);
            _ = invocation.Owner.ConvertMainPlayerSourceGrid(invocation, position.XBits, position.YBits, FalloutSourceFistpSite.PlayerTargetCell);
            return null; // Authored absent target returns before world/native publication.
        }
        private static Exception Excluded() => new InvalidOperationException("Authored FISTP route reached an excluded source/native child.");
        public void PendingWorldPrelude(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public void ReleasePendingOwnedChild(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public Task<bool> TransferPending(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) => throw Excluded();
        public void StorePendingSceneScalar(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) => throw Excluded();
        public void InvokePendingCallback(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) => throw Excluded();
        public void PendingFurniture(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) => throw Excluded();
        public void RetirePendingDeferredChildren(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public bool PendingFlagQueries(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public void PendingFlagChild(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public bool HeldInterface(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public void HeldChild(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public void SceneClock(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
        public void SceneChild(FalloutMainPlayerCellInvocation invocation, bool alternate) => throw Excluded();
        public Task LoadTarget(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target, FalloutMainPlayerSourcePosition position) => throw Excluded();
        public void SetWorldBracket(FalloutMainPlayerCellInvocation invocation, bool value) => throw Excluded();
        public void SetPlayerBracket(FalloutMainPlayerCellInvocation invocation, bool value) => throw Excluded();
        public void AttachCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target) => throw Excluded();
        public void StoreRoot(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target) => throw Excluded();
        public void OptionalTreeChild(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
    }
}
