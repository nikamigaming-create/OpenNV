using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerCellContracts()
    {
        PendingSlotReplacement(); SourceContainment(); AwaitedPlayerOrder(); AwaitedPlayerFailure(); PlayerCellCurrentAndCold(); RunPendingConsumerContracts();
        RunStandaloneMainContracts();
        RunPlayerCellPreparationContracts();
        Console.WriteLine("OPENNV_SOURCE_MAIN_PLAYER_CELL_PASS singlePendingSlot=true supersededOwnerRefused=true " +
            "awaitedPlayerBeforeSteam=true sameThread=true sourceDataFlags=true fistpCoordinates=true " +
            "interiorSkipsRounding=true coldNoReplay=true nativeGameplay=UNEXECUTED unknownChildren=RETAINED");
    }
    private static void PendingSlotReplacement()
    {
        static FalloutPlayerMove Move(uint destination) => new(new("Authored.esm", 0x14), new("Authored.esm", destination), 1, 2, 3);
        var slot = new FalloutPlayerPendingSlot();
        var first = slot.Store(FalloutPlayerPendingKind.MoveTo, Move(30), null, "authored-first-script-statement");
        var second = slot.Store(FalloutPlayerPendingKind.MoveTo, Move(31), null, "authored-second-script-statement");
        Require(slot.Next == second && slot.Capture().Replacement is { } replacement && replacement.Released == first.Identity &&
            replacement.Stored == second.Identity, "Player setter retained two FIFO destinations instead of the actual replacement/null-slot ownership.");
        Reject(() => slot.Complete(first, "wrong-superseded-owner"));
        var cold = new FalloutPlayerPendingSlot(); cold.Restore(slot.Capture());
        Require(cold.Next == second, "Cold pending slot replayed the superseded command or discarded the exact unentered request.");
        cold.Complete(second, "actual-authored-last-consumer"); Require(!cold.Pending && cold.Capture().Completion?.Identity == second.Identity,
            "Actual pending null store did not retain its own request identity.");
        var entered = new FalloutPlayerPendingSlot();
        var payload = entered.Store(FalloutPlayerPendingKind.MoveTo, Move(40), null, "authored-entered-request");
        using (entered.Enter(payload))
        {
            Reject(() => entered.Store(FalloutPlayerPendingKind.MoveTo, Move(41), null, "forbidden-concurrent-writer"));
            Reject(() => entered.Capture());
            Reject(() => entered.Retire());
        }
        Require(entered.Capture().Pending == payload && entered.Error is not null,
            "Refused concurrent mutation or retirement lost the still-owned payload and its original failure.");
        entered.Retire(); Require(entered.Capture().Pending == payload && entered.Error is not null,
            "Retirement invented a successful source null store for failed work.");
    }
    private static void SourceContainment()
    {
        var source = new FalloutCellProcessIdentity(new("Authored.esm", 50), 1, new('a', 64), new("Authored.esm", 2), new('b', 64));
        var cell = new FalloutMainPlayerSourceCell(source, 0, 1, 0);
        var near = new FalloutMainPlayerSourcePosition(BitConverter.SingleToUInt32Bits(4095.75f), 0, 0,
            new(FalloutSourceFloatRounding.NearestEven, "authored-original-rounding-nearest"));
        Require(FalloutActorProcessRuntimeState.MainPlayerPositionContained(cell, near),
            "Player containment used the record header master bit or Floor(position/4096) instead of DATA flags/FISTP.");
        Require(!FalloutActorProcessRuntimeState.MainPlayerPositionContained(cell,
            near with { Rounding = new(FalloutSourceFloatRounding.Down, "authored-original-rounding-down") }),
            "Independent original rounding modes were silently equated.");
        var unowned = near with { Rounding = new(null, "actual-original-rounding-unowned") };
        Reject(() => FalloutActorProcessRuntimeState.MainPlayerPositionContained(cell, unowned));
        Require(!FalloutActorProcessRuntimeState.MainPlayerPositionContained(cell with { CellFlags = 1 }, unowned),
            "Original interior short circuit consumed unrelated rounding inputs.");
        var negative = cell with { X = -1 };
        Require(FalloutActorProcessRuntimeState.MainPlayerPositionContained(negative,
            near with { XBits = BitConverter.SingleToUInt32Bits(-.6f) }), "Player source cell conversion lost signed-shift negative coordinates.");
    }
    private static void AwaitedPlayerOrder()
    {
        using var fixture = new Fixture();
        var thread = Environment.CurrentManagedThreadId;
        var completion = new TaskCompletionSource();
        fixture.Host.AwaitPlayer = _ => completion.Task;
        using var lease = fixture.Bind();
        var call = fixture.Owner.ExecuteMainScriptCaller(8, .02f);
        Require(!call.IsCompleted && !fixture.Host.Log.Contains("Steam") && !fixture.Host.Log.Contains("Timed"),
            "Main ran later source children before the actual retained Player child returned.");
        completion.SetResult(); call.GetAwaiter().GetResult();
        Require(Environment.CurrentManagedThreadId == thread && fixture.Host.Log.IndexOf("Player") < fixture.Host.Log.IndexOf("Steam"),
            "Actual source Main continuation changed its owning thread or original child order.");
        var snapshot = fixture.Owner.CaptureMainScriptCaller();
        Require(snapshot.LastCall!.Children.Single(child => child.Step == FalloutMainScriptCallerStep.Player).Returned is { } returned &&
            snapshot.LastCall.Children.Single(child => child.Step == FalloutMainScriptCallerStep.SteamCallbacks).Entered > returned,
            "Awaited original Player return was recorded after a later native child.");
    }
    private static void PlayerCellCurrentAndCold()
    {
        using var fixture = new Fixture();
        var slot = new FalloutPlayerPendingSlot(); var source = FalloutMainPlayerCellSource.Read(fixture.Host.Source);
        fixture.Owner.ConstructMainPlayerCell(source, slot, null);
        var consumer = new AuthoredPlayerCell(source); fixture.Host.AwaitPlayer = fixture.Owner.ExecuteMainPlayerCell;
        using (fixture.Owner.BindMainPlayerCell(consumer, "authored-actual-player-lifetime"))
        using (fixture.Bind()) fixture.Owner.ExecuteMainScriptCaller(21, .02f).GetAwaiter().GetResult();
        var player = fixture.Owner.CaptureMainPlayerCell(); var caller = fixture.Owner.CaptureMainScriptCaller();
        FalloutActorProcessRuntimeState.RequireMainPlayerCellCaller(player, caller);
        Require(player.LastCall is { Disposition: FalloutMainPlayerCellDisposition.CellUnchanged } &&
            consumer.Log.SequenceEqual(new[] { "SceneMode", "SceneRead", "ParentCellRead", "PositionRead" }),
            "Original interior Player branch consumed scene, rounding, target or unrelated world inputs.");
        var broken = player with { LastCall = player.LastCall! with { Children = player.LastCall.Children
            .Where(child => child.Step != FalloutMainPlayerCellStep.PositionRead).ToArray() } };
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainPlayerCell(broken));
        using var cold = new Fixture(fixture.Owner.Capture(), fixture.Owner.CaptureMainScriptFrameEvidence(), caller, fixture.Fade.Capture());
        cold.Owner.ConstructMainPlayerCell(source, new FalloutPlayerPendingSlot(), player);
        var restored = cold.Owner.CaptureMainPlayerCell();
        Require(restored.CapturedProcess != player.CapturedProcess && restored.Calls == player.Calls && restored.LastCall == player.LastCall &&
            restored.ColdHandoff?.PreviousProcess == player.CapturedProcess, "Cold Player child replayed source consumers or omitted the genuine new process.");
    }
    private static void AwaitedPlayerFailure()
    {
        using var fixture = new Fixture(); var completion = new TaskCompletionSource();
        fixture.Host.AwaitPlayer = _ => completion.Task;
        using var lease = fixture.Bind();
        var call = fixture.Owner.ExecuteMainScriptCaller(5, .02f);
        completion.SetException(new IOException("authored-actual-await-child-failure"));
        Reject(() => call.GetAwaiter().GetResult());
        Require(!fixture.Host.Log.Contains("Steam") && !fixture.Host.Log.Contains("Timed"),
            "Failed awaited Player child allowed later original source consumers to run.");
        var captured = fixture.Owner.CaptureMainScriptCaller();
        Require(captured.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed } &&
            captured.LastCall.Children.Single(child => child.Step == FalloutMainScriptCallerStep.Player) is { Returned: null,
                Error: "authored-actual-await-child-failure" }, "Main did not retain the exact actual failed Player prefix.");
        Reject(() => fixture.Owner.ExecuteMainScriptCaller(6, .02f).GetAwaiter().GetResult());
    }
    private sealed class AuthoredPlayerCell(FalloutMainPlayerCellSource source) : IFalloutMainPlayerCellConsumers
    {
        public FalloutMainPlayerCellSource Source => source;
        public string Owner => "authored-interior-Player-source-child";
        internal List<string> Log { get; } = [];
        private void Read(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellStep step)
        { invocation.Require(step); Log.Add(step.ToString()); }
        private static Exception Excluded() => new InvalidOperationException("An excluded independently authored Player source arm was entered.");
        public bool SceneMode(FalloutMainPlayerCellInvocation invocation) { Read(invocation, FalloutMainPlayerCellStep.SceneMode); return false; }
        public bool ScenePresent(FalloutMainPlayerCellInvocation invocation) { Read(invocation, FalloutMainPlayerCellStep.SceneRead); return false; }
        public FalloutMainPlayerSourceCell? ParentCell(FalloutMainPlayerCellInvocation invocation)
        { Read(invocation, FalloutMainPlayerCellStep.ParentCellRead); return new(new(new("Authored.esm", 10), 0, new('c', 64), null, null), 1, null, null); }
        public FalloutMainPlayerSourcePosition Position(FalloutMainPlayerCellInvocation invocation)
        { Read(invocation, FalloutMainPlayerCellStep.PositionRead); return new(0, 0, 0, new(null, "authored-rounding-deliberately-unowned-on-interior-arm")); }
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
        public FalloutMainPlayerCellTarget? TargetCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerSourceCell before, FalloutMainPlayerSourcePosition position) => throw Excluded();
        public Task LoadTarget(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target, FalloutMainPlayerSourcePosition position) => throw Excluded();
        public void SetWorldBracket(FalloutMainPlayerCellInvocation invocation, bool value) => throw Excluded();
        public void SetPlayerBracket(FalloutMainPlayerCellInvocation invocation, bool value) => throw Excluded();
        public void AttachCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target) => throw Excluded();
        public void StoreRoot(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target) => throw Excluded();
        public void OptionalTreeChild(FalloutMainPlayerCellInvocation invocation) => throw Excluded();
    }
}
