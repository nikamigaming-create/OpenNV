using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    // Existing pure child fixtures deliberately supply authored cached reads.
    // Product supplies the current timer getter under its actual SceneClock call.
    private static FalloutSourceFrameDeltaRead AuthoredSceneCachedDelta(FalloutStandalonePlayerSceneState scene, uint bits) =>
        new(scene.Source.Main.EngineSha256, scene.Process, 1, bits == 0 ? 0 : 1, bits, bits != 0);

    internal static void RunSceneCachedClockContracts()
    {
        RunTimerReadProvenanceContracts();
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var source = FalloutStandalonePlayerSceneSource.Read(main);
        const string stack = "authored-cached-Player-clock";
        var scene = new FalloutStandalonePlayerSceneState(source, stack, Guid.NewGuid());
        var timerSource = FalloutSourceFrameTimer.Read(new(main.EngineSha256, new('a', 64), new('b', 64), new('c', 64),
            new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
            new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
        using var timer = new FalloutSourceFrameTimerState(timerSource, new CachedSceneCounter(), scene.Process);
        var before = timer.ReadScaledDelta(); var invocation = Guid.NewGuid();
        Require(before is { ScaledSecondsBits: 0, HasReturnedUpdate: false } &&
            scene.StoreSceneClock(invocation, 1, timer.ReadScaledDelta) == 0 && scene.EnterSceneVirtual(invocation, 1) == 0,
            "Player clock replaced actual cached constructor-zero with a later update or host-delivered time.");
        Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1,
            () => throw new InvalidOperationException("authored-stop-after-real-clock-child")));
        var stopped = JsonCachedClock(scene.Capture());
        Require(stopped.Clock is { CachedScaledBits: 0, ArgumentBits: 0 } clock && clock.CachedTimer == before &&
            stopped.SceneBracket == 1 && stopped.Prelude is { FailedAt: FalloutStandaloneScenePreludeStep.OwnedChildRelease },
            "Cached Player clock lost the actual timer receipt or incremented failed prefix.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with
        { Clock = stopped.Clock! with { CachedTimer = before with { Process = Guid.NewGuid() } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with
        { Clock = stopped.Clock! with { CachedScaledBits = 0x3e800000 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with
        { Clock = stopped.Clock! with { CachedTimer = null } }));
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(stopped));
        // A real later source update changes its retained scalar; it never
        // replaces the already committed earlier clock argument/history.
        timer.SetFixedMilliseconds(100); timer.Update();
        var updated = timer.ReadScaledDelta();
        Require(updated.HasReturnedUpdate && updated.Sequence > before.Sequence && updated.ScaledSecondsBits != 0 &&
            scene.Capture().Clock == stopped.Clock,
            "A later Main timer update retroactively changed the earlier Player source clock.");
        var later = new FalloutStandalonePlayerSceneState(source, stack, Guid.NewGuid());
        using var laterTimer = new FalloutSourceFrameTimerState(timerSource, new CachedSceneCounter(), later.Process);
        laterTimer.SetFixedMilliseconds(100); laterTimer.Update();
        var current = laterTimer.ReadScaledDelta();
        Require(later.StoreSceneClock(Guid.NewGuid(), 1, laterTimer.ReadScaledDelta) == current.ScaledSecondsBits &&
            later.Capture().Clock!.CachedTimer == current,
            "Returned cached Main time was replaced with an independent host scalar.");
        later.Retire(); scene.Retire();

        var failedRead = new FalloutStandalonePlayerSceneState(source, stack, Guid.NewGuid()); var reads = 0;
        Reject(() => failedRead.StoreSceneClock(Guid.NewGuid(), 1, () =>
        {
            reads++; throw new InvalidOperationException("authored-cached-timer-getter-failure");
        }));
        var failed = JsonCachedClock(failedRead.Capture());
        Require(reads == 1 && failed.SceneBracket == 1 && failed.Clock is
            { Step: FalloutStandaloneSceneClockStep.Failed, CachedTimer: null, CachedScaledBits: null, ArgumentBits: null } &&
            failed.Prelude is null,
            "A failed timer getter ran before the increment or fabricated the argument/virtual child.");
        failedRead.Retire();
        var reentered = new FalloutStandalonePlayerSceneState(source, stack, Guid.NewGuid()); var duplicate = Guid.NewGuid();
        Reject(() => reentered.StoreSceneClock(duplicate, 1, () =>
        {
            try { reentered.EnterSceneVirtual(duplicate, 1); } catch (InvalidOperationException) { }
            return AuthoredSceneCachedDelta(reentered, 0);
        }));
        Require(reentered.Capture().Clock is { Step: FalloutStandaloneSceneClockStep.Failed, ArgumentBits: null },
            "A swallowed getter reentry refusal permitted the source argument store.");
        reentered.Retire();
        Console.WriteLine("OPENNV_PLAYER_CACHED_CLOCK_PASS actualTimer=true constructorZero=true returnedUpdate=true failedGetterPrefix=true swallowedReentryRefused=true wholeFrame=UNEXECUTED");
    }

    private static FalloutStandalonePlayerSceneSnapshot JsonCachedClock(FalloutStandalonePlayerSceneSnapshot snapshot)
    {
        var saved = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(snapshot))!;
        FalloutStandalonePlayerSceneState.Validate(saved); return saved;
    }
    private sealed class CachedSceneCounter : IFalloutSourceTickCounter
    {
        private uint _now = 1000;
        public string Owner => "authored-source-cached-clock-counter";
        public uint Read() => _now;
        public void Wait(uint milliseconds) => _now = unchecked(_now + milliseconds);
    }
}
