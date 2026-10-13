using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunRendererClockContracts()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        FalloutRendererClockConfiguration Configuration(int mode) => new(FalloutRendererClockSource.Reviewed(main), "authored-INI",
            new(new("iUpdateType:HAVOK", FalloutIniCollection.Main, 0), mode, "authored-update-type"),
            new(new("fChaseDeltaMult:HAVOK", FalloutIniCollection.Main, BitConverter.SingleToUInt32Bits(.05f)), .05f, "authored-chase"));
        const float quantum = .016666668f;
        uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        void Publish(FalloutRendererClockState clock, long ordinal, float delta, float rate = 1, bool complexScene = false) =>
            clock.Publish(Guid.NewGuid(), ordinal, delta, rate, quantum, complexScene);
        using (var clock = new FalloutRendererClockState(Configuration(0), Guid.NewGuid()))
        {
            var fresh = clock.Capture();
            Require(clock.SaveBlocker is not null && fresh.InputBits == Bits(.016f) && fresh.PreviousBits == Bits(.032f) &&
                fresh.RemainderBits == 0 && fresh.Steps == 0, "Renderer clock replaced its actual loaded source cells with generic delta defaults.");
            Publish(clock, 1, .003f);
            var deferred = clock.Capture();
            Require(deferred.Steps == 0 && deferred.DeltaBits == 0 && deferred.AccumulatedBits == 0 && deferred.RemainderBits == Bits(.003f),
                "A below-half-quantum frame became a physics step or lost its retained source remainder.");
            Publish(clock, 2, .007f);
            var small = clock.Capture();
            Require(small.Steps == 1 && small.DeltaBits == Bits(.01f) && small.RemainderBits == 0,
                "The source small-step branch did not consume the prior deferred frame.");
            Publish(clock, 3, .2f);
            var capped = clock.Capture();
            Require(capped.Steps == 3 && capped.DeltaBits == Bits(quantum) && capped.RemainderBits == Bits(quantum),
                "Fixed physics publication lost its three-step cap or capped remainder.");
            Publish(clock, 4, .1f, complexScene: true);
            var complexScene = clock.Capture();
            Require(complexScene.Steps == 1 && complexScene.LastPublication!.SceneIsComplex,
                "Scene complexity was aliased to menu pause or lost the independent one-step source cap.");
            var saved = JsonSerializer.Deserialize<FalloutRendererClockSnapshot>(JsonSerializer.Serialize(complexScene))!;
            using var cold = new FalloutRendererClockState(Configuration(0), Guid.NewGuid(), saved);
            var restored = cold.Capture();
            Require(restored.CapturedProcess != saved.CapturedProcess && restored with { CapturedProcess = saved.CapturedProcess } == saved &&
                cold.SaveBlocker is null, "Cold renderer construction replayed a physics publication or reset retained fields.");
            Publish(cold, 5, 0);
            Require(cold.Capture().Publications == saved.Publications + 1, "The next cold Main publisher did not retain source call history.");
            Reject(() => new FalloutRendererClockState(Configuration(0), saved.CapturedProcess, saved));
            Reject(() => new FalloutRendererClockState(Configuration(1), Guid.NewGuid(), saved));
            Reject(() => FalloutRendererClockState.Validate(saved with { MainMode = 4 }));
            Reject(() => FalloutRendererClockState.Validate(saved with { Steps = 2 }));
        }
        foreach (var mode in new[] { 1, 3, 9, -1, 11 })
        {
            using var clock = new FalloutRendererClockState(Configuration(mode), Guid.NewGuid());
            Publish(clock, 1, .004f);
            Require(clock.Capture() is { Steps: 0, AccumulatedBits: 0, DeltaBits: 0 }, "Adaptive physics failed to defer a short source frame.");
            Publish(clock, 2, .008f);
            Require(clock.Capture().Steps == 1 && clock.Capture().DeltaBits == Bits(.012f), "Adaptive physics discarded its prior remainder.");
            Publish(clock, 3, .12f);
            Require(clock.Capture().Steps == 3 && clock.Capture().DeltaBits == Bits(.04f), "Adaptive physics failed to divide the capped source substeps.");
        }
        using (var clock = new FalloutRendererClockState(Configuration(2), Guid.NewGuid()))
        {
            Publish(clock, 1, 200);
            Require(clock.Capture().DeltaBits == Bits(200) && clock.Capture().Steps == 1 &&
                clock.Capture().AccumulatedBits == Bits(clock.Configuration.Source.MaximumAccumulatedSeconds),
                "Raw physics incorrectly consumed the clamped accumulated value instead of its original argument.");
            Publish(clock, 2, -1);
            Require(clock.Capture().Steps == 0 && clock.Capture().DeltaBits == 0 && clock.Capture().InputBits == Bits(-1),
                "The nonpositive source arm lost its independent raw input or invented a substep.");
        }
        using (var clock = new FalloutRendererClockState(Configuration(10), Guid.NewGuid()))
        {
            Publish(clock, 1, .1f);
            // Each source SSE operation rounds separately; the final store is
            // one ULP above rounding the decimal expression only once.
            Require(clock.Capture().PreviousBits == 0x3d10ff98 && clock.Capture().DeltaBits == 0x3d10ff98 && clock.Capture().Steps == 1,
                "Smoothing skipped the source loaded previous value or Float32 chase stores.");
        }
        using (var clock = new FalloutRendererClockState(Configuration(0), Guid.NewGuid()))
        {
            Publish(clock, 1, .02f, rate: 0);
            Require(clock.Capture().Steps == 3 && clock.Capture().DeltaBits == 0,
                "A zero source rate was replaced by a unit multiplier instead of the signed-indefinite/unsigned-cap path.");
            Publish(clock, 2, .02f, rate: -.5f);
            Require(clock.Capture().Steps == 3 && clock.Capture().DeltaBits == Bits(-quantum / 2),
                "A negative source rate was clamped before the actual fixed-step consumer.");
            Reject(() => Publish(clock, 3, float.PositiveInfinity));
            var failed = clock.Capture();
            Require(clock.SaveBlocker is not null && failed.LastPublication is { Failure: not null, Prefix: FalloutRendererClockPrefix.InputStored },
                "Failed renderer arithmetic discarded its entered prefix or admitted a current save.");
            Reject(() => Publish(clock, 4, .02f));
            Reject(() => new FalloutRendererClockState(Configuration(0), Guid.NewGuid(), failed));
        }
        Console.WriteLine("OPENNV_RENDERER_CLOCK_CONTRACT_PASS modes=true sourceRemainder=true complexScene=true signedRates=true failedPrefix=true serializedCold=true noReplay=true");
    }
}
