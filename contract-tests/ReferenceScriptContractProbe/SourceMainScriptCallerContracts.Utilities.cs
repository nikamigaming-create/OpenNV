using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void SourceMainUtilityCases()
    {
        UtilitySignedQueueAndCold(); UtilityFormattingFailedPrefix(); UtilityMutableQueue(); UtilityFailedPrefix(); UtilityCallbackCommitAndFailure();
        Console.WriteLine("OPENNV_SOURCE_MAIN_UTILITIES_PASS authoredPlatformOnly=true realUtilityOrder=true " +
            "duplicateSignedFIFO=true falseApiObservations=true nestedQueue=true cacheBeforeCallbackFault=true " +
            "fourByteFormatting=true formatterBeforeFreshQuery=true coldNoReplay=true nativeSteam=UNEXECUTED gameplay=UNEXECUTED");
    }
    private static void UtilitySignedQueueAndCold()
    {
        using var warm = new Fixture();
        var platform = new UtilityPlatform(warm.Owner.MainUtilitySource);
        var commands = new UtilityCommands(warm.Owner.MainUtilitySource);
        warm.Host.DuringPrologue = warm.Owner.ExecuteMainUtilities;
        using (warm.Owner.BindMainUtilityPlatform(platform))
        using (warm.Owner.BindMainUtilityCommandHost(commands))
        {
            foreach (var id in new[] { 1, 1, -1, 99, 101 }) warm.Owner.AddSourceAchievement(id);
            using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
        }
        var utility = warm.Owner.CaptureMainUtilities();
        var frame = utility.LastFrame ?? throw new InvalidOperationException("Actual utility frame is absent.");
        Require(platform.Achievements.SequenceEqual(["A01", "A01", "A-1", "A99"]) &&
            commands.Range.SequenceEqual([101]) && utility.Pending.Count == 0 && utility.ClearedThrough == 5 &&
            utility.LastCleared.All(request => request.PayloadRetired is not null) && platform.StatisticsQueries == 10 &&
            platform.UserQueries == 0 && frame.Returned && frame.Effects.Last().Step == FalloutMainUtilityStep.ThirdNoOp &&
            frame.Effects.Where(effect => effect.Step is FalloutMainUtilityStep.SetAchievement or FalloutMainUtilityStep.StoreStatistics)
                .All(effect => effect.Boolean == false),
            "Source utility lost signed duplicates, repeated interface queries, payload/queue retirement, or genuine false API observations.");
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainUtilities(utility with
        { LastFrame = frame with { Effects = frame.Effects.Select(effect => effect.Step == FalloutMainUtilityStep.PlatformRunning ? effect with { Boolean = null } : effect).ToArray() } }));
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainUtilities(utility with
        { LastFrame = frame with { Effects = frame.Effects.Select(effect => effect.Step == FalloutMainUtilityStep.SetAchievement ? effect with { Argument = "A77" } : effect).ToArray() } }));
        var runtime = warm.Owner.Capture(); var field = warm.Owner.CaptureMainScriptFrameEvidence();
        var main = warm.Owner.CaptureMainScriptCaller();
        using var cold = new Fixture(runtime, field, main, warm.Fade.Capture());
        cold.Owner.RestoreMainUtilities(utility);
        var restored = cold.Owner.CaptureMainUtilities();
        Require(restored.LastFrame == frame && restored.LastCleared.SequenceEqual(utility.LastCleared) &&
            restored.Pending.Count == 0 && cold.Host.Log.Count == 0 && platform.Achievements.Count == 4,
            "Cold utility reconstruction replayed platform calls, rewrote payloads or fabricated a source callback.");
    }
    private static void UtilityFormattingFailedPrefix()
    {
        foreach (var wide in new[] { 100, -10, int.MinValue })
        {
            using var fixture = new Fixture();
            var platform = new UtilityPlatform(fixture.Owner.MainUtilitySource);
            var commands = new UtilityCommands(fixture.Owner.MainUtilitySource);
            fixture.Host.DuringPrologue = fixture.Owner.ExecuteMainUtilities;
            using (fixture.Owner.BindMainUtilityPlatform(platform))
            using (fixture.Owner.BindMainUtilityCommandHost(commands))
            using (fixture.Bind())
            {
                fixture.Owner.AddSourceAchievement(1); fixture.Owner.AddSourceAchievement(wide);
                fixture.Owner.AddSourceAchievement(2);
                Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
            }
            var state = fixture.Owner.CaptureMainUtilities();
            Require(platform.StatisticsQueries == 3 && platform.Achievements.SequenceEqual(["A01"]) &&
                commands.Range.Count == 0 && state.ClearedThrough == 0 && !state.LoginConstructed &&
                state.Pending is [{ Id: 1, PayloadRetired: not null }, { PayloadRetired: not null }, { Id: 2, PayloadRetired: null }] &&
                state.Pending[1].Id == wide && state.LastFrame is { Returned: false, FailureType: "System.NotSupportedException" } &&
                state.LastFrame.Effects.Last() is { Step: FalloutMainUtilityStep.FormatAchievement, Returned: null, Argument: null },
                "Four-byte formatter overflow queried the second interface, widened/truncated an award, replayed a retired payload or cleared its genuine prefix.");
            var field = fixture.Owner.CaptureMainScriptFrameEvidence(); var main = fixture.Owner.CaptureMainScriptCaller();
            using var cold = new Fixture(fixture.Owner.Capture(), field, main);
            cold.Owner.RestoreMainUtilities(state);
            using (cold.Bind()) Reject(() => cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
            Require(cold.Owner.CaptureMainUtilities().LastFrame == state.LastFrame && cold.Host.Log.Count == 0,
                "Cold reconstructed a guessed formatter handler or replayed the original failure prefix.");
        }
    }
    private static void UtilityMutableQueue()
    {
        using var fixture = new Fixture();
        var platform = new UtilityPlatform(fixture.Owner.MainUtilitySource) { Running = true, LoggedOn = true };
        var commands = new UtilityCommands(fixture.Owner.MainUtilitySource);
        fixture.Host.DuringPrologue = fixture.Owner.ExecuteMainUtilities;
        using (fixture.Owner.BindMainUtilityPlatform(platform))
        using (fixture.Owner.BindMainUtilityCommandHost(commands))
        using (fixture.Bind())
        {
            platform.DuringSet = () => { if (platform.Achievements.Count == 1) fixture.Owner.AddSourceAchievement(2); };
            fixture.Owner.AddSourceAchievement(1);
            fixture.Owner.RegisterSourceLoginCallback("authored-nested-source-callback", (context, loggedOn) =>
            {
                Require(context == 0, "Original login callback changed its neutral context.");
                if (loggedOn)
                {
                    fixture.Owner.AddSourceAchievement(3);
                    fixture.Owner.RegisterSourceLoginCallback("authored-source-unregister", null);
                }
            });
            fixture.Owner.ExecuteMainScriptCaller(7, .02f).GetAwaiter().GetResult();
            var first = fixture.Owner.CaptureMainUtilities();
            Require(platform.Achievements.SequenceEqual(["A01", "A02"]) && first.Pending is [{ Id: 3, PayloadRetired: null }] &&
                first.Callback is { Registered: false, Registration: 2 } && first.LoggedOn && first.Registrations == 2,
                "Nested source writers lost mutable traversal, revived an old callback pointer or prematurely cleared a later login request.");
            fixture.Owner.ExecuteMainScriptCaller(8, .02f).GetAwaiter().GetResult();
            Require(platform.Achievements.SequenceEqual(["A01", "A02", "A03"]) && fixture.Owner.CaptureMainUtilities().Pending.Count == 0,
                "The next genuine Main invocation replayed an earlier source request or lost the pending login callback request.");
        }
    }
    private static void UtilityFailedPrefix()
    {
        using var fixture = new Fixture();
        var platform = new UtilityPlatform(fixture.Owner.MainUtilitySource)
        { DuringStore = () => throw new IOException("Authored platform store fails after the actual SetAchievement return.") };
        var commands = new UtilityCommands(fixture.Owner.MainUtilitySource);
        fixture.Host.DuringPrologue = fixture.Owner.ExecuteMainUtilities;
        using (fixture.Owner.BindMainUtilityPlatform(platform))
        using (fixture.Owner.BindMainUtilityCommandHost(commands))
        using (fixture.Bind())
        {
            fixture.Owner.AddSourceAchievement(4);
            Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
        }
        var state = fixture.Owner.CaptureMainUtilities();
        Require(state.Pending is [{ Id: 4, PayloadRetired: not null }] && state.ClearedThrough == 0 &&
            state.LastFrame is { Returned: false, FailureType: "System.IO.IOException" } &&
            state.LastFrame.Effects.Last() is { Step: FalloutMainUtilityStep.StoreStatistics, Returned: null } &&
            !state.LoginConstructed && platform.Achievements.SequenceEqual(["A04"]),
            "Store fault cleared or replayed a retired source payload, skipped the committed API prefix, or constructed later children.");
        var main = fixture.Owner.CaptureMainScriptCaller(); var field = fixture.Owner.CaptureMainScriptFrameEvidence();
        using var cold = new Fixture(fixture.Owner.Capture(), field, main);
        cold.Owner.RestoreMainUtilities(state);
        using (cold.Bind()) Reject(() => cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
        Require(cold.Owner.CaptureMainUtilities().LastFrame == state.LastFrame && cold.Host.Log.Count == 0 &&
            cold.Owner.MainUtilitySaveBlocker is not null, "Cold replayed the entered failed platform frame.");
    }
    private static void UtilityCallbackCommitAndFailure()
    {
        using var fixture = new Fixture();
        var platform = new UtilityPlatform(fixture.Owner.MainUtilitySource) { Running = true, LoggedOn = true };
        fixture.Host.DuringPrologue = fixture.Owner.ExecuteMainUtilities;
        var calls = 0;
        fixture.Owner.RegisterSourceLoginCallback("authored-platform-login", (context, value) =>
        {
            calls++; Require(context == 0, "Login callback context is not zero.");
            if (value) throw new IOException("Authored login callback fails after the committed cache write.");
        });
        using (fixture.Owner.BindMainUtilityPlatform(platform))
        using (fixture.Bind()) Reject(() => fixture.Owner.ExecuteMainScriptCaller(10, .02f).GetAwaiter().GetResult());
        var state = fixture.Owner.CaptureMainUtilities();
        Require(calls == 2 && state.LoggedOn && state.LoginChanged > 0 && state.Callback is { Registered: true } &&
            state.LastFrame is { Returned: false, FailureType: "System.IO.IOException" } &&
            state.LastFrame.Effects.Last() is { Step: FalloutMainUtilityStep.ChangedCallback, Returned: null } &&
            !state.ThirdConstructed, "A login fault discarded its cache commit or invoked later source children.");
        using var cold = new Fixture(fixture.Owner.Capture(), fixture.Owner.CaptureMainScriptFrameEvidence(), fixture.Owner.CaptureMainScriptCaller());
        Reject(() => cold.Owner.RestoreMainUtilities(state));
        Require(calls == 2, "Cold invented an original callback reconstruction or replayed the callback.");
    }
    private sealed class UtilityPlatform(FalloutMainUtilitySource source) : IFalloutMainUtilityPlatform, IFalloutMainUtilityStatistics, IFalloutMainUtilityUser
    {
        public FalloutMainUtilitySource Source => source;
        public string Owner => "authored-explicit-platform-lifetime";
        internal readonly List<string> Achievements = [];
        internal bool Running { get; init; }
        internal bool LoggedOn { get; init; }
        internal int StatisticsQueries, UserQueries;
        internal Action? DuringSet, DuringStore;
        public bool IsSteamRunning() => Running;
        public IFalloutMainUtilityUser? SteamUser() { UserQueries++; return this; }
        public IFalloutMainUtilityStatistics? SteamUserStats() { StatisticsQueries++; return this; }
        public bool BLoggedOn() => LoggedOn;
        public bool SetAchievement(string identifier) { Achievements.Add(identifier); DuringSet?.Invoke(); return false; }
        public bool StoreStats() { DuringStore?.Invoke(); return false; }
    }
    private sealed class UtilityCommands(FalloutMainUtilitySource source) : IFalloutMainUtilityCommandHost
    {
        public FalloutMainUtilitySource Source => source;
        public string Owner => "authored-original-command-suppression-and-console";
        internal readonly List<int> Range = [];
        public bool AchievementSuppressionByte() => false;
        public void OutOfRangeAchievement(int id) => Range.Add(id);
    }
}
