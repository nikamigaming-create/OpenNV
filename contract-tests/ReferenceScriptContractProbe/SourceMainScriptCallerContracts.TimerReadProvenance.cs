using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunTimerReadProvenanceContracts()
    {
        TimerUpdateOverwritesNonfiniteRateIntermediate();
        var source = FalloutSourceFrameTimer.Read(new(FalloutSourceMainFamily.Fallout3, new('a', 64),
            new('b', 64), new('c', 64), new(FalloutSkillPointOperand.Literal(10),
                FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
            new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
        var process = Guid.NewGuid(); var main = Guid.NewGuid();
        var clockOwner = new FalloutSourceFrameDeltaReadOwner(FalloutSourceFrameDeltaReader.SceneClock,
            Guid.NewGuid(), main, main, 1, process);
        var actorOwner = new FalloutSourceFrameDeltaReadOwner(FalloutSourceFrameDeltaReader.ActorTime,
            Guid.NewGuid(), Guid.NewGuid(), main, 1, process);
        using var timer = new FalloutSourceFrameTimerState(source, new CachedSceneCounter(), process);
        var constructorRead = timer.ReadScaledDelta(clockOwner);
        timer.SetFixedMilliseconds(100); timer.Update();
        var updatedRead = timer.ReadScaledDelta(actorOwner);
        timer.SetFixedMilliseconds(50); timer.Update();
        var saved = JsonTimerReads(timer.Capture());
        Require(constructorRead is { ScaledSecondsBits: 0, HasReturnedUpdate: false } &&
            updatedRead is { HasReturnedUpdate: true } &&
            updatedRead.ScaledSecondsBits != BitConverter.SingleToUInt32Bits(saved.ScaledSeconds) &&
            saved.ReadProvenance!.Reads.Count == 2,
            "Timer read history discarded constructor/returned values or equated old reads with the newest scalar.");
        FalloutSourceFrameTimerState.ValidateRetainedRead(saved, constructorRead, clockOwner);
        FalloutSourceFrameTimerState.ValidateRetainedRead(saved, updatedRead, actorOwner);
        foreach (var altered in new[]
        {
            updatedRead with { Epoch = updatedRead.Epoch + 1 },
            updatedRead with { Sequence = saved.Sequence },
            updatedRead with { ScaledSecondsBits = BitConverter.SingleToUInt32Bits(saved.ScaledSeconds) },
            updatedRead with { HasReturnedUpdate = false },
            updatedRead with { Process = Guid.NewGuid() },
            updatedRead with { Owner = actorOwner with { Consumer = Guid.NewGuid() } },
            updatedRead with { Owner = actorOwner with { Main = Guid.NewGuid() } },
            updatedRead with { Owner = null },
        }) Reject(() => FalloutSourceFrameTimerState.ValidateRetainedRead(saved, altered, actorOwner));
        var retained = saved.ReadProvenance!.Reads.Single(row => row.Read.Owner!.Reader == FalloutSourceFrameDeltaReader.ActorTime);
        Reject(() => FalloutSourceFrameTimerState.Validate(saved with
        {
            ReadProvenance = saved.ReadProvenance! with
            {
                Reads = saved.ReadProvenance!.Reads.Select(row => row == retained ? row with
                { Interval = row.Interval with { Closed = updatedRead.Sequence } } : row).ToArray()
            }
        }));
        Reject(() => FalloutSourceFrameTimerState.Validate(saved with
        { ReadProvenance = saved.ReadProvenance! with { Current = saved.ReadProvenance!.Current with { Bits = 0 } } }));
        Reject(() => FalloutSourceFrameTimerState.Validate(saved with { ReadProvenance = null }));
        Reject(() => FalloutSourceFrameTimerState.Validate(saved with
        { ReadProvenance = saved.ReadProvenance! with { Reads = [retained, retained] } }));
        Reject(() => timer.ReadScaledDelta(actorOwner with { Process = Guid.NewGuid() }));

        // Source writers replace the current interval, not a growing update
        // journal. Reads replace exactly one receipt per concrete consumer.
        for (var index = 0; index < 32; index++)
        {
            timer.Update();
            var before = timer.Capture().Sequence; var nextMain = Guid.NewGuid();
            timer.ReadScaledDelta(clockOwner with { Main = nextMain, Invocation = nextMain, Ordinal = index + 2 });
            Require(timer.Capture().Sequence == before && timer.Capture().ReadProvenance!.Reads.Count == 2,
                "Typed read advanced source time or appended an unbounded per-frame ledger.");
        }
        // Preserve the independently retained old Actor-time read even after
        // many writers replaced the current value/clock consumer receipt.
        saved = JsonTimerReads(timer.Capture());
        FalloutSourceFrameTimerState.ValidateRetainedRead(saved, updatedRead, actorOwner);
        Reject(() => FalloutSourceFrameTimerState.ValidateRetainedRead(saved, constructorRead, clockOwner));

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var coldProcess = Guid.NewGuid();
            using var cold = new FalloutSourceFrameTimerState(source, new CachedSceneCounter(), coldProcess, saved);
            var rebound = JsonTimerReads(cold.Capture());
            Require(rebound.Sequence == saved.Sequence && rebound.Updated == saved.Updated &&
                rebound.Epoch == saved.Epoch && rebound.ReadProvenance!.Publications.Count == cycle + 2 &&
                JsonSerializer.Serialize(rebound.ReadProvenance!.Reads) == JsonSerializer.Serialize(saved.ReadProvenance!.Reads),
                "Cold timer replayed a writer, relabelled historical reads or omitted its actual new process publication.");
            FalloutSourceFrameTimerState.ValidateRetainedRead(rebound, updatedRead, actorOwner);
            Reject(() => FalloutSourceFrameTimerState.Validate(rebound with
            {
                ReadProvenance = rebound.ReadProvenance! with
                {
                    Publications = rebound.ReadProvenance!.Publications.Select((row, index) => index == cycle + 1
                        ? row with { PreviousProcess = Guid.NewGuid() } : row).ToArray()
                }
            }));
            var next = Guid.NewGuid();
            var freshClock = clockOwner with { Consumer = Guid.NewGuid(), Main = next, Invocation = next,
                Ordinal = cycle + 34, Process = coldProcess };
            var fresh = cold.ReadScaledDelta(freshClock);
            FalloutSourceFrameTimerState.ValidateRetainedRead(cold.Capture(), fresh, freshClock);
            cold.Update(); saved = JsonTimerReads(cold.Capture());
        }

        ActualTimerReadParent();
        Console.WriteLine("OPENNV_TIMER_READ_PROVENANCE_PASS actualWriterIntervals=true retainedReaders=2 " +
            "olderValuesPreserved=true coldCycles=3 actualSceneClockGuard=true gameplay=UNEXECUTED");
    }

    private static void TimerUpdateOverwritesNonfiniteRateIntermediate()
    {
        var engine = FalloutSourceMainFamily.Fallout3;
        var source = FalloutSourceFrameTimer.Read(new(engine, new('a', 64), new('b', 64), new('c', 64),
            new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
            new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
        using var timer = new FalloutSourceFrameTimerState(source, new CachedSceneCounter(), Guid.NewGuid());
        timer.SetUseRateWrites(false);
        timer.SetRate(0, true);
        timer.Update();
        timer.SetRate(-2, true);
        Reject(() => timer.Capture());
        timer.Update();
        var saved = JsonTimerReads(timer.Capture());
        var interval = saved.ReadProvenance!.Current;
        Require(interval.Operation == "cached-timer-update" &&
            !float.IsFinite(BitConverter.UInt32BitsToSingle(interval.BeforeBits)) &&
            float.IsFinite(BitConverter.UInt32BitsToSingle(interval.Bits)),
            "The actual returned update lost the zero-rate intermediate or failed to overwrite it.");
        Reject(() => FalloutSourceFrameTimerState.Validate(saved with
        { ReadProvenance = saved.ReadProvenance with { Current = interval with { Bits = 0x7fc00000 } } }));
        Reject(() => FalloutSourceFrameTimerState.Validate(saved with
        { ReadProvenance = saved.ReadProvenance with { Current = interval with { Operation = "source-rate-adjustment" } } }));
    }

    private static FalloutSourceFrameTimerSnapshot JsonTimerReads(FalloutSourceFrameTimerSnapshot snapshot)
    {
        var saved = JsonSerializer.Deserialize<FalloutSourceFrameTimerSnapshot>(JsonSerializer.Serialize(snapshot))!;
        FalloutSourceFrameTimerState.Validate(saved); return saved;
    }

    private static void ActualTimerReadParent()
    {
        using var fixture = new StandaloneFixture();
        var receipt = new FalloutAdvancementRuntimeReceipt(fixture.Host.Source.EngineSha256, new('a', 64), new('b', 64), new('c', 64),
            new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
            new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk"));
        var source = FalloutSourceFrameTimer.Read(receipt);
        fixture.Owner.ConfigureSourceCachedTimer(new(source, "authored-source-timer-INI",
            new("bChangeTimeMultSlowly:General", FalloutIniCollection.Main, 1), true, "authored-current-timer-setting"));
        fixture.Owner.PrepareSourceGlobalTime(FalloutGlobalTimeSource.Read(receipt));
        fixture.Owner.PublishPreparedSourceGlobalTime();
        var nativeRate = new FalloutGlobalTimeNativeValues(1, 1);
        var nativeRetired = false;
        using var globalTimeLease = fixture.Owner.RequireSourceGlobalTime().BindPresentation(new(fixture,
            "authored-timer-read-global-simulation-and-audio",
            () => !nativeRetired ? nativeRate : throw new InvalidOperationException("Authored rate owner retired."),
            publication => nativeRate = new(publication.CurrentRate, publication.AudioRate), () => nativeRetired = true));
        fixture.BindPlayerCachedTimerRead(_ => throw new NotSupportedException("authored-stop-after-real-SceneClock-read"));
        using var binding = fixture.Bind();
        Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, .25f).GetAwaiter().GetResult());
        var saved = fixture.Owner.Capture();
        FalloutActorProcessRuntimeState.Validate(saved);
        var scene = saved.StandaloneMain!.Scene;
        var clock = scene.Clock!; var read = clock.CachedTimer!;
        Require(read.Owner is { Reader: FalloutSourceFrameDeltaReader.SceneClock } owner &&
            owner.Consumer == scene.Constructor && owner.Main == clock.Main &&
            owner.Invocation == clock.Main && owner.Process == saved.CapturedProcess,
            "Actual SceneClock guard did not bind the typed read to its real Scene/Main constructor.");
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with
        {
            StandaloneMain = saved.StandaloneMain with
            { Scene = scene with { Clock = clock with { CachedTimer = read with { Epoch = read.Epoch + 1 } } } }
        }));
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with
        { CachedTimer = saved.CachedTimer! with { ReadProvenance = saved.CachedTimer!.ReadProvenance! with { Reads = [] } } }));
    }
}
