using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunGlobalTime()
    {
        var source = FalloutGlobalTimeSource.Read(SandboxRuntimeReceipt(
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57"));
        var counter = new AuthoredSandboxTickCounter { Now = 1000 };
        using var timer = new FalloutSourceFrameTimerState(source.Timer, counter, Guid.NewGuid());
        using var owner = new FalloutGlobalTimeState(source, timer);
        var actual = new GlobalTimeAuthoredPresentation();
        using var native = owner.BindPresentation(actual.Binding());
        owner.Set(0.1);
        Require(timer.ReadRates().Current == 0.1f && timer.ReadRates().Target == 0.1f &&
            actual.Simulation == 0.1f && actual.Audio == .25f,
            "Global time lost the Float32 timer writer or independent quarter-rate audio floor.");
        owner.Set(2, int.MinValue);
        Require(actual.Simulation == 2 && actual.Audio == 1,
            "Global sound flag narrowed nonzero signed source integers or changed the simulation target.");
        timer.SetRate(.5f, true); owner.RefreshPresentation();
        Require(actual.Simulation == .5 && actual.Audio == 1,
            "An independent genuine timer writer failed to reach the retained audio-policy consumer.");
        owner.Set(.125f);
        var clock = timer.Capture(); var snapshot = owner.Capture(clock);
        using (var coldTimer = new FalloutSourceFrameTimerState(source.Timer,
            new AuthoredSandboxTickCounter { Now = 90000 }, Guid.NewGuid(), Copy(clock)))
        using (var cold = new FalloutGlobalTimeState(source, coldTimer, Copy(snapshot)))
        {
            Require(cold.SaveBlocker == "source-global-time-presentation-unbound",
                "Cold global time restored a synthetic native publication lease.");
            var freshNative = new GlobalTimeAuthoredPresentation();
            using var rebound = cold.BindPresentation(freshNative.Binding());
            var recaptured = cold.Capture(coldTimer.Capture());
            Require(freshNative.Simulation == .125f && freshNative.Audio == .25f &&
                recaptured.Revision == snapshot.Revision && recaptured.Sequence == snapshot.Sequence &&
                recaptured.CapturedProcess != snapshot.CapturedProcess && actual.Simulation == .125f,
                "Cold global time replayed the command or changed an independent warm owner.");
            Reject(() => new FalloutGlobalTimeState(source, coldTimer, snapshot with { CurrentRate = .25f }));
        }
        var before = JsonSerializer.Serialize(owner.Capture(timer.Capture()));
        foreach (var number in new double[] { double.NaN, double.PositiveInfinity, double.MaxValue })
            Reject(() => owner.Set(number));
        foreach (var flag in new double[] { .5, double.NaN, (double)int.MaxValue + 1 })
            Reject(() => owner.Set(1, flag));
        Require(JsonSerializer.Serialize(owner.Capture(timer.Capture())) == before,
            "Malformed source arguments entered or changed a real rate writer.");
        var primary = FalloutGlobalTimeCommands.Function("SetGlobalTimeMultiplier", owner)!;
        Require(ReferenceEquals(primary, FalloutGlobalTimeCommands.Function("sgtm", owner)) &&
            FalloutGlobalTimeCommands.Function("SetGlobalTimeMultiplier", null) is null,
            "Global command aliases lost their actual shared invocation or invented an absent owner.");
        GlobalTimeReadFailurePrefix(source);
        GlobalTimeThroughReader(source);
        GlobalTimeCurrentCaptureJoins(source);
        GlobalTimeFailedNativeBindingRetirement(source);
        RunGlobalTimeFallout3();
        Console.WriteLine("OPENNV_SOURCE_GLOBAL_TIME_PASS timer=true audioPolicy=true cold=true compiled=true prefix=true nativeSdk=unrun");
    }

    private static void GlobalTimeReadFailurePrefix(FalloutGlobalTimeSource source)
    {
        using var timer = new FalloutSourceFrameTimerState(source.Timer,
            new AuthoredSandboxTickCounter { Now = 10 }, Guid.NewGuid());
        using var owner = new FalloutGlobalTimeState(source, timer);
        var actual = new GlobalTimeAuthoredPresentation();
        var lease = owner.BindPresentation(actual.Binding());
        try
        {
            actual.FailAudio = true;
            Reject(() => owner.Set(.5f, -1));
            var history = JsonSerializer.Serialize(owner.State);
            Require(timer.ReadRates().Current == .5f && actual.Simulation == .5 && actual.Audio == 1 &&
                owner.SaveBlocker == "source-global-time-command-failed" && history.Contains("authored-audio-failure", StringComparison.Ordinal),
                "A failed second native consumer erased source timer/audio-byte or its entered publication.");
            var writes = actual.Writes;
            Reject(() => owner.Set(2));
            Require(actual.Writes == writes, "Entered native failure replayed its source command.");
        }
        finally { lease.Dispose(); }
        Require(actual.Retirements == 1 && actual.Simulation == .7 && actual.Audio == .9f,
            "Failed publication lost the genuine independent property-retirement owner.");
    }

    private static void GlobalTimeThroughReader(FalloutGlobalTimeSource source)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-global-time-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var change = Call(0x1186, Join(U16(2), BoundQueryDouble(.125), Integer(-3)));
            BoundQueryFixture(directory, Join(change, Set('f', 1, " 7")), records =>
            {
                using var world = new FalloutReferenceWorld(records);
                var quests = new FalloutQuestState(records);
                using var timer = new FalloutSourceFrameTimerState(source.Timer,
                    new AuthoredSandboxTickCounter { Now = 100 }, Guid.NewGuid());
                using var state = new FalloutGlobalTimeState(source, timer);
                var actual = new GlobalTimeAuthoredPresentation();
                using var native = state.BindPresentation(actual.Binding());
                var scripts = GlobalTimeExecutor(records, world, quests, state);
                var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                foreach (var _ in scripts.CompiledSteps(Key(0x20), program, program.Events[0])) { }
                Require(quests.Variable(Key(0x20), 1) == 7 && timer.ReadRates().Current == .125f && actual.Audio == 1,
                    "Actual full-reader SCDA did not call the shared rate owner or its suffix.");
                var emptyQuests = new FalloutQuestState(records);
                Reject(() => { foreach (var _ in GlobalTimeExecutor(records, world, emptyQuests, null)
                    .CompiledSteps(Key(0x20), program, program.Events[0])) { } });
                Require(emptyQuests.Variable(Key(0x20), 1) == 0,
                    "Missing global-time owner manufactured command success or committed its suffix.");
            });
            foreach (var bad in new[] { Call(0x1186, U16(0)), Call(0x1186, Join(U16(3), BoundQueryDouble(1), Integer(0), Integer(0))),
                Call(0x1186, Join(U16(2), BoundQueryDouble(1), BoundQueryDouble(.5))) })
                BoundQueryFixture(directory, Join(bad, Set('f', 1, " 99")), records =>
                {
                    using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
                    using var timer = new FalloutSourceFrameTimerState(source.Timer,
                        new AuthoredSandboxTickCounter { Now = 100 }, Guid.NewGuid());
                    using var state = new FalloutGlobalTimeState(source, timer);
                    var actual = new GlobalTimeAuthoredPresentation();
                    using var native = state.BindPresentation(actual.Binding());
                    var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                    Reject(() => { foreach (var _ in GlobalTimeExecutor(records, world, quests, state)
                        .CompiledSteps(Key(0x20), program, program.Events[0])) { } });
                    Require(timer.ReadRates().Current == 1 && quests.Variable(Key(0x20), 1) == 0,
                        "Malformed SCDA global-time arguments changed rate or committed a suffix.");
                });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static FalloutReferenceScripts GlobalTimeExecutor(FalloutPluginStack records,
        FalloutReferenceWorld world, FalloutQuestState quests, FalloutGlobalTimeState? state) => new(records, world, quests,
            new((_, _) => throw new InvalidDataException("Rate fixture requested unrelated furniture."),
                _ => throw new InvalidDataException("Rate fixture requested unrelated presentation."), GlobalTime: state));

    // These authored property cells verify C# owner ordering only. They are
    // explicitly not a Godot/native playback or SDK acceptance substitute.
    private sealed class GlobalTimeAuthoredPresentation
    {
        internal double Simulation = .7;
        internal float Audio = .9f;
        internal int Writes, Retirements;
        internal bool FailAudio;
        internal FalloutGlobalTimeNativeBinding Binding() => new(this, "authored-global-rate-properties",
            () => new(Simulation, Audio), publication =>
            {
                ++Writes; Simulation = publication.CurrentRate;
                if (FailAudio) throw new InvalidOperationException("authored-audio-failure");
                Audio = publication.AudioRate;
            }, () => { ++Retirements; Simulation = .7; Audio = .9f; });
    }
}
