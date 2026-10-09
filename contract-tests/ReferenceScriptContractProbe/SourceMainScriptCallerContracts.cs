using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

// Independently authored host inputs exercise the actual shared C# caller,
// samples, current-child tokens and cold process handoff. No retail input,
// platform pump, original context-list producer or native frame is simulated
// as product authority by these contracts.
internal static partial class SourceMainScriptCallerContracts
{
    private const string Engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    internal static void Run()
    {
        SuccessfulOrderAndCold(); FailedSteamPrefixAndCold(); ShortCircuitAndForbiddenReentry(); ActualMissingHost();
        SourceMainUtilityCases();
        RunPlayerCellContracts();
        SourceSkyTransferContracts.Run();
        RunRawPlayerTransferContracts();
        Console.WriteLine("OPENNV_SOURCE_MAIN_SCRIPT_CALLER_PASS authoredScopeOnly=true realTwoSites=true orderedPlayerSteamContexts=true " +
            "independentContextClock=true sourceOrdinalNotDeliveryFrame=true coldNoReplay=true callbackFailurePrefix=true " +
            "nativeSteamAndWholeMain=UNOWNED nativeGameplay=UNEXECUTED");
    }
    private static void SuccessfulOrderAndCold()
    {
        using var warm = new Fixture();
        warm.Host.DuringPlayer = invocation =>
        {
            Require(warm.Owner.Observe().Allows, "First cached sample was replaced by a later live opacity query.");
            warm.Fade.Start(1, 0, false);
            Require(warm.Owner.Observe().Allows, "A Player child changed the cached byte without an actual next sample.");
            Reject(() => invocation.AdvanceContextTime(1));
        };
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(71, .02f).GetAwaiter().GetResult();
        var state = warm.Owner.CaptureMainScriptCaller(); var field = warm.Owner.CaptureMainScriptFrameEvidence();
        Require(state.Calls == 1 && state.LastCall is { Disposition: FalloutMainScriptCallerDisposition.ScopeReturned } &&
            field is { Frame: 1, LastSite: FalloutMainScriptSampleSite.AfterMainChildren, BlocksGameMode: true } &&
            state.ContextTimeWrites == 1 && state.ContextTimeBits == BitConverter.SingleToUInt32Bits(.125f) &&
            warm.Host.Log.IndexOf("Player") < warm.Host.Log.IndexOf("Steam") && warm.Host.Log.IndexOf("Steam") < warm.Host.Log.IndexOf("Timed") &&
            warm.Host.RealChildMutations == 3, "Actual caller changed independent clocks, child order, field samples or real authored effects.");
        var runtime = warm.Owner.Capture();
        using var cold = new Fixture(runtime, field, state, warm.Fade.Capture());
        Require(cold.Host.RealChildMutations == 0 && cold.Owner.CaptureMainScriptCaller().LastCall == state.LastCall &&
            cold.Owner.CaptureMainScriptFrameEvidence() == field && !cold.Owner.Observe().Allows,
            "Cold reconstruction executed a child, cleared its cache or replayed a context-time write.");
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, .03f).GetAwaiter().GetResult();
        var next = cold.Owner.CaptureMainScriptCaller();
        Require(next.Calls == 2 && next.LastCall is { DeliveryFrame: 1, Ordinal: 2 } && next.ContextTimeWrites == 2 &&
            next.ContextTimeBits == BitConverter.SingleToUInt32Bits(.25f), "Cold delivery numbering rewound source invocation/time ownership.");
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainScriptCaller(next with { ContextTimeBits = 0 }));
        Reject(() => FalloutActorProcessRuntimeState.RequireMainScriptSamples(next, field));
        Reject(() => cold.Owner.SampleScriptFrame(3, FalloutMainScriptSampleSite.BeforeMainChildren));
    }
    private static void FailedSteamPrefixAndCold()
    {
        using var warm = new Fixture();
        warm.Host.DuringSteam = _ => throw new IOException("Authored Steam consumer fails after the real Player prefix.");
        using (warm.Bind()) Reject(() => warm.Owner.ExecuteMainScriptCaller(300, .02f).GetAwaiter().GetResult());
        var state = warm.Owner.CaptureMainScriptCaller(); var field = warm.Owner.CaptureMainScriptFrameEvidence();
        Require(state.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed, FailureType: "System.IO.IOException" } &&
            state.LastCall.Children.Last() is { Step: FalloutMainScriptCallerStep.SteamCallbacks, Returned: null, Error: not null } &&
            field is { Frame: 1, LastSite: FalloutMainScriptSampleSite.BeforeMainChildren, Error: null } &&
            state.ContextTimeWrites == 0 && warm.Host.RealChildMutations == 2,
            "A Steam fault discarded Player/entered Steam effects, sampled the second site, or changed another clock.");
        Reject(() => warm.Owner.Observe()); Reject(() => warm.Owner.CaptureScriptFrame());
        var runtime = warm.Owner.Capture();
        using var cold = new Fixture(runtime, field, state);
        using (cold.Bind()) Reject(() => cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
        Require(cold.Host.Log.Count == 0 && cold.Owner.CaptureMainScriptCaller().LastCall == state.LastCall &&
            cold.Owner.MainScriptCallerSaveBlocker is not null, "Cold replayed or silently accepted a failed actual source child.");
        var failedCall = state.LastCall ?? throw new InvalidOperationException("Steam consumer lost its entered failed call.");
        var children = failedCall.Children.Where(child => child.Step != FalloutMainScriptCallerStep.SteamCallbacks).ToArray();
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainScriptCaller(state with
        { LastCall = failedCall with { Children = children, Disposition = FalloutMainScriptCallerDisposition.ScopeReturned, FailureType = null, Error = null } }));
    }
    private static void ShortCircuitAndForbiddenReentry()
    {
        using (var suppressed = new Fixture())
        {
            suppressed.Host.Tab = suppressed.Host.Alt = true;
            using (suppressed.Bind()) suppressed.Owner.ExecuteMainScriptCaller(0, .02f).GetAwaiter().GetResult();
            Require(suppressed.Owner.CaptureMainScriptCaller().LastCall is { Disposition: FalloutMainScriptCallerDisposition.InputSuppressed } &&
                suppressed.Host.Log.SequenceEqual(["Tab", "Alt"]) && suppressed.Owner.CaptureScriptFrame().Frame == 0,
                "OS high-bit early return ran a source child or invented either cached write.");
        }
        using (var menu = new Fixture())
        {
            menu.Host.Menu = true;
            using (menu.Bind()) menu.Owner.ExecuteMainScriptCaller(2, .02f).GetAwaiter().GetResult();
            var call = menu.Owner.CaptureMainScriptCaller();
            Require(!menu.Host.Log.Contains("Gui") && !menu.Host.Log.Contains("Hold") && call.ContextTimeWrites == 0 &&
                call.LastCall is { Disposition: FalloutMainScriptCallerDisposition.ScopeReturned },
                "The real short-circuit read an excluded GUI/Main-held branch or fabricated cumulative context time.");
        }
        using (var retiring = new Fixture())
        {
            retiring.Host.DuringPlayer = _ => Reject(retiring.TryRetireFade);
            using (retiring.Bind()) Reject(() => retiring.Owner.ExecuteMainScriptCaller(4, .02f).GetAwaiter().GetResult());
            Require(retiring.Owner.CaptureMainScriptCaller().LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed } &&
                !retiring.Host.Log.Contains("Steam"), "An entered child swallowed fade retirement and returned an invented Main prefix.");
        }
        using var reentered = new Fixture();
        reentered.Host.DuringPlayer = _ => { Reject(() => reentered.Owner.Capture()); Reject(() => reentered.Owner.ExecuteMainScriptCaller(100, .02f).GetAwaiter().GetResult()); };
        using (reentered.Bind()) Reject(() => reentered.Owner.ExecuteMainScriptCaller(99, .02f).GetAwaiter().GetResult());
        Require(reentered.Owner.CaptureMainScriptCaller().LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed } &&
            reentered.Host.RealChildMutations == 1 && !reentered.Host.Log.Contains("Steam"),
            "A source child swallowed forbidden capture/reentry and returned an invented Main scope.");
    }
    private static void ActualMissingHost()
    {
        using var fixture = new Fixture();
        var source = FalloutMainScriptCallerSource.Read(fixture.Source);
        using (fixture.Owner.BindMainScriptCaller(new FalloutCampaignMainScriptConsumers(source, _ => false), "authored-native-key-provider-only"))
            Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
        var state = fixture.Owner.CaptureMainScriptCaller();
        var failed = state.LastCall ?? throw new InvalidOperationException("Product host lost its refused actual call.");
        Require(failed.Children.Last() is { Step: FalloutMainScriptCallerStep.Prologue, Returned: null } &&
            failed.Error!.Contains("source-Main-platform-utility-current-native-invocation-scope-unbound", StringComparison.Ordinal) &&
            fixture.Owner.CaptureMainScriptFrameEvidence().Frame == 0,
            "Product host fabricated missing original consumers or jumped directly to a cached sample.");
    }
    private sealed class Fixture : IDisposable
    {
        private static FalloutFormKey Key(uint value) => new("Authored.esm", value);
        internal readonly FalloutImmediateScriptSource Source = new(Engine, new('1', 64), FalloutImmediateScriptSource.CurrentContractSha256);
        internal readonly FalloutActorProcessRuntimeState Owner;
        internal readonly FalloutInterfaceFade Fade;
        internal readonly Host Host;
        private readonly IDisposable _fadeLease;
        internal Fixture(FalloutActorProcessRuntimeSnapshot? runtime = null, FalloutMainScriptFrameSnapshot? field = null,
            FalloutMainScriptCallerSnapshot? caller = null, FalloutInterfaceFadeSnapshot? fade = null)
        {
            FalloutCombatActorIdentity Identity(FalloutFormKey key) => new(key, Key(7), "ENGINE_PLAYER", 0, Engine, "NPC_", 0, new('4', 64), true);
            Owner = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(Engine), "authored-source-Main-caller", Key(0x14), Identity, runtime);
            Owner.ConstructScriptFrame(Source, field, caller?.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed });
            Owner.ConstructMainScriptCaller(FalloutMainScriptCallerSource.Read(Source), caller);
            var rows = new[] { new FalloutInterfaceFadeCatalogRow(0, FalloutInterfaceFadeRoot.Primary, "textures/interface/faders/a.dds"),
                new FalloutInterfaceFadeCatalogRow(1, FalloutInterfaceFadeRoot.Secondary, "textures/interface/faders/a.dds"),
                new FalloutInterfaceFadeCatalogRow(2, FalloutInterfaceFadeRoot.Secondary, "textures/interface/faders/b.dds") };
            var source = new FalloutInterfaceFadeSource(new(Engine, FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32, rows), Source.RuntimeSha256,
                rows.Select(row => new FalloutInterfaceFadeTexture(row.Channel, row.TexturePath, new('f', 64))).ToArray(), FalloutInterfaceFadeSource.CurrentContractSha256);
            Fade = new(source, fade); Fade.BindNative(new(_ => { }, _ => { }, _ => { }));
            _fadeLease = Owner.BindScriptFrameFade(Fade); Host = new(FalloutMainScriptCallerSource.Read(Source));
        }
        internal IDisposable Bind() => Owner.BindMainScriptCaller(Host, "authored-source-Main-delivery");
        internal void TryRetireFade() => _fadeLease.Dispose();
        public void Dispose() { _fadeLease.Dispose(); Owner.Dispose(); }
    }
    private sealed class Host(FalloutMainScriptCallerSource source) : IFalloutMainScriptCallerConsumers
    {
        public FalloutMainScriptCallerSource Source => source;
        public string Owner => "authored-actual-callback-host";
        internal readonly List<string> Log = [];
        internal bool Tab, Alt, Menu;
        internal int RealChildMutations;
        internal Action<FalloutMainScriptInvocation>? DuringPlayer, DuringSteam, DuringPrologue;
        internal Func<FalloutMainScriptInvocation, Task>? AwaitPlayer;
        public bool AsyncKeyHighBit(FalloutMainScriptInvocation invocation, int key)
        { invocation.Require(key == 9 ? FalloutMainScriptCallerStep.TabKey : FalloutMainScriptCallerStep.AltKey); Log.Add(key == 9 ? "Tab" : "Alt"); return key == 9 ? Tab : Alt; }
        public void Prologue(FalloutMainScriptInvocation invocation) { invocation.Require(FalloutMainScriptCallerStep.Prologue); Log.Add("Prologue"); DuringPrologue?.Invoke(invocation); }
        public bool MenuGate(FalloutMainScriptInvocation invocation) { Log.Add("Menu"); return Menu; }
        public bool GuiModeTwo(FalloutMainScriptInvocation invocation) { Log.Add("Gui"); return false; }
        public bool FirstInterfacePredicate(FalloutMainScriptInvocation invocation) { Log.Add("First"); return false; }
        public bool FinalInterfacePredicate(FalloutMainScriptInvocation invocation) { Log.Add("Final"); return false; }
        public bool ForeignActiveMenu(FalloutMainScriptInvocation invocation) { Log.Add("Foreign"); return false; }
        public int InterfaceContextKind(FalloutMainScriptInvocation invocation) { Log.Add("Kind"); return 2; }
        public void KindThreePrelude(FalloutMainScriptInvocation invocation) => throw new InvalidOperationException("Excluded authored kind-three branch was invoked.");
        public Task Player(FalloutMainScriptInvocation invocation)
        { invocation.Require(FalloutMainScriptCallerStep.Player); Log.Add("Player"); RealChildMutations++; DuringPlayer?.Invoke(invocation); return AwaitPlayer?.Invoke(invocation) ?? Task.CompletedTask; }
        public void SteamCallbacks(FalloutMainScriptInvocation invocation)
        { invocation.Require(FalloutMainScriptCallerStep.SteamCallbacks); Log.Add("Steam"); RealChildMutations++; DuringSteam?.Invoke(invocation); }
        public bool MainHold(FalloutMainScriptInvocation invocation) { Log.Add("Hold"); return false; }
        public void TimedContexts(FalloutMainScriptInvocation invocation, bool advanceTimer)
        { invocation.Require(FalloutMainScriptCallerStep.TimedContexts); Log.Add("Timed"); RealChildMutations++; if (advanceTimer) invocation.AdvanceContextTime(.125f); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception failure) when (failure is InvalidOperationException or InvalidDataException or NotSupportedException or IOException) { return; }
        throw new InvalidOperationException("Expected actual source Main refusal.");
    }
}
